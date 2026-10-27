<#
.SYNOPSIS
    Creates tool.json beside an existing tool DLL; no files are copied or published.
.EXAMPLE
    .\New-ToolManifest.ps1 -ToolDll C:\MyTool\MyTool.dll -DisplayName 'My Tool'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ToolDll,
    [string]$DisplayName,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$dllPath = (Resolve-Path -LiteralPath $ToolDll).Path
if ([IO.Path]::GetExtension($dllPath) -ine '.dll') { throw 'ToolDll must name a managed tool DLL.' }
$assembly = [Reflection.AssemblyName]::GetAssemblyName($dllPath)
if ($assembly.Name -eq 'csharp_interop') { throw 'csharp_interop is supplied by the host and must not be registered as a tool.' }
if (!$DisplayName) { $DisplayName = $assembly.Name }
if ([string]::IsNullOrWhiteSpace($DisplayName)) { throw 'DisplayName must not be blank.' }
if ([IO.Path]::GetFileNameWithoutExtension($dllPath) -cne $assembly.Name) {
    throw 'The DLL filename must match its assembly name.'
}
$manifestPath = Join-Path ([IO.Path]::GetDirectoryName($dllPath)) 'tool.json'
if ([IO.File]::Exists($manifestPath) -and !$Force) {
    throw 'tool.json already exists. Use -Force to regenerate it from the DLL.'
}
$manifest = [ordered]@{
    displayName = $DisplayName
    assetName = [IO.Path]::GetFileName($dllPath)
    version = $assembly.Version.ToString(3)
}
$temporary = $manifestPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($manifestPath)) { [IO.File]::Replace($temporary, $manifestPath, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $manifestPath) }
}
finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
Write-Host "Created $manifestPath ($($assembly.Name) $($manifest.version)). Add this folder as a tool source to run it directly."
