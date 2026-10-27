[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDll,
    [Parameter(Mandatory)][string]$ToolRoot,
    [Parameter(Mandatory)][string]$Id,
    [Parameter(Mandatory)][string]$DisplayName,
    [string]$Repository = 'iStokee/MESharpTools'
)
$ErrorActionPreference = 'Stop'
if ($Id -notmatch '^[A-Za-z0-9_][A-Za-z0-9_.-]*$' -or $Repository -notmatch '^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$') {
    throw 'Invalid tool id or repository.'
}
$assembly = [Reflection.AssemblyName]::GetAssemblyName($SourceDll)
if ($assembly.Name -cne $Id) { throw 'Tool id differs from assembly identity.' }
$directory = Join-Path ([IO.Path]::GetFullPath($ToolRoot)) $Id
[IO.Directory]::CreateDirectory($directory) | Out-Null
$lockPath = Join-Path $directory '.installation.lock'
$deadline = [DateTime]::UtcNow.AddMinutes(2)
$handle = $null
while ($null -eq $handle) {
    try { $handle = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] {
        if ([DateTime]::UtcNow -ge $deadline) { throw }
        Write-Verbose 'Waiting for another installer to release the tool lock.'
        Start-Sleep -Milliseconds 100
    }
}
$manifestPath = Join-Path $directory 'tool.json'
$destination = Join-Path $directory "$Id.dll"
$transaction = [guid]::NewGuid().ToString('N')
$staged = "$destination.$transaction.download"
$backup = "$destination.$transaction.backup"
$manifestTemporary = "$manifestPath.$transaction.tmp"
$previousManifest = $null
$replaced = $false
$committed = $false
try {
    $previousManifest = if (Test-Path -LiteralPath $manifestPath) { [IO.File]::ReadAllBytes($manifestPath) } else { $null }
    if ($previousManifest) {
        $existing = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        if ($existing.source -and $existing.source -ne $Repository) { throw 'Existing installation belongs to another source.' }
    }
    [IO.File]::Copy($SourceDll, $staged)
    $hash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifest = [ordered]@{ displayName = $DisplayName; assetName = "$Id.dll";
        version = $assembly.Version.ToString(3); source = $Repository; sha256 = $hash }
    [IO.File]::WriteAllText($manifestTemporary, ($manifest | ConvertTo-Json))
    if ([IO.File]::Exists($destination)) { [IO.File]::Copy($destination, $backup) }
    # Windows PowerShell converts $null string arguments to ""; NullString passes a real CLR null.
    if ([IO.File]::Exists($destination)) { [IO.File]::Replace($staged, $destination, [NullString]::Value) }
    else { [IO.File]::Move($staged, $destination) }
    $replaced = $true
    if ([IO.File]::Exists($manifestPath)) { [IO.File]::Replace($manifestTemporary, $manifestPath, [NullString]::Value) }
    else { [IO.File]::Move($manifestTemporary, $manifestPath) }
    $committed = $true
}
catch {
    if ($replaced -and -not $committed) {
        if ([IO.File]::Exists($backup)) { [IO.File]::Replace($backup, $destination, [NullString]::Value) }
        else { [IO.File]::Delete($destination) }
        if ($previousManifest) { [IO.File]::WriteAllBytes($manifestPath, $previousManifest) }
        elseif ([IO.File]::Exists($manifestPath)) { [IO.File]::Delete($manifestPath) }
    }
    throw
}
finally {
    foreach ($temporary in @($staged, $manifestTemporary)) {
        try { [IO.File]::Delete($temporary) } catch { Write-Warning "Could not clean $temporary" }
    }
    if ($committed) { try { [IO.File]::Delete($backup) } catch { Write-Warning "Could not clean $backup" } }
    $handle.Dispose()
}
Write-Host "Deployed $Id $($assembly.Version.ToString(3)); reload the tool explicitly to activate it."
