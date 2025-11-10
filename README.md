# Game Cache Cleaner — CrestPoint Digital (Fresh Build)
- WPF UI, tray icon (loads Assets/crestpoint.ico if present), weekly scheduler (schtasks), per‑launcher breakdown, excludes, dry‑run
- Headless `--auto-clean` for scheduled runs

## Build single-file EXE
pwsh -ExecutionPolicy Bypass -File .\publish_selfcontained.ps1
