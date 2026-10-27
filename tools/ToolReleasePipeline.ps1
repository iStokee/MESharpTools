# Shared by staging, validation, and publication. No build or publish work runs on import.
function Get-ToolMinimumInteropVersion($Versions, [string]$Id, $Previous, [bool]$Building) {
    $configured = $Versions._toolMinInteropVersions.$Id
    if (!$configured) { $configured = $Versions._toolMinInteropVersion }
    if (!$Building -and $Previous.minInteropVersion) {
        if ($configured -and [version]$configured -ne [version]$Previous.minInteropVersion) {
            throw "Bump $Id before changing its minimum interop requirement."
        }
        return [string]$Previous.minInteropVersion
    }
    if (!$configured -or $configured -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
        throw "Set _toolMinInteropVersions.$Id to the oldest tested compatible interop version."
    }
    return [string]$configured
}

function Get-ToolReleaseBaseline([string]$Repository, [string]$Destination, [string]$LocalDirectory) {
    if ($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
    $release = $null
    $local = $null
    $maximumBundle = 0
    if ($LocalDirectory) {
        $local = (Resolve-Path -LiteralPath $LocalDirectory).Path
        $manifestPath = Join-Path $local 'tools-manifest.json'
    }
    else {
        $headers = @{ 'User-Agent' = 'MESharpTools-Release'; Accept = 'application/vnd.github+json' }
        for ($page = 1; $page -le 10; $page++) {
            $releases = @(Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page" -Headers $headers)
            foreach ($candidate in $releases) {
                if ($candidate.tag_name -match '^tools-v(\d+)$') { $maximumBundle = [Math]::Max($maximumBundle, [int]$Matches[1]) }
                if (!$release -and !$candidate.draft -and !$candidate.prerelease -and
                    @($candidate.assets | Where-Object name -eq 'tools-manifest.json').Count -eq 1) { $release = $candidate }
            }
            if ($releases.Count -lt 100) { break }
            if ($page -eq 10) { throw 'Release history exceeds 1000 entries; provide -BaselineDirectory explicitly.' }
        }
        if (!$release) { return $null }
        [IO.Directory]::CreateDirectory($Destination) | Out-Null
        $manifestPath = Join-Path $Destination 'tools-manifest.json'
        $asset = $release.assets | Where-Object name -eq 'tools-manifest.json'
        $expected = "https://github.com/$Repository/releases/download/$($release.tag_name)/tools-manifest.json"
        if ($asset.browser_download_url -cne $expected) { throw 'Baseline catalog URL is not pinned to its repository release.' }
        Invoke-WebRequest -UseBasicParsing -Uri $expected -OutFile $manifestPath -TimeoutSec 120
    }
    $catalog = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($catalog.schema -notin @(1, 2) -or $catalog.release -notmatch '^tools-v\d+$' -or $null -eq $catalog.tools -or
        ($release -and $catalog.release -cne $release.tag_name)) { throw 'Invalid baseline catalog.' }
    if ($catalog.release -match '^tools-v(\d+)$') { $maximumBundle = [Math]::Max($maximumBundle, [int]$Matches[1]) }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($tool in $catalog.tools) {
        if ($tool.id -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]*$' -or !$ids.Add($tool.id) -or
            $tool.assetName -cne "$($tool.id).dll" -or $tool.version -notmatch '^\d+\.\d+\.\d+$' -or
            $tool.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid or duplicate baseline tool entry.' }
    }
    return [pscustomobject]@{ Catalog = $catalog; ManifestPath = $manifestPath; LocalDirectory = $local;
        Release = $release; Repository = $Repository; MaximumBundle = $maximumBundle }
}

function Get-ToolReleasePlan($Tools, $Baseline, [string[]]$SelectedTools) {
    foreach ($selected in $SelectedTools) {
        if (@($Tools | Where-Object Id -eq $selected).Count -ne 1) { throw "Selected tool is not public: $selected" }
    }
    foreach ($tool in $Tools) {
        $previous = @($Baseline.Catalog.tools | Where-Object id -eq $tool.Id)
        if ($previous.Count -gt 1) { throw "Duplicate baseline tool: $($tool.Id)" }
        $old = if ($previous.Count) { $previous[0] } else { $null }
        if ($old -and [version]$tool.Version -lt [version]$old.version) { throw "Version decreased: $($tool.Id)" }
        $changed = !$old -or [version]$tool.Version -gt [version]$old.version
        if ($SelectedTools -contains $tool.Id -and !$changed) { throw "Bump the version before rebuilding $($tool.Id)." }
        if ($SelectedTools.Count -gt 0 -and $old -and $changed -and $SelectedTools -notcontains $tool.Id) {
            throw "Version changed for unselected tool $($tool.Id); select it or restore its published version."
        }
        [pscustomobject]@{ Tool = $tool; Previous = $old; Build = $changed }
    }
}

function Copy-PublishedAsset($Baseline, $Tool, [string]$Destination) {
    if ($Tool.assetName -ne [IO.Path]::GetFileName($Tool.assetName) -or $Tool.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Invalid baseline asset metadata.'
    }
    if ($Baseline.LocalDirectory) {
        Copy-Item -LiteralPath (Join-Path $Baseline.LocalDirectory $Tool.assetName) -Destination $Destination
    }
    else {
        $asset = @($Baseline.Release.assets | Where-Object name -eq $Tool.assetName)
        $expected = "https://github.com/$($Baseline.Repository)/releases/download/$($Baseline.Catalog.release)/$($Tool.assetName)"
        if ($asset.Count -ne 1 -or $asset[0].browser_download_url -cne $expected) { throw "Missing or unpinned baseline asset: $($Tool.assetName)" }
        Invoke-WebRequest -UseBasicParsing -Uri $expected -OutFile $Destination -TimeoutSec 120
    }
    if ((Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash -ine $Tool.sha256) {
        throw "Baseline hash mismatch: $($Tool.id)"
    }
}

function Copy-PublishedTool($Baseline, $Tool, [string]$Destination) {
    Copy-PublishedAsset $Baseline $Tool $Destination
    $assembly = [Reflection.AssemblyName]::GetAssemblyName($Destination)
    if ($assembly.Name -cne $Tool.id -or $assembly.Version.ToString(3) -ne $Tool.version) {
        throw "Baseline assembly identity/version mismatch: $($Tool.id)"
    }
}

function Test-ToolBundle([string]$Directory, [string]$PreviousManifest) {
    $bundlePath = (Resolve-Path -LiteralPath $Directory).Path
    $manifestPath = Join-Path $bundlePath 'tools-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schema -ne 2 -or $manifest.release -notmatch '^tools-v\d+$') {
        throw 'Expected a schema 2 catalog with a tools-vN release tag.'
    }
    $previous = if ($PreviousManifest) { Get-Content -LiteralPath $PreviousManifest -Raw | ConvertFrom-Json } else { $null }
    if ($previous -and ($previous.schema -notin @(1, 2) -or $null -eq $previous.tools)) {
        throw 'Previous catalog is invalid or uses an unsupported schema.'
    }
    if ($previous) {
        $previousIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($oldTool in $previous.tools) {
            if ($oldTool.id -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]*$' -or -not $previousIds.Add($oldTool.id) -or
                $oldTool.version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or $oldTool.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
                throw 'Previous catalog contains an invalid or duplicate tool entry.'
            }
        }
    }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $assets = [Collections.Generic.List[string]]::new()
    $assets.Add($manifestPath)
    $repository = $null
    if (@($manifest.tools).Count -eq 0) { throw 'Catalog contains no tools.' }
    foreach ($tool in $manifest.tools) {
        if ($tool.id -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]*$' -or -not $ids.Add($tool.id) -or
            $tool.assetName -cne "$($tool.id).dll" -or [string]::IsNullOrWhiteSpace($tool.displayName) -or
            $tool.version -notmatch '^\d+\.\d+\.\d+$' -or
            $tool.minInteropVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or
            $tool.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw "Invalid catalog entry: $($tool.id)" }
        $dllPath = Join-Path $bundlePath $tool.assetName
        $hash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
        if ($hash -ne $tool.sha256) { throw "Hash mismatch: $($tool.id)" }
        $assembly = [Reflection.AssemblyName]::GetAssemblyName($dllPath)
        if ($assembly.Name -cne $tool.id -or $assembly.Version.ToString(3) -ne $tool.version) {
            throw "Assembly identity/version mismatch: $($tool.id)"
        }
        $old = @($previous.tools | Where-Object id -eq $tool.id)
        if ($old.Count -gt 1) { throw "Duplicate previous catalog entry: $($tool.id)" }
        if ($old.Count -eq 1) {
            if ([version]$tool.version -lt [version]$old[0].version) { throw "Version decreased: $($tool.id)" }
            if ([version]$tool.version -eq [version]$old[0].version -and $hash -ne $old[0].sha256) {
                throw "Changed DLL requires a version bump: $($tool.id)"
            }
        }
        $toolManifestPath = Join-Path $bundlePath "$($tool.id).tool.json"
        $toolManifest = Get-Content -LiteralPath $toolManifestPath -Raw | ConvertFrom-Json
        if ($toolManifest.source -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$' -or
            $toolManifest.updateUrl -cne "https://github.com/$($toolManifest.source)/releases/download/$($manifest.release)/$($tool.id).tool.json") {
            throw "Per-tool manifest source/download URL is invalid: $($tool.id)"
        }
        if ($repository -and $repository -ne $toolManifest.source) { throw 'Bundle contains multiple source repositories.' }
        $repository = $toolManifest.source
        if ($toolManifest.assetName -cne $tool.assetName -or $toolManifest.version -ne $tool.version -or
            $toolManifest.sha256 -ne $tool.sha256 -or $toolManifest.minInteropVersion -ne $tool.minInteropVersion) {
            throw "Per-tool manifest differs from catalog: $($tool.id)"
        }
        $assets.Add($dllPath)
        $assets.Add($toolManifestPath)
    }
    if ($manifest.navigationData) {
        $nav = $manifest.navigationData
        if ($nav.assetName -ne [IO.Path]::GetFileName($nav.assetName)) { throw 'Invalid navigation asset name.' }
        $navPath = Join-Path $bundlePath $nav.assetName
        if ((Get-FileHash -LiteralPath $navPath -Algorithm SHA256).Hash -ne $nav.sha256 -or
            (Get-Item -LiteralPath $navPath).Length -ne $nav.sizeBytes) { throw 'Navigation asset differs from catalog.' }
        $assets.Add($navPath)
    }
    if ($manifest.toolManager) {
        $manager = $manifest.toolManager
        if ($manager.assetName -cne 'MESharp.ToolManager.dll' -or $manager.contractVersion -ne 1 -or
            $manager.minInteropVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$' -or
            $manager.version -notmatch '^\d+\.\d+\.\d+$' -or $manager.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
            throw 'Invalid tool manager metadata.'
        }
        $managerPath = Join-Path $bundlePath $manager.assetName
        $identity = [Reflection.AssemblyName]::GetAssemblyName($managerPath)
        if ($identity.Name -cne 'MESharp.ToolManager' -or $identity.Version.ToString(3) -ne $manager.version -or
            (Get-FileHash -LiteralPath $managerPath -Algorithm SHA256).Hash -ine $manager.sha256) { throw 'Tool manager identity/version/hash mismatch.' }
        if ($previous.toolManager -and ([version]$manager.version -lt [version]$previous.toolManager.version -or
            ([version]$manager.version -eq [version]$previous.toolManager.version -and $manager.sha256 -ine $previous.toolManager.sha256))) {
            throw 'Changed tool manager requires a version bump.'
        }
        $assets.Add($managerPath)
    }
    $hashes = @{}
    foreach ($asset in $assets) { $hashes[[IO.Path]::GetFileName($asset)] = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash }
    return [pscustomobject]@{ Directory = $bundlePath; Manifest = $manifest; Assets = $assets.ToArray(); Hashes = $hashes; Repository = $repository }
}

function Publish-ToolBundle([string]$Directory, [string]$Repository) {
    if ($Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid GitHub repository.' }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh CLI is required for publication.' }
    $bundle = Test-ToolBundle $Directory
    if ($bundle.Repository -ne $Repository) { throw 'Staged source differs from publication repository; restage with the intended -Repo.' }
    $tag = $bundle.Manifest.release
    # Get the prior published catalog rather than relying on repository-wide /latest/.
    $previousTag = $null
    for ($page = 1; $page -le 10; $page++) {
        # Parse JSON here instead of passing embedded jq quotes through Windows PowerShell.
        $pageJson = gh api "repos/$Repository/releases?per_page=100&page=$page"
        if ($LASTEXITCODE -ne 0) { throw 'Unable to establish the previous release; publication stopped.' }
        $releases = ($pageJson -join "`n") | ConvertFrom-Json
        $priorRelease = $releases | Where-Object {
            -not $_.draft -and -not $_.prerelease -and
            @($_.assets | Where-Object name -eq 'tools-manifest.json').Count -gt 0
        } | Select-Object -First 1
        if ($priorRelease) { $previousTag = $priorRelease.tag_name; break }
        if (@($releases).Count -lt 100) { break }
        if ($page -eq 10) { throw 'Unable to establish the previous release within 1000 releases.' }
    }
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('mesharp-release-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary | Out-Null
    try {
        if ($previousTag) {
            gh release download $previousTag --repo $Repository --pattern tools-manifest.json --dir $temporary
            if ($LASTEXITCODE -ne 0) { throw 'Unable to download previous catalog.' }
            $bundle = Test-ToolBundle $Directory (Join-Path $temporary 'tools-manifest.json')
        }
        $notesPath = Join-Path $temporary 'notes.md'
        $notes = "MESharp tools catalog $tag.`n`n" + (($bundle.Manifest.tools | ForEach-Object { "- $($_.displayName): $($_.version)" }) -join "`n")
        [IO.File]::WriteAllText($notesPath, $notes)
        $arguments = @('release', 'create', $tag, '--draft', '--repo', $Repository,
            '--title', "MESharp Tools $tag", '--notes-file', $notesPath) + $bundle.Assets
        gh @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Draft creation/upload failed; no release was published.' }
        $download = Join-Path $temporary 'uploaded'
        gh release download $tag --repo $Repository --dir $download
        if ($LASTEXITCODE -ne 0) { throw 'Unable to verify draft assets; draft left unpublished.' }
        foreach ($asset in $bundle.Assets) {
            $uploaded = Join-Path $download ([IO.Path]::GetFileName($asset))
            if ($bundle.Hashes[[IO.Path]::GetFileName($asset)] -ne
                (Get-FileHash -LiteralPath $uploaded -Algorithm SHA256).Hash) {
                throw "Uploaded bytes differ for $asset; draft left unpublished."
            }
        }
        gh release edit $tag --repo $Repository --draft=false
        if ($LASTEXITCODE -ne 0) { throw 'Could not publish validated draft.' }
        Write-Host "Published validated staged bundle: $tag" -ForegroundColor Green
    }
    finally {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporary)
        $expectedRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if ($resolvedTemporary.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolvedTemporary) -match '^mesharp-release-[a-f0-9]{32}$') {
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
}
