<#
.SYNOPSIS
    Command-line wrapper for the GoodCopBadCop Unity project.

.DESCRIPTION
    Finds the Unity Editor version the project uses (ProjectSettings/ProjectVersion.txt)
    and runs it in batch mode. Logs and test results go to Logs/cli (git-ignored).

    Commands:
      info                 Show detected editor, Hub, version and whether the project is open.
      compile              Import + compile scripts headlessly; prints C# errors. Exit 0 = clean.
      test                 Run Unity Test Framework tests (-Platform EditMode|PlayMode, -Filter).
      build                Build the player (-Target Win64 by default, -Development, -Output).
      open                 Open the project in the Unity Editor (normal GUI).
      install-editor       Install the project's editor version through Unity Hub's CLI.
      log                  Print the tail of the last CLI log (-Name compile|test-EditMode|build...).

.EXAMPLE
    .\Tools\Unity\unity.ps1 compile
    .\Tools\Unity\unity.ps1 test -Platform EditMode -Filter PopulationServiceTests
    .\Tools\Unity\unity.ps1 build -Development
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('info', 'compile', 'test', 'build', 'open', 'install-editor', 'log', 'help')]
    [string]$Command = 'help',

    [ValidateSet('EditMode', 'PlayMode')]
    [string]$Platform = 'EditMode',

    [string]$Filter,

    [ValidateSet('Win64', 'Win', 'OSXUniversal', 'Linux64')]
    [string]$Target = 'Win64',

    [switch]$Development,

    [string]$Output,

    [string]$Name,

    [int]$Tail = 60,

    # Pass extra modules to install-editor, e.g. -Modules windows-il2cpp
    [string[]]$Modules = @()
)

$ErrorActionPreference = 'Stop'

# --- Paths ----------------------------------------------------------------

$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$LogDir = Join-Path $ProjectPath 'Logs\cli'
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir | Out-Null }

function Get-ProjectVersion {
    $file = Join-Path $ProjectPath 'ProjectSettings\ProjectVersion.txt'
    $text = Get-Content $file -Raw
    $version = ([regex]::Match($text, 'm_EditorVersion:\s*(\S+)')).Groups[1].Value
    $changeset = ([regex]::Match($text, 'm_EditorVersionWithRevision:\s*\S+\s*\(([0-9a-f]+)\)')).Groups[1].Value
    return [pscustomobject]@{ Version = $version; Changeset = $changeset }
}

function Find-UnityHub {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Unity Hub\Unity Hub.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Unity Hub\Unity Hub.exe')
    )
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return $c } }
    return $null
}

function Find-UnityEditor([string]$Version) {
    if ($env:UNITY_EDITOR -and (Test-Path $env:UNITY_EDITOR)) { return $env:UNITY_EDITOR }

    $roots = New-Object System.Collections.Generic.List[string]
    $hubData = Join-Path $env:APPDATA 'UnityHub'

    # Custom Hub install location
    $secondary = Join-Path $hubData 'secondaryInstallPath.json'
    if (Test-Path $secondary) {
        $p = (Get-Content $secondary -Raw).Trim().Trim('"')
        if ($p) { $roots.Add($p) }
    }
    $roots.Add((Join-Path $env:ProgramFiles 'Unity\Hub\Editor'))
    $roots.Add('C:\Program Files\Unity\Hub\Editor')

    foreach ($r in $roots) {
        $exe = Join-Path $r "$Version\Editor\Unity.exe"
        if (Test-Path $exe) { return $exe }
    }

    # Editors registered with Hub manually ("Locate")
    foreach ($f in @('editors-v2.json', 'editors.json')) {
        $path = Join-Path $hubData $f
        if (Test-Path $path) {
            $raw = Get-Content $path -Raw
            foreach ($m in [regex]::Matches($raw, '"([^"]*?Unity\.exe)"')) {
                $exe = $m.Groups[1].Value -replace '\\\\', '\'
                if ($exe -like "*$Version*" -and (Test-Path $exe)) { return $exe }
            }
        }
    }
    return $null
}

function Test-ProjectOpen {
    # The Editor holds an exclusive lock on Temp/UnityLockfile while the project is open.
    $lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try {
        $fs = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        return $false
    } catch {
        return $true
    }
}

function Quote([string]$s) { return '"' + $s + '"' }

# --- Running Unity --------------------------------------------------------

function Invoke-Unity([string[]]$UnityArgs, [string]$LogName) {
    $ver = Get-ProjectVersion
    $editor = Find-UnityEditor $ver.Version
    if (-not $editor) {
        Write-Host "Unity $($ver.Version) not found." -ForegroundColor Red
        Write-Host "Install it with:  .\Tools\Unity\unity.ps1 install-editor"
        Write-Host "or point to it:    `$env:UNITY_EDITOR = 'C:\path\to\Unity.exe'"
        exit 10
    }
    if (Test-ProjectOpen) {
        Write-Host 'The project is open in the Unity Editor. Batch mode cannot open it at the same time.' -ForegroundColor Yellow
        Write-Host 'Close the Editor (or use Unity MCP / the Editor itself) and try again.'
        exit 11
    }

    $logFile = Join-Path $LogDir "$LogName.log"
    if (Test-Path $logFile) { Remove-Item $logFile -Force }

    $all = @('-batchmode', '-nographics', '-projectPath', (Quote $ProjectPath), '-logFile', (Quote $logFile)) + $UnityArgs
    $argLine = $all -join ' '

    Write-Host "Unity $($ver.Version): $argLine" -ForegroundColor DarkGray
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $editor -ArgumentList $argLine -PassThru -NoNewWindow
    $null = $proc.Handle  # cache the handle so ExitCode is available after exit

    # Show progress lines from the log while Unity runs.
    $lastLen = 0
    while (-not $proc.HasExited) {
        Start-Sleep -Seconds 3
        if (Test-Path $logFile) {
            try {
                $lines = Get-Content $logFile -ErrorAction SilentlyContinue
                if ($lines -and $lines.Count -gt $lastLen) {
                    $new = $lines[$lastLen..($lines.Count - 1)]
                    $lastLen = $lines.Count
                    $new | Where-Object { $_ -match 'error CS\d+|Compilation|\[CLI|Build (succeeded|completed|failed)|Running tests|Test run|Exiting batchmode' } |
                        ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
                }
            } catch { }
        }
    }
    $proc.WaitForExit()
    $sw.Stop()
    $code = $proc.ExitCode
    Write-Host ("Unity exited with code {0} after {1:mm\:ss}. Log: {2}" -f $code, $sw.Elapsed, $logFile)
    return [pscustomobject]@{ ExitCode = $code; LogFile = $logFile }
}

function Show-CompileMessages([string]$LogFile) {
    if (-not (Test-Path $LogFile)) { return 0 }
    $lines = Get-Content $LogFile
    $errors = $lines | Where-Object { $_ -match 'error CS\d+' } | Sort-Object -Unique
    $warnings = $lines | Where-Object { $_ -match 'warning CS\d+' -and $_ -match '_GoodCopBadCop' } | Sort-Object -Unique
    if ($errors) {
        Write-Host "`n$(@($errors).Count) compile error(s):" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }
    if ($warnings) {
        Write-Host "`n$(@($warnings).Count) warning(s) in Assets/_GoodCopBadCop:" -ForegroundColor Yellow
        $warnings | Select-Object -First 30 | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    }
    return @($errors).Count
}

function Show-TestResults([string]$ResultsFile) {
    if (-not (Test-Path $ResultsFile)) {
        Write-Host 'No test results file was written (tests may not have started - check the log).' -ForegroundColor Red
        return
    }
    [xml]$xml = Get-Content $ResultsFile -Raw
    $run = $xml.'test-run'
    $color = 'Green'
    if ([int]$run.failed -gt 0) { $color = 'Red' }
    Write-Host ("`nTests: {0} total, {1} passed, {2} failed, {3} skipped ({4}s)" -f `
        $run.total, $run.passed, $run.failed, $run.skipped, [math]::Round([double]$run.duration, 1)) -ForegroundColor $color
    $failed = $xml.SelectNodes("//test-case[@result='Failed']")
    foreach ($t in $failed) {
        Write-Host "  FAIL $($t.fullname)" -ForegroundColor Red
        $msg = $t.failure.message.'#cdata-section'
        if (-not $msg) { $msg = $t.failure.message }
        if ($msg) { Write-Host ("       " + ($msg.Trim() -split "`n")[0]) -ForegroundColor Red }
    }
    Write-Host "Results: $ResultsFile"
}

# --- Commands -------------------------------------------------------------

switch ($Command) {
    'help' {
        Get-Help $PSCommandPath -Detailed | Out-String | Write-Host
    }

    'info' {
        $ver = Get-ProjectVersion
        $editor = Find-UnityEditor $ver.Version
        $hub = Find-UnityHub
        Write-Host "Project:      $ProjectPath"
        Write-Host "Unity:        $($ver.Version) ($($ver.Changeset))"
        if ($editor) { Write-Host "Editor:       $editor" -ForegroundColor Green }
        else { Write-Host 'Editor:       NOT FOUND (run install-editor or set $env:UNITY_EDITOR)' -ForegroundColor Red }
        if ($hub) { Write-Host "Unity Hub:    $hub" } else { Write-Host 'Unity Hub:    not found' -ForegroundColor Yellow }
        Write-Host "Project open: $(Test-ProjectOpen)"
        Write-Host "Logs:         $LogDir"
    }

    'compile' {
        $r = Invoke-Unity @('-quit') 'compile'
        $errCount = Show-CompileMessages $r.LogFile
        if ($r.ExitCode -eq 0 -and $errCount -eq 0) {
            Write-Host "`nCompile OK" -ForegroundColor Green
            exit 0
        }
        Write-Host "`nCompile FAILED" -ForegroundColor Red
        if ($errCount -eq 0) { Write-Host "No 'error CS' lines found - see the log for the reason." }
        exit 1
    }

    'test' {
        $results = Join-Path $LogDir "test-results-$Platform.xml"
        if (Test-Path $results) { Remove-Item $results -Force }
        $a = @('-runTests', '-testPlatform', $Platform, '-testResults', (Quote $results))
        if ($Filter) { $a += @('-testFilter', (Quote $Filter)) }
        $r = Invoke-Unity $a "test-$Platform"
        Show-CompileMessages $r.LogFile | Out-Null
        Show-TestResults $results
        # Unity: 0 = all passed, 2 = some failed, 3 = run error
        exit $r.ExitCode
    }

    'build' {
        $a = @('-buildTarget', $Target, '-executeMethod', 'GoodCopBadCop.Cli.CommandLineBuild.Build')
        if ($Development) { $a += '-cliDevelopment' }
        if ($Output) { $a += @('-cliOutput', (Quote ([System.IO.Path]::GetFullPath($Output)))) }
        $r = Invoke-Unity $a "build-$Target"
        Show-CompileMessages $r.LogFile | Out-Null
        Get-Content $r.LogFile | Where-Object { $_ -match '\[CLI BUILD\]' } | ForEach-Object { Write-Host $_ }
        if ($r.ExitCode -eq 0) { Write-Host 'Build OK' -ForegroundColor Green } else { Write-Host 'Build FAILED' -ForegroundColor Red }
        exit $r.ExitCode
    }

    'open' {
        $ver = Get-ProjectVersion
        $editor = Find-UnityEditor $ver.Version
        if (-not $editor) { Write-Host "Unity $($ver.Version) not found." -ForegroundColor Red; exit 10 }
        Start-Process -FilePath $editor -ArgumentList ('-projectPath ' + (Quote $ProjectPath))
        Write-Host "Opening project in Unity $($ver.Version)..."
    }

    'install-editor' {
        $ver = Get-ProjectVersion
        $hub = Find-UnityHub
        if (-not $hub) { Write-Host 'Unity Hub not found. Install it first: winget install Unity.UnityHub' -ForegroundColor Red; exit 12 }
        $a = @('--', '--headless', 'install', '--version', $ver.Version, '--changeset', $ver.Changeset)
        foreach ($m in $Modules) { $a += @('--module', $m) }
        Write-Host "Installing Unity $($ver.Version) via Hub (this can take a while)..."
        & $hub @a
        Write-Host "`nInstalled editors:"
        & $hub -- --headless editors --installed
    }

    'log' {
        if (-not $Name) {
            $latest = Get-ChildItem $LogDir -Filter *.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        } else {
            $latest = Get-Item (Join-Path $LogDir "$Name.log") -ErrorAction SilentlyContinue
        }
        if (-not $latest) { Write-Host 'No CLI logs yet.'; exit 0 }
        Write-Host "== $($latest.FullName)" -ForegroundColor DarkGray
        Get-Content $latest.FullName -Tail $Tail
    }
}
