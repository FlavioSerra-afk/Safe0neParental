param(
  [switch]$AlsoDeleteVS = $true
)

$ErrorActionPreference = 'Stop'

Write-Host "Cleaning build artifacts..." -ForegroundColor Cyan

# Remove bin/obj
Get-ChildItem -Path . -Recurse -Force -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -in @('bin','obj','TestResults') } |
  ForEach-Object {
    try {
      Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
      Write-Host "Removed $($_.FullName)"
    } catch {
      Write-Warning "Failed to remove $($_.FullName): $($_.Exception.Message)"
    }
  }

if ($AlsoDeleteVS) {
  if (Test-Path .\.vs) {
    try {
      Remove-Item -LiteralPath .\.vs -Recurse -Force -ErrorAction Stop
      Write-Host "Removed .vs"
    } catch {
      Write-Warning "Failed to remove .vs: $($_.Exception.Message)"
    }
  }
}

Write-Host "Done." -ForegroundColor Green
