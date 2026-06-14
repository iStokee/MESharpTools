<#
.SYNOPSIS
    Builds a MESharp tool, computes its sha256, writes a manifest, and (optionally) publishes a
    GitHub release that the in-client ToolUpdater can detect and pull down.

.DESCRIPTION
    Mirrors Orbit/tools/OrbitRelease.ps1 but for hot-reloadable tools. Per-tool independent
    versioning is achieved with tag-prefixed releases (e.g. navtool-v1.2.0); the updater filters
    releases by the tool's TagPrefix, so one monorepo can ship many tools at different versions.

    The release asset is the Costura-woven tool DLL only (csharp_interop is excluded at build time
    and never shipped). A sidecar <id>.manifest.json carries the sha256 for download verification.

.PARAMETER Tool
    Which tool to release: apitool | mcptool | navtool | sharpbuilder.

.PARAMETER Version
    Semantic version, e.g. 1.2.0. The build stamps AssemblyVersion=<Version>.0 so the installed
    DLL's version matches the release tag (the updater normalizes 3- vs 4-part versions).

.PARAMETER Publish
    Actually create the GitHub release via `gh`. Without it, the script only stages artifacts.

.EXAMPLE
    pwsh ToolRelease.ps1 -Tool navtool -Version 1.2.0 -Publish
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('apitool', 'mcptool', 'navtool', 'sharpbuilder')][string]$Tool,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$Repo = 'iStokee/MESharpTools',
    [string]$Configuration = 'Release',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot   # C#/MESharpTools

# Per-tool release metadata. ProjectPath is relative to the repo's C# root.
$catalog = @{
    apitool      = @{ ProjectPath = 'MESharpTools/MESharpApiTool/MESharpApiTool.csproj';   PackId = 'MESharpApiTool';      AssetName = 'MESharpApiTool.dll';        TagPrefix = 'apitool-';      Tfm = 'net10.0-windows' }
    mcptool      = @{ ProjectPath = 'MESharpTools/MESharpMcpTool/MESharpMcpTool.csproj';   PackId = 'MESharpMcpTool';      AssetName = 'MESharpMcpTool.dll';        TagPrefix = 'mcptool-';      Tfm = 'net10.0-windows' }
    navtool      = @{ ProjectPath = 'MESharpTools/MESharpNavTool/MESharpNavTool.csproj';   PackId = 'MESharpNavTool';      AssetName = 'MESharpNavTool.dll';        TagPrefix = 'navtool-';      Tfm = 'net10.0-windows' }
    sharpbuilder = @{ ProjectPath = 'SharpBuilder/SharpBuilder.ScriptHost/SharpBuilder.ScriptHost.csproj'; PackId = 'SharpBuilder'; AssetName = 'SharpBuilder.ScriptHost.dll'; TagPrefix = 'sharpbuilder-'; Tfm = 'net10.0-windows' }
}

$meta = $catalog[$Tool]
$csharpRoot = Split-Path -Parent $root      # C#
$projectPath = Join-Path $csharpRoot $meta.ProjectPath
if (-not (Test-Path $projectPath)) { throw "Project not found: $projectPath" }

$tag = "$($meta.TagPrefix)v$Version"
$assemblyVersion = "$Version.0"

Write-Host "==> Releasing $($meta.PackId) $Version (tag $tag)" -ForegroundColor Cyan

# 1) Build Release with the version stamped into AssemblyVersion (so installed == tag).
Write-Host "==> dotnet build $Configuration (AssemblyVersion=$assemblyVersion)"
dotnet build $projectPath -c $Configuration -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# 2) Locate the woven DLL.
$projectDir = Split-Path -Parent $projectPath
$dll = Join-Path $projectDir "bin/$Configuration/$($meta.Tfm)/$($meta.AssetName)"
if (-not (Test-Path $dll)) { throw "Built DLL not found: $dll" }

# 3) Stage payload + compute sha256 + write manifest.
$stage = Join-Path $root "artifacts/$Tool/$Version"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $dll (Join-Path $stage $meta.AssetName) -Force

$sha = (Get-FileHash -Algorithm SHA256 -Path (Join-Path $stage $meta.AssetName)).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    version        = $Version
    assetName      = $meta.AssetName
    packId         = $meta.PackId
    sha256         = $sha
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$manifestName = "$($meta.PackId).manifest.json"
$manifestPath = Join-Path $stage $manifestName
$manifest | ConvertTo-Json | Set-Content -Path $manifestPath -Encoding utf8

Write-Host "==> Staged:" -ForegroundColor Green
Write-Host "    $stage\$($meta.AssetName)"
Write-Host "    $manifestPath  (sha256 $sha)"

# 4) Publish the GitHub release (token-free for the consumer; requires `gh auth login` here).
if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '`gh` CLI not found; cannot publish.' }
    Write-Host "==> Creating GitHub release $tag on $Repo" -ForegroundColor Cyan
    gh release create $tag `
        (Join-Path $stage $meta.AssetName) `
        $manifestPath `
        --repo $Repo `
        --title "$($meta.PackId) $Version" `
        --notes "Automated release of $($meta.PackId) $Version."
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
    Write-Host "==> Published $tag" -ForegroundColor Green
}
else {
    Write-Host "==> Dry run (no -Publish). To publish: gh release create $tag <dll> <manifest> --repo $Repo" -ForegroundColor Yellow
}
