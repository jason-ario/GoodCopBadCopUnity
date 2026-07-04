param(
    [string]$ProjectRoot = (Resolve-Path ".").Path
)

$ErrorActionPreference = "Stop"

function Get-RelativePathSafe {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $baseUri = [System.Uri]((Resolve-Path $BasePath).Path.TrimEnd('\') + '\')
    $targetUri = [System.Uri]((Resolve-Path $TargetPath).Path)
    [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-RgFiles {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string[]]$Globs = @()
    )

    $rg = Get-Command rg -ErrorAction SilentlyContinue
    if ($rg) {
        $args = @(
            "--files",
            "-g", "!Library/**",
            "-g", "!Temp/**",
            "-g", "!Obj/**",
            "-g", "!Logs/**",
            "-g", "!UserSettings/**",
            "-g", "!UMotionData/Temp/**"
        )

        foreach ($glob in $Globs) {
            $args += @("-g", $glob)
        }

        Push-Location $Root
        try {
            return (& rg @args) | Where-Object { $_ }
        }
        finally {
            Pop-Location
        }
    }

    $excluded = @('\Library\', '\Temp\', '\Obj\', '\Logs\', '\UserSettings\', '\UMotionData\Temp\')
    $allFiles = Get-ChildItem -LiteralPath $Root -Recurse -File -Force | Where-Object {
        $full = $_.FullName
        -not ($excluded | Where-Object { $full.Contains($_) })
    }

    if ($Globs.Count -eq 0) {
        return $allFiles | ForEach-Object { Get-RelativePathSafe -BasePath $Root -TargetPath $_.FullName }
    }

    $patterns = $Globs | ForEach-Object { $_.Replace("/", "\").Replace("**\", "*\") }
    return $allFiles | Where-Object {
        $relative = Get-RelativePathSafe -BasePath $Root -TargetPath $_.FullName
        $patterns | Where-Object { $relative -like $_ }
    } | ForEach-Object { Get-RelativePathSafe -BasePath $Root -TargetPath $_.FullName }
}

function Get-ProjectVersion {
    param([string]$Root)

    $path = Join-Path $Root "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $path)) {
        return [ordered]@{}
    }

    $data = [ordered]@{}
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match "^(?<key>[^:]+):\s*(?<value>.+)$") {
            $data[$Matches.key.Trim()] = $Matches.value.Trim()
        }
    }
    return $data
}

function Get-BuildScenes {
    param([string]$Root)

    $path = Join-Path $Root "ProjectSettings\EditorBuildSettings.asset"
    if (-not (Test-Path -LiteralPath $path)) {
        return @()
    }

    $scenes = New-Object System.Collections.Generic.List[object]
    $current = $null
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match "^\s*-\s+enabled:\s+(?<enabled>\d+)") {
            if ($current -and $current.path) {
                $scenes.Add([pscustomobject]$current)
            }
            $current = [ordered]@{
                enabled = ([int]$Matches.enabled -eq 1)
                path = $null
                guid = $null
            }
            continue
        }

        if ($null -ne $current -and $line -match "^\s+path:\s+(?<path>.+)$") {
            $current.path = $Matches.path.Trim()
            continue
        }

        if ($null -ne $current -and $line -match "^\s+guid:\s+(?<guid>[a-fA-F0-9]+)") {
            $current.guid = $Matches.guid.Trim()
        }
    }

    if ($current -and $current.path) {
        $scenes.Add([pscustomobject]$current)
    }

    return $scenes.ToArray()
}

function Get-GitStatus {
    param([string]$Root)

    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        return @("git not found")
    }

    Push-Location $Root
    try {
        $status = git status --short
        if ($LASTEXITCODE -ne 0) {
            return @("git status failed")
        }
        return @($status)
    }
    finally {
        Pop-Location
    }
}

function Get-TopScriptFolders {
    param([string]$Root)

    $scriptRoot = Join-Path $Root "Assets\_GoodCopBadCop\_Scripts"
    if (-not (Test-Path -LiteralPath $scriptRoot)) {
        return @()
    }

    return Get-ChildItem -LiteralPath $scriptRoot -Recurse -File -Filter "*.cs" |
        Group-Object { Split-Path -Parent $_.FullName } |
        Sort-Object Count -Descending |
        Select-Object -First 40 |
        ForEach-Object {
            [pscustomobject]@{
                count = $_.Count
                path = Get-RelativePathSafe -BasePath $Root -TargetPath $_.Name
            }
        }
}

function Get-TopLevelDirs {
    param([string]$Root, [string]$RelativePath)

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        return @()
    }

    return Get-ChildItem -LiteralPath $path -Directory |
        Sort-Object Name |
        ForEach-Object { $_.Name }
}

function Get-ExtensionCounts {
    param([string]$Root)

    $allFiles = Get-RgFiles -Root $Root
    $counts = @{}
    foreach ($relative in $allFiles) {
        $extension = [System.IO.Path]::GetExtension($relative)
        if ([string]::IsNullOrWhiteSpace($extension)) {
            $extension = "(none)"
        }
        if (-not $counts.ContainsKey($extension)) {
            $counts[$extension] = 0
        }
        $counts[$extension] += 1
    }

    return $counts.GetEnumerator() |
        Sort-Object Value -Descending |
        Select-Object -First 60 |
        ForEach-Object {
            [pscustomobject]@{
                extension = $_.Key
                count = $_.Value
            }
        }
}

function Get-Warnings {
    param(
        [object[]]$GitStatus,
        [object[]]$Asmdefs,
        [object[]]$BuildScenes
    )

    $warnings = New-Object System.Collections.Generic.List[string]

    if ($GitStatus.Count -gt 0) {
        $warnings.Add("Working tree is not clean. Preserve unrelated changes.")
    }

    $productAsmdefs = @($Asmdefs | Where-Object { $_ -like "Assets\_GoodCopBadCop\*" })
    if ($productAsmdefs.Count -eq 0) {
        $warnings.Add("No product asmdef found under Assets/_GoodCopBadCop; product scripts likely compile into Assembly-CSharp.")
    }

    $enabledScenes = @($BuildScenes | Where-Object { $_.enabled })
    if ($enabledScenes.Count -eq 0) {
        $warnings.Add("No enabled build scenes found.")
    }

    return $warnings.ToArray()
}

$root = (Resolve-Path $ProjectRoot).Path
$generatedDir = Join-Path $root "Docs\AI\Alex\generated"
New-Item -ItemType Directory -Force -Path $generatedDir | Out-Null

$manifest = Read-JsonFile -Path (Join-Path $root "Packages\manifest.json")
$dependencies = [ordered]@{}
if ($manifest -and $manifest.dependencies) {
    $manifest.dependencies.PSObject.Properties |
        Sort-Object Name |
        ForEach-Object { $dependencies[$_.Name] = $_.Value }
}

$projectVersion = Get-ProjectVersion -Root $root
$buildScenes = @(Get-BuildScenes -Root $root)
$gitStatus = @(Get-GitStatus -Root $root)
$sceneFiles = @(Get-RgFiles -Root $root -Globs @("Assets/_GoodCopBadCop/_Scenes/**/*.unity", "Assets/_GoodCopBadCop/_Scenes/*.unity") | Sort-Object -Unique)
$asmdefs = @(Get-RgFiles -Root $root -Globs @("*.asmdef") | Sort-Object)
$scriptFiles = @(Get-RgFiles -Root $root -Globs @("Assets/_GoodCopBadCop/_Scripts/**/*.cs", "Assets/_GoodCopBadCop/_Scripts/*.cs") | Sort-Object -Unique)
$prefabFiles = @(Get-RgFiles -Root $root -Globs @("Assets/_GoodCopBadCop/_Prefabs/**/*.prefab", "Assets/_GoodCopBadCop/_Prefabs/*.prefab") | Sort-Object -Unique)
$dataAssets = @(Get-RgFiles -Root $root -Globs @("Assets/_GoodCopBadCop/_Data/**/*.asset", "Assets/_GoodCopBadCop/_Data/*.asset") | Sort-Object -Unique)
$extensionCounts = @(Get-ExtensionCounts -Root $root)
$topScriptFolders = @(Get-TopScriptFolders -Root $root)
$warnings = @(Get-Warnings -GitStatus $gitStatus -Asmdefs $asmdefs -BuildScenes $buildScenes)

$snapshot = [ordered]@{
    generatedAt = (Get-Date).ToString("o")
    projectRoot = $root
    unity = $projectVersion
    buildScenes = $buildScenes
    packageHighlights = [ordered]@{
        unityMcp = $dependencies["com.coplaydev.unity-mcp"]
        beziSidekickPresent = (Test-Path -LiteralPath (Join-Path $root "Packages\com.bezi.sidekick"))
        r3 = $dependencies["com.cysharp.r3"]
        uniTask = $dependencies["com.cysharp.unitask"]
        vcontainer = $dependencies["jp.hadashikick.vcontainer"]
        dotweenPresent = (Test-Path -LiteralPath (Join-Path $root "Assets\Plugins\Demigiant\DOTween"))
        dotweenProPresent = (Test-Path -LiteralPath (Join-Path $root "Assets\Plugins\Demigiant\DOTweenPro"))
        netcode = $dependencies["com.unity.netcode.gameobjects"]
        inputSystem = $dependencies["com.unity.inputsystem"]
        urp = $dependencies["com.unity.render-pipelines.universal"]
        shaderGraph = $dependencies["com.unity.shadergraph"]
        cinemachine = $dependencies["com.unity.cinemachine"]
        timeline = $dependencies["com.unity.timeline"]
        testFramework = $dependencies["com.unity.test-framework"]
    }
    counts = [ordered]@{
        productScripts = $scriptFiles.Count
        productPrefabs = $prefabFiles.Count
        productDataAssets = $dataAssets.Count
        productScenes = $sceneFiles.Count
        asmdefs = $asmdefs.Count
    }
    productTopLevelDirs = @(Get-TopLevelDirs -Root $root -RelativePath "Assets\_GoodCopBadCop")
    scriptTopLevelDirs = @(Get-TopLevelDirs -Root $root -RelativePath "Assets\_GoodCopBadCop\_Scripts")
    dataTopLevelDirs = @(Get-TopLevelDirs -Root $root -RelativePath "Assets\_GoodCopBadCop\_Data")
    sceneFiles = $sceneFiles
    asmdefs = $asmdefs
    topScriptFolders = $topScriptFolders
    extensionCounts = $extensionCounts
    gitStatus = $gitStatus
    warnings = $warnings
}

$jsonPath = Join-Path $generatedDir "PROJECT_SNAPSHOT.json"
$markdownPath = Join-Path $generatedDir "PROJECT_SNAPSHOT.md"

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$jsonText = (($snapshot | ConvertTo-Json -Depth 8) -join "`n") -replace "`r`n", "`n" -replace "`r", "`n"
[System.IO.File]::WriteAllText($jsonPath, $jsonText + "`n", $utf8NoBom)

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# Project Snapshot")
$md.Add("")
$md.Add("Generated at: $($snapshot.generatedAt)")
$md.Add("")
$md.Add("## Unity")
$md.Add("")
$md.Add("- Editor version: $($snapshot.unity["m_EditorVersion"])")
$md.Add("- Editor version with revision: $($snapshot.unity["m_EditorVersionWithRevision"])")
$md.Add("")
$md.Add("## Build Scenes")
$md.Add("")
foreach ($scene in $buildScenes) {
    $state = if ($scene.enabled) { "enabled" } else { "disabled" }
    $md.Add("- [$state] $($scene.path)")
}
$md.Add("")
$md.Add("## Package Highlights")
$md.Add("")
foreach ($property in $snapshot.packageHighlights.GetEnumerator()) {
    $md.Add("- $($property.Key): $($property.Value)")
}
$md.Add("")
$md.Add("## Counts")
$md.Add("")
foreach ($property in $snapshot.counts.GetEnumerator()) {
    $md.Add("- $($property.Key): $($property.Value)")
}
$md.Add("")
$md.Add("## Product Directories")
$md.Add("")
foreach ($dir in $snapshot.productTopLevelDirs) {
    $md.Add("- $dir")
}
$md.Add("")
$md.Add("## Script Folders")
$md.Add("")
foreach ($folder in $topScriptFolders) {
    $md.Add("- $($folder.path): $($folder.count)")
}
$md.Add("")
$md.Add("## Data Categories")
$md.Add("")
foreach ($dir in $snapshot.dataTopLevelDirs) {
    $md.Add("- $dir")
}
$md.Add("")
$md.Add("## Scenes")
$md.Add("")
foreach ($sceneFile in $sceneFiles) {
    $md.Add("- $sceneFile")
}
$md.Add("")
$md.Add("## Assembly Definitions")
$md.Add("")
if ($asmdefs.Count -eq 0) {
    $md.Add("- None found")
}
else {
    foreach ($asmdef in $asmdefs) {
        $md.Add("- $asmdef")
    }
}
$md.Add("")
$md.Add("## Git Status")
$md.Add("")
if ($gitStatus.Count -eq 0) {
    $md.Add("- Clean")
}
else {
    foreach ($status in $gitStatus) {
        $md.Add("- $status")
    }
}
$md.Add("")
$md.Add("## Warnings")
$md.Add("")
if ($warnings.Count -eq 0) {
    $md.Add("- None")
}
else {
    foreach ($warning in $warnings) {
        $md.Add("- $warning")
    }
}
$md.Add("")
$md.Add("## Extension Counts")
$md.Add("")
foreach ($entry in $extensionCounts) {
    $md.Add("- $($entry.extension): $($entry.count)")
}

$markdownText = $md -join "`n"
[System.IO.File]::WriteAllText($markdownPath, $markdownText + "`n", $utf8NoBom)

Write-Host "Wrote $markdownPath"
Write-Host "Wrote $jsonPath"
