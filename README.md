# Mutualzz.Rpc (C# / .NET)

.NET client for Mutualzz desktop Rich Presence over local IPC.

Protocol: [`../rpc/PROTOCOL.md`](../rpc/PROTOCOL.md)

## Install

```bash
dotnet add package Mutualzz.Rpc
```

Published via [`.github/workflows/release-rpc.yml`](../../.github/workflows/release-rpc.yml) using NuGet Trusted Publishing. Version must match `packages/rpc/package.json`.

From this monorepo:

```bash
dotnet build packages/rpc-csharp/Mutualzz.Rpc.csproj
dotnet pack packages/rpc-csharp/Mutualzz.Rpc.csproj -c Release
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
