<div align="center">

# Mutualzz is developed at [git.mutualzz.com](https://git.mutualzz.com/mutualzz/rpc-csharp)

**GitHub is a read-only mirror. Issues and pull requests opened on GitHub are not monitored.**

Sign in with your GitHub account. No new password needed.

</div>

# Mutualzz RPC (C# / .NET)

.NET client for Mutualzz desktop Rich Presence over local IPC.

Requires the **Mutualzz desktop app** to be running.

Protocol: [`PROTOCOL.md`](https://git.mutualzz.com/mutualzz/rpc/src/branch/master/PROTOCOL.md)

## Install

```bash
dotnet add package Mutualzz.Rpc
```

From this monorepo:

```bash
dotnet build packages/rpc-csharp/Mutualzz.Rpc.csproj
```

## Usage

```csharp
using Mutualzz.Rpc;

using var rpc = new MutualzzRpc(new MutualzzRpcOptions
{
    ClientId = "your-app-id",
});

await rpc.ConnectAsync();

await rpc.SetActivityAsync(RpcActivity.Playing(
    name: "My Game",
    details: "Ranked",
    state: "In a Match",
    start: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
));

await rpc.ClearActivityAsync();
```

`MutualzzRpc` implements `IDisposable` — disposing disconnects and clears presence.

## Example

```bash
dotnet run --project packages/rpc-csharp/examples/Example
```
