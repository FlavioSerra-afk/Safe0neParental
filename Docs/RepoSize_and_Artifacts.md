# Repo size and build artifacts

If your working copy or ZIP export is >1GB, it is almost always because build artifacts and IDE caches were included.

## What should NOT be in source control or shared source zips
- `.vs/` (Visual Studio cache)
- `**/bin/` and `**/obj/` (compiled outputs and intermediate files)
- `**/TestResults/` (test run artifacts)
- Local mode SSOT/data files:
  - `control-plane*.json`
  - `enrollment*.json`
  - `activity.outbox.*.json`

## How to clean
Run:

```powershell
./tools/clean_repo.ps1
```

## How to create a source-only zip

```powershell
./tools/pack_source.ps1 -OutPath ..\Safe0ne_Parental_SOURCE.zip
```
