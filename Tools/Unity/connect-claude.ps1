# Connects this Unity project to the Claude desktop app via Unity's official MCP server.
#  1. Installs the Unity CLI (beta channel) if it isn't on PATH
#  2. Adds the com.unity.pipeline package to Packages/manifest.json (lets the CLI talk to the open Editor)
#  3. Registers "unity mcp" in Claude desktop's config, pinned to this project
# Safe to re-run. Output is mirrored to Logs\cli\connect-claude.txt.

$ErrorActionPreference = 'Continue'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$LogDir = Join-Path $Root 'Logs\cli'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$Log = Join-Path $LogDir 'connect-claude.txt'
Start-Transcript -Path $Log -Force | Out-Null

function Refresh-Path {
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                [Environment]::GetEnvironmentVariable('Path', 'User')
}

Write-Host "Project: $Root"
Write-Host "=== 1. Unity CLI ==="
Refresh-Path
if (-not (Get-Command unity -ErrorAction SilentlyContinue)) {
    Write-Host "Unity CLI not found - installing (beta channel)..."
    $env:UNITY_CLI_CHANNEL = 'beta'
    Invoke-RestMethod https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | Invoke-Expression
    Refresh-Path
}
$unity = Get-Command unity -ErrorAction SilentlyContinue
if (-not $unity) {
    Write-Host "ERROR: Unity CLI still not on PATH after install. Open a new terminal and re-run."
    Write-Host "RESULT: FAILED"
    Stop-Transcript | Out-Null
    exit 1
}
Write-Host "unity at: $($unity.Source)"
unity --version --no-banner

Write-Host "=== 2. Pipeline package ==="
unity pipeline install --project-path "$Root" --non-interactive --no-banner
$pipeExit = $LASTEXITCODE
Write-Host "pipeline install exit code: $pipeExit"

Write-Host "=== 3. Register MCP server in Claude desktop ==="
unity mcp configure claude --project-path "$Root" --yes --no-banner
$mcpExit = $LASTEXITCODE
Write-Host "mcp configure exit code: $mcpExit"

Write-Host "=== 4. Status ==="
unity pipeline list --no-banner
unity status --no-banner

if ($pipeExit -eq 0 -and $mcpExit -eq 0) { Write-Host "RESULT: OK" } else { Write-Host "RESULT: CHECK ERRORS ABOVE" }
Stop-Transcript | Out-Null
