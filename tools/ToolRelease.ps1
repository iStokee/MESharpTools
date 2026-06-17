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

    Add a tool: mark its .csproj + (optionally) add a versions.json entry. It is then discovered,
    bundled, and surfaced in-client automatically - no edits here or in ToolRegistry.

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
    [int]$BundleVersion,
    [switch]$Publish,
    [switch]$Stage
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # C#/MESharpTools
$csharpRoot = Split-Path -Parent $root     # C#
$versionsPath = Join-Path $PSScriptRoot 'versions.json'

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

function Set-ToolVersion([string]$id, [string]$newVersion) {
    if ($newVersion -notmatch '^\d+\.\d+\.\d+$') { throw "'$newVersion' is not a 3-part semantic version (e.g. 1.2.0)." }
    $versions = Get-Versions
    if ($versions.PSObject.Properties.Name -contains $id) { $versions.$id = $newVersion }
    else { $versions | Add-Member -NotePropertyName $id -NotePropertyValue $newVersion }
    $versions | ConvertTo-Json -Depth 5 | Set-Content -Path $versionsPath -Encoding utf8
    Write-Host "==> versions.json: $id -> $newVersion" -ForegroundColor Green
}

# Tool Ids excluded from the build/release, stored in versions.json under "_exclude". A tool is
# ACTIVE (shipped) unless its Id is listed here, so newly-marked tools are included by default.
function Get-Excluded {
    $v = Get-Versions
    if ($v.PSObject.Properties.Name -contains '_exclude' -and $v._exclude) { return @($v._exclude) }
    return @()
}

function Set-Excluded([string[]]$ids) {
    $versions = Get-Versions
    if ($versions.PSObject.Properties.Name -contains '_exclude') { $versions._exclude = @($ids) }
    else { $versions | Add-Member -NotePropertyName '_exclude' -NotePropertyValue @($ids) }
    $versions | ConvertTo-Json -Depth 5 | Set-Content -Path $versionsPath -Encoding utf8
}

# Flip a tool's active/excluded state.
function Toggle-Active([string]$id) {
    $ex = @(Get-Excluded)
    if ($ex -contains $id) {
        Set-Excluded @($ex | Where-Object { $_ -ne $id })
        Write-Host "==> $id is now ACTIVE (will be shipped)." -ForegroundColor Green
    }
    else {
        Set-Excluded @($ex + $id)
        Write-Host "==> $id is now EXCLUDED (omitted from the release)." -ForegroundColor DarkYellow
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
# (false when its Id is in versions.json "_exclude").
function Get-ToolProjects {
    $projects = Get-ChildItem -Path $csharpRoot -Recurse -Filter *.csproj -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    $excluded = @(Get-Excluded)
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
            Active      = (-not ($excluded -contains $assemblyName))
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

    $tools = @($discovered | Where-Object Active)
    $skipped = @($discovered | Where-Object { -not $_.Active })
    if (-not $tools) { throw "Every discovered tool is excluded - nothing to release. Re-activate at least one." }

    $bundleN = Resolve-BundleNumber
    $tag = "tools-v$bundleN"
    $stage = Join-Path $root "artifacts/bundle/$tag"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Write-Host ""
    Write-Host "==> Building bundle $tag ($($tools.Count) active tool(s))" -ForegroundColor Cyan
    if ($skipped) { Write-Host "    Excluded: $(($skipped | ForEach-Object { $_.Id }) -join ', ')" -ForegroundColor DarkYellow }

    $manifestTools = @()
    $assetPaths = @()
    foreach ($t in $tools) {
        $av = "$($t.Version).0"
        Write-Host "==> $($t.Id) $($t.Version)  (AssemblyVersion=$av)"
        dotnet build $t.ProjectPath -c $Configuration -p:AssemblyVersion=$av -p:FileVersion=$av --nologo
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
    }

    $manifest = [ordered]@{
        schema         = 1
        release        = $tag
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        tools          = $manifestTools
    }
    $manifestPath = Join-Path $stage 'tools-manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding utf8

    Write-Host "==> Staged $($tools.Count) DLL(s) + manifest in: $stage" -ForegroundColor Green

    if ($script:PublishMode) {
        if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '`gh` CLI not found; cannot publish.' }
        $title = "MESharp Tools $tag"
        $notes = "Umbrella release $tag. Tools: " + (($manifestTools | ForEach-Object { "$($_.id) $($_.version)" }) -join ', ') + '.'
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
        Write-Host " $($tools.Count) tool(s) discovered, $activeCount active. [x] = shipped, [ ] = excluded:"
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
        Write-Host "  #) toggle a tool active/excluded"
        Write-Host "  s) set a tool's version"
        Write-Host "  r) build + release the bundle (active tools only)"
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
