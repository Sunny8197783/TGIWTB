$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $root
$game='C:/Users/gram/Downloads/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
dotnet build --no-restore -v quiet *> docs/review/forest-build.log
if ($LASTEXITCODE -ne 0) { Get-Content docs/review/forest-build.log -Tail 15; throw 'Build failed' }
$import=Start-Process -FilePath $game -WindowStyle Hidden -ArgumentList '--headless --editor --path . --import' -RedirectStandardOutput docs/review/forest-import.log -RedirectStandardError docs/review/forest-import-errors.log -Wait -PassThru
if ($import.ExitCode -ne 0 -or (Select-String -Path docs/review/forest-import.log,docs/review/forest-import-errors.log -Pattern '^ERROR:|Unhandled exception' -Quiet)) { throw 'Asset import failed' }
foreach($check in @('reference-check','combat-check','selfcheck')) {
    $run=Start-Process -FilePath $game -WindowStyle Hidden -ArgumentList "--headless --path . -- --$check" -RedirectStandardOutput "docs/review/forest-$check.log" -RedirectStandardError "docs/review/forest-$check-errors.log" -Wait -PassThru
    Get-Content "docs/review/forest-$check.log" -Tail 5
    if($run.ExitCode -ne 0 -or (Select-String -Path "docs/review/forest-$check.log","docs/review/forest-$check-errors.log" -Pattern '^ERROR:|Unhandled exception' -Quiet)) { Get-Content "docs/review/forest-$check-errors.log" -Tail 45; throw "$check failed" }
}
