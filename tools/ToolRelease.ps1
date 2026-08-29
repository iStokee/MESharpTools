<#
.SYNOPSIS
    Builds every MESharp tool, bundles the DLLs + a manifest, and (optionally) publishes a single
    umbrella GitHub release that the in-client ToolUpdater parses to update tools individually.

.DESCRIPTION
    Umbrella-release model. Tools self-register in their .csproj:

        <IsMESharpTool>true</IsMESharpTool>
        <MESharpToolDisplayName>Navigation</MESharpToolDisplayName>

    This script auto-discovers every such project (no hardcoded catalog), builds each at the version
    recorded in tools/versions.json (keyed by tool Id == AssemblyName, default 1.0.0), and produces ONE
    release tagged `tools-vN`. The release carries every tool DLL plus `tools-manifest.json`, which maps
    id -> {displayName, assetName, version, sha256}. The client compares each manifest version to the
    installed DLL and hot-reloads only the tools that changed.

    Add a tool: mark its .csproj and deliberately add its AssemblyName to tools/versions.json
    `_include`. Newly discovered tools remain internal by default.

    Only the Costura-woven tool DLLs are shipped; csharp_interop is excluded at build time and never
    bundled (a second copy would break hot reload - DUPLICATE_ASSEMBLY_FIX.md).

.PARAMETER Repo
    GitHub "owner/repo" that hosts the releases. Default: iStokee/MESharpTools.

.PARAMETER BundleVersion
    Override the umbrella release number N (tag becomes tools-vN). Default: auto-incremented from the
    newest existing tools-v* tag (1 if none / offline).

.PARAMETER Publish
    Build + create the GitHub release via `gh`. Without -Publish/-Stage you get the interactive menu.

.PARAMETER Stage
    Build + stage the bundle (manifest, sha256, paste-ready release fields) without publishing.

.EXAMPLE
    pwsh ToolRelease.ps1                 # interactive menu (set versions, stage, or publish)
.EXAMPLE
    pwsh ToolRelease.ps1 -Stage          # build + stage the whole bundle (dry run)
.EXAMPLE
    pwsh ToolRelease.ps1 -Publish        # build + publish one tools-vN release with all tools
#>
[CmdletBinding()]
param(
    [string]$Repo = 'iStokee/MESharpTools',
    [string]$Configuration = 'Release',
    [string]$NavigationDataSource,
    [int]$BundleVersion,
    [switch]$Publish,
    [switch]$Stage
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # C#/MESharpTools
$csharpRoot = Split-Path -Parent $root     # C#
$versionsPath = Join-Path $PSScriptRoot 'versions.json'
$navigationDataExplicit = -not [string]::IsNullOrWhiteSpace($NavigationDataSource)
if (-not $NavigationDataSource) {
    $NavigationDataSource = Join-Path $PSScriptRoot 'navigation-data'
}

# Publish state: seeded from -Publish, toggled by the menu.
$script:PublishMode = [bool]$Publish

# ----------------------------- versions.json -----------------------------

function Get-Versions {
    if (-not (Test-Path $versionsPath)) { return [pscustomobject]@{} }
    return Get-Content $versionsPath -Raw | ConvertFrom-Json
}

# Version for a tool Id; defaults to 1.0.0 when absent so a freshly-marked tool needs no edit.
function Resolve-Version([string]$id) {
    $v = (Get-Versions).$id
    if (-not $v) { return '1.0.0' }
    if ($v -notmatch '^\d+\.\d+\.\d+$') { throw "versions.json[$id] = '$v' is not a 3-part semantic version." }
    return $v
}

function Resolve-NavigationDataVersion {
    $v = (Get-Versions)._navigationDataVersion
    if (-not $v -or $v -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "versions.json[_navigationDataVersion] must be a 3- or 4-part version."
    }
    return "$v"
}

function Resolve-NavigationMinInteropVersion {
    $v = (Get-Versions)._navigationDataMinInteropVersion
    if (-not $v -or $v -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "versions.json[_navigationDataMinInteropVersion] must be a 3- or 4-part version."
    }
    return "$v"
}

function Set-ToolVersion([string]$id, [string]$newVersion) {
    if ($newVersion -notmatch '^\d+\.\d+\.\d+$') { throw "'$newVersion' is not a 3-part semantic version (e.g. 1.2.0)." }
    $versions = Get-Versions
    if ($versions.PSObject.Properties.Name -contains $id) { $versions.$id = $newVersion }
    else { $versions | Add-Member -NotePropertyName $id -NotePropertyValue $newVersion }
    $versions | ConvertTo-Json -Depth 5 | Set-Content -Path $versionsPath -Encoding utf8
    Write-Host "==> versions.json: $id -> $newVersion" -ForegroundColor Green
}

# Explicit public release allowlist. A newly-marked tool is internal until deliberately included.
function Get-Included {
    $v = Get-Versions
    if ($v.PSObject.Properties.Name -contains '_include' -and $v._include) { return @($v._include) }
    return @()
}

function Set-Included([string[]]$ids) {
    $versions = Get-Versions
    if ($versions.PSObject.Properties.Name -contains '_include') { $versions._include = @($ids) }
    else { $versions | Add-Member -NotePropertyName '_include' -NotePropertyValue @($ids) }
    $versions | ConvertTo-Json -Depth 5 | Set-Content -Path $versionsPath -Encoding utf8
}

# Flip a tool's public/internal state.
function Toggle-Active([string]$id) {
    $included = @(Get-Included)
    if ($included -contains $id) {
        Set-Included @($included | Where-Object { $_ -ne $id })
        Write-Host "==> $id is now INTERNAL (omitted from the release)." -ForegroundColor DarkYellow
    }
    else {
        Set-Included @($included + $id)
        Write-Host "==> $id is now PUBLIC (will be shipped)." -ForegroundColor Green
    }
}

# ----------------------------- discovery -----------------------------

function Get-CsprojProp([xml]$xml, [string]$name) {
    foreach ($pg in $xml.Project.PropertyGroup) {
        $val = $pg.$name
        if ($null -ne $val -and "$val".Trim()) { return "$val".Trim() }
    }
    return $null
}

# Auto-discover every project that self-registers as a MESharp tool. Each carries an Active flag
# (true only when its Id is in versions.json "_include").
function Get-ToolProjects {
    $projects = Get-ChildItem -Path $csharpRoot -Recurse -Filter *.csproj -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    $included = @(Get-Included)
    $tools = @()
    foreach ($file in $projects) {
        try { $xml = [xml](Get-Content $file.FullName -Raw) } catch { continue }
        if ((Get-CsprojProp $xml 'IsMESharpTool') -ne 'true') { continue }

        $assemblyName = Get-CsprojProp $xml 'AssemblyName'
        if (-not $assemblyName) { $assemblyName = [IO.Path]::GetFileNameWithoutExtension($file.Name) }
        $tfm = Get-CsprojProp $xml 'TargetFramework'
        if (-not $tfm) { throw "Tool $assemblyName has no single <TargetFramework>." }
        $display = Get-CsprojProp $xml 'MESharpToolDisplayName'
        if (-not $display) { $display = $assemblyName }

        $tools += [pscustomobject]@{
            Id          = $assemblyName
            DisplayName = $display
            ProjectPath = $file.FullName
            ProjectDir  = $file.DirectoryName
            Tfm         = $tfm
            AssetName   = "$assemblyName.dll"
            Version     = Resolve-Version $assemblyName
            Active      = ($included -contains $assemblyName)
        }
    }
    return $tools | Sort-Object Id
}

# ----------------------------- release -----------------------------

# Next umbrella release number: -BundleVersion wins, else newest tools-v* tag + 1 (1 if none/offline).
function Resolve-BundleNumber {
    if ($BundleVersion) { return $BundleVersion }
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        try {
            $tags = gh release list --repo $Repo --limit 200 --json tagName --jq '.[].tagName' 2>$null
            $nums = $tags | ForEach-Object { if ($_ -match '^tools-v(\d+)$') { [int]$Matches[1] } }
            if ($nums) { return (($nums | Measure-Object -Maximum).Maximum + 1) }
        }
        catch { Write-Host "   (could not read existing tags; defaulting bundle number)" -ForegroundColor DarkYellow }
    }
    return 1
}

function Invoke-BundleRelease {
    $discovered = Get-ToolProjects
    if (-not $discovered) { throw "No tool projects found (no .csproj with <IsMESharpTool>true under $csharpRoot)." }

    $discoveredIds = @($discovered | ForEach-Object Id)
    $missingPublicIds = @(Get-Included | Where-Object { $discoveredIds -notcontains $_ })
    if ($missingPublicIds.Count -gt 0) {
        throw "versions.json `_include` contains undiscovered tool id(s): $($missingPublicIds -join ', ')"
    }

    $tools = @($discovered | Where-Object Active)
    $skipped = @($discovered | Where-Object { -not $_.Active })
    if (-not $tools) { throw "No discovered tools are public - add at least one id to versions.json `_include`." }

    $bundleN = Resolve-BundleNumber
    $tag = "tools-v$bundleN"
    $stage = Join-Path $root "artifacts/bundle/$tag"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Write-Host ""
    Write-Host "==> Building bundle $tag ($($tools.Count) public tool(s))" -ForegroundColor Cyan
    if ($skipped) { Write-Host "    Internal: $(($skipped | ForEach-Object { $_.Id }) -join ', ')" -ForegroundColor DarkYellow }

    $manifestTools = @()
    $assetPaths = @()
    foreach ($t in $tools) {
        $av = "$($t.Version).0"
        Write-Host "==> $($t.Id) $($t.Version)  (AssemblyVersion=$av)"
        dotnet build $t.ProjectPath -c $Configuration -p:AssemblyVersion=$av -p:FileVersion=$av -p:SkipDeployToMe=true --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $($t.Id)." }

        $dll = Join-Path $t.ProjectDir "bin/$Configuration/$($t.Tfm)/$($t.AssetName)"
        if (-not (Test-Path $dll)) { throw "Built DLL not found: $dll" }

        $staged = Join-Path $stage $t.AssetName
        Copy-Item $dll $staged -Force
        $sha = (Get-FileHash -Algorithm SHA256 -Path $staged).Hash.ToLowerInvariant()

        $manifestTools += [ordered]@{
            id          = $t.Id
            displayName = $t.DisplayName
            assetName   = $t.AssetName
            version     = $t.Version
            sha256      = $sha
        }
        $assetPaths += $staged

        # Per-tool manifest used by the directory-based client updater. Its DLL asset is resolved
        # relative to this manifest's stable /latest/download URL.
        $toolManifestPath = Join-Path $stage "$($t.Id).tool.json"
        [ordered]@{
            displayName = $t.DisplayName
            assetName   = $t.AssetName
            version     = $t.Version
            updateUrl   = "https://github.com/iStokee/MESharpTools/releases/latest/download/$($t.Id).tool.json"
            sha256      = $sha
        } | ConvertTo-Json | Set-Content -Path $toolManifestPath -Encoding utf8
        $assetPaths += $toolManifestPath
    }

    # Navigation runtime data is a first-class umbrella asset. The source is an explicit,
    # publisher-prepared directory so a developer's mixed LocalAppData graph is never uploaded
    # accidentally. Required layout: collision/index.json + shards, webwalk_graph.json;
    # obstacles.json is optional.
    $navVersion = $null
    $manifestNavigationData = $null
    $navCollision = Join-Path $NavigationDataSource 'collision'
    $navGraph = Join-Path $NavigationDataSource 'webwalk_graph.json'
    $hasNavigationData = (Test-Path (Join-Path $navCollision 'index.json') -PathType Leaf) -and
        (Test-Path $navGraph -PathType Leaf)
    if (-not $hasNavigationData -and $navigationDataExplicit) {
        throw "Navigation data source is missing collision/index.json: $NavigationDataSource"
    }
    if (-not $hasNavigationData) {
        Write-Host "    Navigation data omitted (default publisher source is not prepared)." -ForegroundColor DarkYellow
    }
    else {
    $navVersion = Resolve-NavigationDataVersion
    $navMinInterop = Resolve-NavigationMinInteropVersion
    if (-not (Get-ChildItem $navCollision -Filter '*_*.json' -File -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        throw "Navigation data source contains no collision shards: $navCollision"
    }
    if (-not (Test-Path $navGraph -PathType Leaf)) {
        throw "Navigation data source is missing webwalk_graph.json: $NavigationDataSource"
    }

    try { $navGraphData = Get-Content $navGraph -Raw | ConvertFrom-Json }
    catch { throw "Navigation vendor graph is invalid JSON: $($_.Exception.Message)" }
    $allowedSources = @('seed', 'cache', 'curated', 'wiki', 'publisher')
    $graphEntities = @($navGraphData.nodes) + @($navGraphData.edges) + @($navGraphData.areas)
    $unsafeEntities = @($graphEntities | Where-Object {
        -not $_.source -or $allowedSources -notcontains "$($_.source)".ToLowerInvariant()
    })
    if ($unsafeEntities.Count -gt 0) {
        throw "Navigation vendor graph contains $($unsafeEntities.Count) entity/entities without an approved publisher source. Allowed: $($allowedSources -join ', ')."
    }
    if (@($navGraphData.edges | Where-Object { $null -ne $_.observedCostMs }).Count -gt 0) {
        throw 'Navigation vendor graph contains runtime observedCostMs values; publish estimates only.'
    }

    try { $collisionIndex = Get-Content (Join-Path $navCollision 'index.json') -Raw | ConvertFrom-Json }
    catch { throw "Collision index is invalid JSON: $($_.Exception.Message)" }
    $collisionSquares = @($collisionIndex.squares)
    if ($collisionSquares.Count -eq 0) { throw 'Collision index contains no squares.' }

    $navContent = Join-Path $stage '_navigation-data-content'
    New-Item -ItemType Directory -Force -Path $navContent | Out-Null
    $packCollision = Join-Path $navContent 'collision'
    New-Item -ItemType Directory -Force -Path $packCollision | Out-Null
    Copy-Item (Join-Path $navCollision 'index.json') (Join-Path $packCollision 'index.json') -Force
    foreach ($square in $collisionSquares) {
        if ("$square" -notmatch '^\d+_\d+$') { throw "Collision index contains invalid square '$square'." }
        $shard = Join-Path $navCollision "$square.json"
        if (-not (Test-Path $shard -PathType Leaf)) { throw "Collision index references missing shard: $shard" }
        Copy-Item $shard (Join-Path $packCollision "$square.json") -Force
    }

    $sourceComponents = Join-Path $navCollision 'components'
    if (Test-Path (Join-Path $sourceComponents 'index.json') -PathType Leaf) {
        $packComponents = Join-Path $packCollision 'components'
        New-Item -ItemType Directory -Force -Path $packComponents | Out-Null
        Copy-Item (Join-Path $sourceComponents 'index.json') (Join-Path $packComponents 'index.json') -Force
        foreach ($square in $collisionSquares) {
            $componentShard = Join-Path $sourceComponents "$square.json"
            if (Test-Path $componentShard -PathType Leaf) {
                Copy-Item $componentShard (Join-Path $packComponents "$square.json") -Force
            }
        }
    }
    Copy-Item $navGraph (Join-Path $navContent 'webwalk_graph.json') -Force
    $navObstacles = Join-Path $NavigationDataSource 'obstacles.json'
    if (Test-Path $navObstacles -PathType Leaf) {
        Copy-Item $navObstacles (Join-Path $navContent 'obstacles.json') -Force
    }
    [ordered]@{
        schema = 1
        version = $navVersion
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    } | ConvertTo-Json | Set-Content -Path (Join-Path $navContent 'navigation-data.json') -Encoding utf8

    $navAssetName = 'mesharp-navigation-data.zip'
    $navAssetPath = Join-Path $stage $navAssetName
    Compress-Archive -Path (Join-Path $navContent '*') -DestinationPath $navAssetPath -CompressionLevel Optimal
    Remove-Item $navContent -Recurse -Force
    $navSha = (Get-FileHash -Algorithm SHA256 -Path $navAssetPath).Hash.ToLowerInvariant()
    $navSize = (Get-Item $navAssetPath).Length
    $assetPaths += $navAssetPath

    $manifestNavigationData = [ordered]@{
        version           = $navVersion
        schema            = 1
        assetName         = $navAssetName
        sha256            = $navSha
        minInteropVersion = $navMinInterop
        sizeBytes         = $navSize
    }
    }

    $manifest = [ordered]@{
        schema         = 1
        release        = $tag
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        tools          = $manifestTools
        navigationData = $manifestNavigationData
    }
    $manifestPath = Join-Path $stage 'tools-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding utf8

    $navSummary = if ($manifestNavigationData) { ", navigation data $navVersion" } else { "" }
    Write-Host "==> Staged $($tools.Count) DLL(s)$navSummary, and manifests in: $stage" -ForegroundColor Green

    if ($script:PublishMode) {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '`gh` CLI not found; cannot publish.' }
        $title = "MESharp Tools $tag"
        $notes = "Umbrella release $tag. Tools: " + (($manifestTools | ForEach-Object { "$($_.id) $($_.version)" }) -join ', ') + '.'
        if ($manifestNavigationData) { $notes += " Navigation data $navVersion." }
        $ghArgs = @('release', 'create', $tag) + $assetPaths + @($manifestPath, '--repo', $Repo, '--title', $title, '--notes', $notes)
        Write-Host "==> Creating GitHub release $tag on $Repo" -ForegroundColor Cyan
        gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create failed for $tag." }
        Write-Host "==> Published $tag" -ForegroundColor Green
    }
    else {
        Write-Host ""
        Write-Host "==> Dry run (no publish)." -ForegroundColor Yellow
        Write-Host "    Paste into GitHub -> $Repo -> Releases -> Draft a new release:" -ForegroundColor Yellow
        Write-Host "    -------------------------------------------------------------------"
        Write-Host "    Tag version:    $tag"
        Write-Host "    Release title:  MESharp Tools $tag"
        Write-Host "    Attach assets:  (everything in $stage)"
        foreach ($p in ($assetPaths + $manifestPath)) { Write-Host "                    $p" }
        Write-Host "    -------------------------------------------------------------------"
        Write-Host "    (then type your release description and click Publish)"
        Write-Host "    Or via CLI:  gh release create $tag <dlls...> tools-manifest.json --repo $Repo"
    }
}

function Confirm-Publish {
    $ans = (Read-Host "PUBLISH the bundle to $Repo - create a live GitHub release? (y/N)").Trim().ToLower()
    return $ans -eq 'y'
}

# ----------------------------- menu -----------------------------

function Show-Menu {
    while ($true) {
        $tools = Get-ToolProjects
        $activeCount = @($tools | Where-Object Active).Count
        $mode = if ($script:PublishMode) { 'PUBLISH (creates a GitHub release)' } else { 'DRY RUN (stage only)' }
        Write-Host ""
        Write-Host "============ MESharp Tool Release (umbrella) ============" -ForegroundColor Cyan
        Write-Host " Repo: $Repo"
        Write-Host " Mode: " -NoNewline; Write-Host $mode -ForegroundColor ($(if ($script:PublishMode) { 'Red' } else { 'Yellow' }))
        Write-Host " $($tools.Count) tool(s) discovered, $activeCount public. [x] = public, [ ] = internal:"
        Write-Host "--------------------------------------------------------"
        for ($i = 0; $i -lt $tools.Count; $i++) {
            $n = ($i + 1).ToString().PadLeft(2)
            $box = if ($tools[$i].Active) { '[x]' } else { '[ ]' }
            $id = $tools[$i].Id.PadRight(26)
            $ver = ('v' + $tools[$i].Version).PadRight(9)
            $color = if ($tools[$i].Active) { 'Gray' } else { 'DarkGray' }
            Write-Host "  $n) $box $id $ver $($tools[$i].DisplayName)" -ForegroundColor $color
        }
        Write-Host "--------------------------------------------------------"
        Write-Host "  #) toggle a tool public/internal"
        Write-Host "  s) set a tool's version"
        Write-Host "  r) build + release the bundle (public tools only)"
        Write-Host "  p) toggle publish/dry-run (currently: $(if ($script:PublishMode){'publish'}else{'dry run'}))"
        Write-Host "  q) quit"
        Write-Host "========================================================"
        $choice = (Read-Host 'Select an option').Trim().ToLower()

        switch ($choice) {
            'q' { return }
            'p' { $script:PublishMode = -not $script:PublishMode }
            'r' {
                if ($script:PublishMode -and -not (Confirm-Publish)) { continue }
                try { Invoke-BundleRelease } catch { Write-Host "!! $($_.Exception.Message)" -ForegroundColor Red }
            }
            's' {
                $sel = (Read-Host 'Set version for which tool number').Trim()
                if ($sel -match '^\d+$' -and [int]$sel -ge 1 -and [int]$sel -le $tools.Count) {
                    $t = $tools[[int]$sel - 1]
                    $nv = (Read-Host "New version for $($t.Id) (current $($t.Version))").Trim()
                    try { Set-ToolVersion $t.Id $nv } catch { Write-Host "!! $($_.Exception.Message)" -ForegroundColor Red }
                }
                else { Write-Host '!! Invalid tool number.' -ForegroundColor Red }
            }
            default {
                if ($choice -match '^\d+$' -and [int]$choice -ge 1 -and [int]$choice -le $tools.Count) {
                    Toggle-Active $tools[[int]$choice - 1].Id
                }
                else { Write-Host '!! Unrecognized option.' -ForegroundColor Red }
            }
        }
    }
}

# ----------------------------- dispatch -----------------------------
if ($Publish -or $Stage) {
    Invoke-BundleRelease
}
else {
    Show-Menu
}
