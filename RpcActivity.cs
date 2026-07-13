using System.Text.Json.Serialization;

namespace Mutualzz.Rpc;

public sealed class RpcTimestamps
{
  [JsonPropertyName("start")]
  public long? Start { get; set; }

  [JsonPropertyName("end")]
  public long? End { get; set; }
}

public sealed class RpcActivity
{
  [JsonPropertyName("name")]
  public required string Name { get; set; }

  [JsonPropertyName("details")]
  public string? Details { get; set; }

  [JsonPropertyName("state")]
  public string? State { get; set; }

  [JsonPropertyName("type")]
  public string Type { get; set; } = "playing";

  [JsonPropertyName("applicationId")]
  public string? ApplicationId { get; set; }

  [JsonPropertyName("timestamps")]
  public RpcTimestamps? Timestamps { get; set; }

  public static RpcActivity Playing(
    string name,
    string? details = null,
    string? state = null,
    long? start = null
  ) =>
    new()
    {
      Name = name,
      Details = details,
      State = state,
      Type = "playing",
      Timestamps = start is null ? null : new RpcTimestamps { Start = start },
    };
}
