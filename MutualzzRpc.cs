using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mutualzz.Rpc;

public sealed class MutualzzRpcOptions
{
  public required string ClientId { get; set; }
  public int ConnectTimeoutMs { get; set; } = 5000;
  public string? Path { get; set; }
}

public sealed class MutualzzRpc : IDisposable
{
  private const int OpcodeHandshake = 0;
  private const int OpcodeFrame = 1;
  private const int OpcodeClose = 2;
  private const int OpcodePing = 3;
  private const int OpcodePong = 4;
  private const int MaxSlots = 10;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };

  private readonly MutualzzRpcOptions _options;
  private Stream? _stream;
  private readonly MemoryStream _readBuffer = new();
  private bool _ready;

  public MutualzzRpc(MutualzzRpcOptions options)
  {
    if (string.IsNullOrWhiteSpace(options.ClientId))
      throw new ArgumentException("clientId is required", nameof(options));
    _options = options;
  }

  public bool IsConnected => _stream is not null && _ready;
  public string? Path { get; private set; }

  public async Task ConnectAsync(CancellationToken cancellationToken = default)
  {
    if (IsConnected)
      return;

    Disconnect();

    var (stream, path) = await OpenStreamAsync(cancellationToken)
      .ConfigureAwait(false);
    _stream = stream;
    Path = path;

    await WriteFrameAsync(
        OpcodeHandshake,
        JsonSerializer.Serialize(
          new { clientId = _options.ClientId.Trim() },
          JsonOptions
        ),
        cancellationToken
      )
      .ConfigureAwait(false);

    await ReadUntilAsync(
        cmd => cmd == "READY",
        expectedNonce: null,
        cancellationToken
      )
      .ConfigureAwait(false);

    _ready = true;
  }

  public async Task SetActivityAsync(
    RpcActivity? activity,
    int? pid = null,
    CancellationToken cancellationToken = default
  )
  {
    if (!IsConnected)
      await ConnectAsync(cancellationToken).ConfigureAwait(false);

    var nonce = Guid.NewGuid().ToString("N");
    var payload = new
    {
      cmd = "SET_ACTIVITY",
      nonce,
      args = new
      {
        pid = pid ?? Environment.ProcessId,
        activity,
      },
    };

    await WriteFrameAsync(
        OpcodeFrame,
        JsonSerializer.Serialize(payload, JsonOptions),
        cancellationToken
      )
      .ConfigureAwait(false);

    await ReadUntilAsync(
        cmd => cmd == "SET_ACTIVITY",
        expectedNonce: nonce,
        cancellationToken
      )
      .ConfigureAwait(false);
  }

  public Task ClearActivityAsync(
    int? pid = null,
    CancellationToken cancellationToken = default
  ) => SetActivityAsync(null, pid, cancellationToken);

  public void Disconnect()
  {
    _ready = false;
    try
    {
      if (_stream is not null)
      {
        WriteFrameAsync(OpcodeClose, "{}", CancellationToken.None)
          .GetAwaiter()
          .GetResult();
      }
    }
    catch { }

    _stream?.Dispose();
    _stream = null;
    Path = null;
    _readBuffer.SetLength(0);
  }

  public void Dispose() => Disconnect();

  private async Task<(Stream stream, string path)> OpenStreamAsync(
    CancellationToken cancellationToken
  )
  {
    if (!string.IsNullOrWhiteSpace(_options.Path))
    {
      var stream = await ConnectPathAsync(_options.Path, cancellationToken)
        .ConfigureAwait(false);
      return (stream, _options.Path);
    }

    Exception? last = null;
    for (var slot = 0; slot < MaxSlots; slot++)
    {
      var path = PipePath(slot);
      try
      {
        var stream = await ConnectPathAsync(path, cancellationToken)
          .ConfigureAwait(false);
        return (stream, path);
      }
      catch (Exception ex)
      {
        last = ex;
      }
    }

    throw new IOException(
      "No Mutualzz RPC pipe found (is the Mutualzz desktop app running?)",
      last
    );
  }

  private async Task<Stream> ConnectPathAsync(
    string path,
    CancellationToken cancellationToken
  )
  {
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken
    );
    timeoutCts.CancelAfter(_options.ConnectTimeoutMs);
    var ct = timeoutCts.Token;

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      var pipe = new NamedPipeClientStream(
        ".",
        path.Replace(@"\\.\pipe\", "", StringComparison.OrdinalIgnoreCase),
        PipeDirection.InOut,
        PipeOptions.Asynchronous
      );
      await pipe.ConnectAsync(ct).ConfigureAwait(false);
      return pipe;
    }

    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    try
    {
      await socket
        .ConnectAsync(new UnixDomainSocketEndPoint(path), ct)
        .ConfigureAwait(false);
      return new NetworkStream(socket, ownsSocket: true);
    }
    catch
    {
      socket.Dispose();
      throw;
    }
  }

  private static string PipePath(int slot) =>
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
      ? $@"\\.\pipe\mutualzz-ipc-{slot}"
      : $"/tmp/mutualzz-ipc-{slot}";

  private async Task WriteFrameAsync(
    int opcode,
    string json,
    CancellationToken cancellationToken
  )
  {
    if (_stream is null)
      throw new InvalidOperationException("Mutualzz RPC socket is not connected");

    var body = Encoding.UTF8.GetBytes(json);
    var frame = new byte[8 + body.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), (uint)opcode);
    BinaryPrimitives.WriteUInt32LittleEndian(
      frame.AsSpan(4, 4),
      (uint)body.Length
    );
    Buffer.BlockCopy(body, 0, frame, 8, body.Length);
    await _stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
  }

  private async Task ReadUntilAsync(
    Func<string, bool> cmdPredicate,
    string? expectedNonce,
    CancellationToken cancellationToken
  )
  {
    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      while (TryReadFrame(out var opcode, out var payload))
      {
        if (opcode == OpcodePing)
        {
          await WriteFrameAsync(
              OpcodePong,
              string.IsNullOrWhiteSpace(payload) ? "{}" : payload,
              cancellationToken
            )
            .ConfigureAwait(false);
          continue;
        }

        if (opcode == OpcodeClose)
          throw new IOException("host closed connection");

        if (opcode != OpcodeFrame)
          continue;

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("cmd", out var cmdEl))
          continue;

        var cmd = cmdEl.GetString();
        if (cmd is null || !cmdPredicate(cmd))
          continue;

        if (expectedNonce is not null)
        {
          if (
            !doc.RootElement.TryGetProperty("nonce", out var nonceEl)
            || nonceEl.GetString() != expectedNonce
          )
          {
            continue;
          }
        }

        return;
      }

      await ReadMoreAsync(cancellationToken).ConfigureAwait(false);
    }
  }

  private bool TryReadFrame(out int opcode, out string payload)
  {
    opcode = 0;
    payload = "";
    if (!_readBuffer.TryGetBuffer(out var segment) || segment.Count < 8)
      return false;

    var span = segment.AsSpan();
    opcode = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4));
    var payloadLength = (int)
      BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4));
    if (segment.Count < 8 + payloadLength)
      return false;

    payload = Encoding.UTF8.GetString(segment.Array!, segment.Offset + 8, payloadLength);

    var remaining = segment.Count - (8 + payloadLength);
    if (remaining > 0)
    {
      var leftover = new byte[remaining];
      Buffer.BlockCopy(
        segment.Array!,
        segment.Offset + 8 + payloadLength,
        leftover,
        0,
        remaining
      );
      _readBuffer.SetLength(0);
      _readBuffer.Write(leftover, 0, leftover.Length);
    }
    else
    {
      _readBuffer.SetLength(0);
    }

    return true;
  }

  private async Task ReadMoreAsync(CancellationToken cancellationToken)
  {
    if (_stream is null)
      throw new InvalidOperationException("not connected");

    var buffer = new byte[8192];
    var read = await _stream
      .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
      .ConfigureAwait(false);
    if (read <= 0)
      throw new EndOfStreamException("Mutualzz RPC connection closed");
    _readBuffer.Write(buffer, 0, read);
  }
}
