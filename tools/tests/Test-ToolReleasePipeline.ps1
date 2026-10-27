[CmdletBinding()]
param([string]$ToolDll)
$ErrorActionPreference = 'Stop'
if (-not $ToolDll) { $ToolDll = Join-Path $PSScriptRoot '..\..\MESharpApiTool\bin\Debug\net10.0-windows\MESharpApiTool.dll' }
. (Join-Path $PSScriptRoot '..\ToolReleasePipeline.ps1')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('mesharp-pipeline-test-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$script:Published = $false
$script:CorruptUpload = $false
$script:ApiFails = $false
$script:DownloadFails = $false
$script:PreviousCatalog = $null
$script:BundleDirectory = Join-Path $temporary 'bundle'
$script:RemoteDirectory = Join-Path $temporary 'remote'
$script:Calls = [Collections.Generic.List[string]]::new()
function Assert-Throws([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        return
    }
    throw "Expected failure containing: $Message"
}
# Substitute the external boundary; no network calls or GitHub writes occur.
function gh {
    $global:LASTEXITCODE = 0
    $script:Calls.Add(($args -join ' '))
    if ($args[0] -eq 'api') {
        if ($script:ApiFails) { $global:LASTEXITCODE = 1 }
        elseif ($script:PreviousCatalog) {
            Write-Output '[{"tag_name":"tools-v1","draft":false,"prerelease":false,"assets":[{"name":"tools-manifest.json"}]}]'
        }
        else { Write-Output '[]' }
        return
    }
    if ($args[0] -ne 'release') { throw 'Unexpected gh call' }
    switch ($args[1]) {
        'create' {
            if ($args -notcontains '--draft' -or $args -notcontains '--notes-file') { throw 'Publication must use draft and notes file.' }
            [IO.Directory]::CreateDirectory($script:RemoteDirectory) | Out-Null
            Get-ChildItem -LiteralPath $script:BundleDirectory -File | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $script:RemoteDirectory -Force
            }
        }
        'download' {
            $destination = $args[[Array]::IndexOf($args, '--dir') + 1]
            if ($args -contains '--pattern') {
                Copy-Item -LiteralPath $script:PreviousCatalog -Destination (Join-Path $destination 'tools-manifest.json')
                return
            }
            if ($script:DownloadFails) { $global:LASTEXITCODE = 1; return }
            [IO.Directory]::CreateDirectory($destination) | Out-Null
            Get-ChildItem -LiteralPath $script:RemoteDirectory -File | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
            }
            if ($script:CorruptUpload) { [IO.File]::AppendAllText((Join-Path $destination 'tools-manifest.json'), 'corrupt') }
        }
        'edit' { $script:Published = $true }
        default { throw 'Unexpected gh release operation' }
    }
}
try {
    [IO.Directory]::CreateDirectory($script:BundleDirectory) | Out-Null
    $assembly = [Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $ToolDll).Path)
    $assetName = $assembly.Name + '.dll'
    $dll = Join-Path $script:BundleDirectory $assetName
    Copy-Item -LiteralPath $ToolDll -Destination $dll
    $entry = [ordered]@{ id = $assembly.Name; displayName = 'Test Tool'; assetName = $assetName;
        version = $assembly.Version.ToString(3); sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash;
        minInteropVersion = '1.0.0'; source = 'owner/repo';
        updateUrl = "https://github.com/owner/repo/releases/download/tools-v2/$($assembly.Name).tool.json" }
    $manifestPath = Join-Path $script:BundleDirectory 'tools-manifest.json'
    $toolManifestPath = Join-Path $script:BundleDirectory ($assembly.Name + '.tool.json')
    $catalog = [ordered]@{ schema = 2; release = 'tools-v2'; tools = @($entry) }
    function Save-Fixture {
        [IO.File]::WriteAllText($manifestPath, ($catalog | ConvertTo-Json -Depth 8))
        [IO.File]::WriteAllText($toolManifestPath, ($entry | ConvertTo-Json))
    }
    Save-Fixture
    $requirements = [pscustomobject]@{ _toolMinInteropVersions = [pscustomobject]@{ Api = '1.1.0'; Nav = '1.2.0' } }
    if ((Get-ToolMinimumInteropVersion $requirements 'Api' $null $true) -ne '1.1.0' -or
        (Get-ToolMinimumInteropVersion $requirements 'Nav' $null $true) -ne '1.2.0') { throw 'Requirements were not independent.' }
    $priorRequirement = [pscustomobject]@{ minInteropVersion = '1.1.0' }
    if ((Get-ToolMinimumInteropVersion $requirements 'Api' $priorRequirement $false) -ne '1.1.0') { throw 'Unchanged requirement changed.' }
    Assert-Throws { Get-ToolMinimumInteropVersion $requirements 'Nav' $priorRequirement $false } 'Bump Nav'
    Assert-Throws { Get-ToolMinimumInteropVersion $requirements 'Unknown' $null $true } '_toolMinInteropVersions.Unknown'
    $offline = Join-Path $temporary 'offline-baseline'
    [IO.Directory]::CreateDirectory($offline) | Out-Null
    Copy-Item -LiteralPath $manifestPath -Destination $offline
    Copy-Item -LiteralPath $dll -Destination $offline
    $baseline = Get-ToolReleaseBaseline 'owner/repo' (Join-Path $temporary 'unused') $offline
    $unchanged = [pscustomobject]@{ Id = $assembly.Name; Version = $assembly.Version.ToString(3) }
    $plan = @(Get-ToolReleasePlan @($unchanged) $baseline @())
    if ($plan.Count -ne 1 -or $plan[0].Build) { throw 'Unchanged tool was scheduled for rebuild.' }
    Assert-Throws { Get-ToolReleasePlan @($unchanged) $baseline @($assembly.Name) } 'Bump the version'
    $changed = [pscustomobject]@{ Id = $assembly.Name; Version = "$($assembly.Version.Major).$($assembly.Version.Minor).$($assembly.Version.Build + 1)" }
    $new = [pscustomobject]@{ Id = 'NewTool'; Version = '1.0.0' }
    $plan = @(Get-ToolReleasePlan @($changed, $new) $baseline @($assembly.Name))
    if ($plan.Count -ne 2 -or @($plan | Where-Object { !$_.Build }).Count) { throw 'Changed/new tools were not built.' }
    Assert-Throws { Get-ToolReleasePlan @($changed, $new) $baseline @('NewTool') } 'unselected tool'
    Assert-Throws { Get-ToolReleasePlan @($unchanged) $baseline @('MissingTool') } 'not public'
    $copied = Join-Path $temporary $assetName
    Copy-PublishedTool $baseline $baseline.Catalog.tools[0] $copied
    if ((Get-FileHash -LiteralPath $copied).Hash -ne (Get-FileHash -LiteralPath $dll).Hash) { throw 'Carry-forward changed DLL bytes.' }
    [IO.File]::AppendAllText((Join-Path $offline $assetName), 'corrupt')
    Assert-Throws { Copy-PublishedTool $baseline $baseline.Catalog.tools[0] $copied } 'Baseline hash mismatch'
    $bundle = Test-ToolBundle $script:BundleDirectory
    if ($bundle.Assets.Count -ne 3) { throw 'Expected DLL, per-tool manifest, and catalog.' }
    $previousPath = Join-Path $temporary 'previous.json'
    $previous = $catalog | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $previous.tools[0].sha256 = ('b' * 64)
    [IO.File]::WriteAllText($previousPath, ($previous | ConvertTo-Json -Depth 8))
    Assert-Throws { Test-ToolBundle $script:BundleDirectory $previousPath } 'version bump'
    $catalog.schema = 99
    Save-Fixture
    Assert-Throws { Test-ToolBundle $script:BundleDirectory } 'schema 2'
    $catalog.schema = 2
    $entry.sha256 = 'bad'
    Save-Fixture
    Assert-Throws { Test-ToolBundle $script:BundleDirectory } 'Invalid catalog entry'
    $entry.sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    Save-Fixture
    Publish-ToolBundle $script:BundleDirectory 'owner/repo'
    if (-not $script:Published) { throw 'Verified draft was not published.' }
    $script:Published = $false
    $script:PreviousCatalog = $previousPath
    $before = $script:Calls.Count
    Assert-Throws { Publish-ToolBundle $script:BundleDirectory 'owner/repo' } 'version bump'
    if (($script:Calls.GetRange($before, $script:Calls.Count - $before) | Where-Object { $_ -like 'release create*' }).Count) {
        throw 'Version baseline failure created a draft.'
    }
    $script:PreviousCatalog = $null
    $script:ApiFails = $true
    Assert-Throws { Publish-ToolBundle $script:BundleDirectory 'owner/repo' } 'Unable to establish'
    $script:ApiFails = $false
    $script:DownloadFails = $true
    Assert-Throws { Publish-ToolBundle $script:BundleDirectory 'owner/repo' } 'Unable to verify'
    if ($script:Published) { throw 'Unverified draft was published.' }
    $script:DownloadFails = $false
    Assert-Throws { Publish-ToolBundle $script:BundleDirectory 'another/repo' } 'differs from publication'
    $script:CorruptUpload = $true
    Assert-Throws { Publish-ToolBundle $script:BundleDirectory 'owner/repo' } 'Uploaded bytes differ'
    if ($script:Published) { throw 'Corrupted upload was published.' }
    if (($script:Calls | Where-Object { $_ -match 'build' }).Count) { throw 'Publishing invoked a build.' }
    Write-Host 'PASS: selective builds, byte-identical carry-forward, baseline corruption rejection, version bump enforcement, draft verification, no rebuild on publish.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $expectedRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -match '^mesharp-pipeline-test-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
