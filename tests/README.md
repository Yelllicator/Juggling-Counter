# Managed offline integration verification

These tests invoke the installed game's actual managed APIs and Harmony patches.
They do not start the game, render UI, use saves, or load any live combat state.
Fixtures use `RuntimeHelpers.GetUninitializedObject` and reflection only to supply
synthetic public player/hand/power data without Godot, RNG or save initialization.

With the .NET 9 SDK installed, replace the example game path and run from the project root:

```powershell
$env:STS2_GAME_DIR = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
.\build.ps1
dotnet build .\tests\STS2Count.Tests.csproj -c Release --no-dependencies
dotnet .\tests\bin\Release\net9.0\STS2Count.Tests.dll .\tests\verification.json
```

The test project references `../STS2Count.csproj`. The `--no-dependencies` build
option uses the already built mod without rebuilding or repackaging it. Game DLLs
are copied only into the test build output; they do not belong in `dist`.
Without an explicit report-path argument, the executable writes to
`tests/verification.json` relative to its own build output, independently of the
current working directory. `dotnet run --project <absolute-test-project-path>
-c Release` also works and builds dependencies normally.

Coverage: real native manifest parsing; five Harmony targets and idempotent
registration; patched native display properties; stack/counter preservation;
actual first/second/fourth attack callbacks; skill and other-player exclusion;
actual turn-end reset and event notifications; throwing display subscribers;
highlight phase, hand, owner, red-glow, canonical, missing-power and count bounds.

Limits: the third-attack cloning/animation path, `AfterApplied` history lookup,
actual `NHandCardHolder` getter execution, and rendered counter/glow require the
game runtime and are not claimed as validated here. Their patch targets are
checked against the actual DLL. The game's managed Sentry module can print that
its GDExtension is absent; this is expected outside Godot.
