param([string]$Manifest = 'docs/pixellab-assets.json')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$items = Get-Content -Raw (Join-Path $root $Manifest) | ConvertFrom-Json
function Test-Png([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $stream = [IO.File]::OpenRead($Path)
    try {
        $bytes = New-Object byte[] 8
        if ($stream.Length -lt 20 -or $stream.Read($bytes,0,8) -ne 8 -or [BitConverter]::ToString($bytes) -ne '89-50-4E-47-0D-0A-1A-0A') { return $false }
        [void]$stream.Seek(-12,[IO.SeekOrigin]::End)
        $end = New-Object byte[] 12
        return $stream.Read($end,0,12) -eq 12 -and [BitConverter]::ToString($end) -eq '00-00-00-00-49-45-4E-44-AE-42-60-82'
    }
    finally { $stream.Dispose() }
}
$pending = @(foreach ($item in $items) {
    $uri = [Uri]$item.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'backblaze.pixellab.ai') { throw 'Unexpected asset host' }
    $target = [IO.Path]::GetFullPath((Join-Path $root $item.path))
    if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar)) { throw 'Invalid asset path' }
    if (Test-Png $target) { continue }
    @{ Uri=$uri; Target=$target }
})
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(45)
try {
    for ($start=0; $start -lt $pending.Count; $start+=4) {
        $batch = @($pending[$start..([Math]::Min($start+3,$pending.Count-1))])
        foreach ($entry in $batch) { $entry.Task=$client.GetByteArrayAsync([Uri]$entry.Uri) }
        foreach ($entry in $batch) {
            for ($attempt=0; $attempt -lt 3; $attempt++) {
                try {
                    $bytes=$entry.Task.GetAwaiter().GetResult()
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($entry.Target)) | Out-Null
                    [IO.File]::WriteAllBytes($entry.Target+'.part',$bytes)
                    if (-not (Test-Png ($entry.Target+'.part'))) { throw 'Downloaded asset is not a PNG' }
                    Move-Item -LiteralPath ($entry.Target+'.part') -Destination $entry.Target -Force
                    break
                }
                catch {
                    if ($attempt -eq 2) { throw }
                    Start-Sleep -Seconds 2
                    $entry.Task=$client.GetByteArrayAsync([Uri]$entry.Uri)
                }
            }
        }
    }
}
finally { $client.Dispose() }
Write-Output ('Actor assets present: ' + $items.Count)
