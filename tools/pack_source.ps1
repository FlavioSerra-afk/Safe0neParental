param(
  [string]$OutPath = "..\Safe0ne_Parental_SOURCE.zip"
)

$ErrorActionPreference = 'Stop'

# Create a source-only zip that excludes build artifacts and local data.
# Requires PowerShell 5+ (Compress-Archive).

Write-Host "Creating source zip: $OutPath" -ForegroundColor Cyan

$root = (Resolve-Path .).Path

# Ensure clean-ish tree
& "$PSScriptRoot\clean_repo.ps1" -AlsoDeleteVS

# Build exclude list
$exclude = @(
  '\\bin\\',
  '\\obj\\',
  '\\.vs\\',
  '\\TestResults\\',
  'control-plane',
  'enrollment',
  'activity.outbox'
)

$tmp = Join-Path $env:TEMP ("safeone_pack_" + [Guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Path $tmp | Out-Null

# Copy everything except excluded patterns
Get-ChildItem -Path $root -Force |
  ForEach-Object {
    $name = $_.Name
    # Skip obvious excludes at top level
    if ($name -in @('bin','obj','TestResults','.vs')) { return }
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $tmp $name) -Recurse -Force
  }

# Remove excluded patterns inside temp copy
Get-ChildItem -Path $tmp -Recurse -Force |
  Where-Object {
    $p = $_.FullName
    foreach ($ex in $exclude) {
      if ($p -match [Regex]::Escape($ex)) { return $true }
    }
    return $false
  } |
  Sort-Object FullName -Descending |
  ForEach-Object {
    try { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue } catch {}
  }

if (Test-Path $OutPath) { Remove-Item -LiteralPath $OutPath -Force }
Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $OutPath

Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Created: $OutPath" -ForegroundColor Green
