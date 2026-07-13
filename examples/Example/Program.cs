using Mutualzz.Rpc;

var rpc = new MutualzzRpc(new MutualzzRpcOptions { ClientId = "csharp-example" });

await rpc.ConnectAsync();
Console.WriteLine($"Connected to {rpc.Path}");

await rpc.SetActivityAsync(
  RpcActivity.Playing(
    "C# Example",
    details: "Ranked",
    state: "In a Match",
    start: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
  )
);

Console.WriteLine("Holding 30s…");
await Task.Delay(TimeSpan.FromSeconds(30));

await rpc.ClearActivityAsync();
rpc.Dispose();
