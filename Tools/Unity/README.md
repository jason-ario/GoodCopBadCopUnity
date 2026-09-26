# Unity CLI

Headless compile, test and build for this project. Run from the repo root in PowerShell or cmd:

```powershell
.\Tools\Unity\unity.cmd info                     # editor path, version, is the project open?
.\Tools\Unity\unity.cmd compile                  # compile check; prints C# errors, exit 0 = clean
.\Tools\Unity\unity.cmd test                     # EditMode tests (Assets/_GoodCopBadCop/_Scripts/Editor/Tests)
.\Tools\Unity\unity.cmd test -Platform PlayMode
.\Tools\Unity\unity.cmd test -Filter PopulationServiceTests
.\Tools\Unity\unity.cmd build                    # Win64 -> Builds/StandaloneWindows64/
.\Tools\Unity\unity.cmd build -Development -Output D:\Builds\Test\Game.exe
.\Tools\Unity\unity.cmd open                     # open the Editor normally
.\Tools\Unity\unity.cmd install-editor           # install the exact version via Unity Hub
.\Tools\Unity\unity.cmd log -Name compile        # tail a log
```

Notes:

- The editor version comes from `ProjectSettings/ProjectVersion.txt`. Override detection with `$env:UNITY_EDITOR`.
- Batch mode can't run while the project is open in the Editor. The script detects that and stops (exit 11); close the Editor or use Unity MCP instead.
- Logs and test results go to `Logs/cli/` (git-ignored). Builds go to `Builds/` (git-ignored).
- Exit codes: `test` returns Unity's code (0 pass, 2 failures, 3 run error); 10 = editor not found; 11 = project open.
- Builds use `GoodCopBadCop.Cli.CommandLineBuild.Build` (`Assets/_GoodCopBadCop/_Scripts/Editor/CommandLineBuild.cs`) and the enabled scenes in Build Settings.
