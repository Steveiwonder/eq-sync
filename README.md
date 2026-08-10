# EQ Sync

EQ Sync is a Windows .NET 10 tray app for synchronizing EverQuest settings across PCs.

## V1 Goals

- Detect separate EverQuest and EverQuest Legends installs.
- Track user-owned settings, character UI files, `userdata`, `AudioTriggers`, `maps`, and `uifiles`.
- Avoid patch-managed game files.
- Discover peers on the local network.
- Use manual preview/apply sync with newest-wins planning.
- Block sync while EverQuest or LaunchPad is running.
- Keep transport boundaries ready for a future remote HTTP server.

## Projects

- `src/EqSync.Core`: install discovery, sync rules, manifests, planning, backups, LAN contracts.
- `src/EqSync.App`: WPF tray app.
- `tests/EqSync.Core.Tests`: unit tests for sync behavior.

## Build

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

## Sync

Run EQ Sync on both PCs, with EverQuest and LaunchPad closed on both sides.

1. Select the local EverQuest or EverQuest Legends install.
2. Select the LAN peer.
3. Click **Preview Sync**.
4. Review the planned file actions.
5. Click **Apply Sync**.

The app copies newer files in both directions and creates backups before overwriting local files.
