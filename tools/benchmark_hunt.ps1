param(
    [ValidatePattern('^[a-z0-9-]+$')][string]$Label='after',
    [ValidateRange(20,300)][int]$Seconds=45
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $root
$game='C:/Users/gram/Downloads/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
if(Get-Process -Name 'Godot*' -ErrorAction SilentlyContinue) { throw 'Close other Godot instances before benchmarking.' }
# Fixed viewpoints: the same native terrain, props and nearby actor silhouettes in both runs.
# The input-only 30-minute hunt is a separate combat/route soak, not mixed into these measurements.
$points=[ordered]@{town='3088,3728';slime='10384,5936';goblin='10224,2448'}
foreach($point in $points.GetEnumerator()) {
    $log="docs/review/hunt-$Label-$($point.Key).log"
    $errors="docs/review/hunt-$Label-$($point.Key)-errors.log"
    $run=Start-Process -FilePath $game -WindowStyle Hidden -ArgumentList "--path . --resolution 1280x720 -- --capture --capture-fresh --capture-no-images --capture-seconds=$Seconds --capture-start=$($point.Value)" -RedirectStandardOutput $log -RedirectStandardError $errors -Wait -PassThru
    if($run.ExitCode -ne 0 -or (Select-String -Path $log,$errors -Pattern '^ERROR:|Unhandled exception' -Quiet)) { throw "Benchmark failed: $($point.Key)" }
    Select-String -Path $log -Pattern 'world-ready|PerfSummary'
}
