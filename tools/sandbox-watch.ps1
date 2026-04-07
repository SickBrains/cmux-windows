# Host-side file watcher — triggers sandbox rebuild when source files change
# Usage: powershell -ExecutionPolicy Bypass -File tools/sandbox-watch.ps1
param(
    [string]$WatchDir = "src",
    [int]$DebounceSeconds = 3
)

$cmdFile = Join-Path $PSScriptRoot "sandbox-cmd.txt"
$lastTrigger = [DateTime]::MinValue

$watcher = [System.IO.FileSystemWatcher]::new($WatchDir)
$watcher.Filter = "*.*"
$watcher.IncludeSubdirectories = $true
$watcher.NotifyFilter = [System.IO.NotifyFilters]::LastWrite -bor [System.IO.NotifyFilters]::FileName
$watcher.EnableRaisingEvents = $true

$action = {
    $path = $Event.SourceEventArgs.FullPath
    $ext = [System.IO.Path]::GetExtension($path)
    if ($ext -notin '.cs', '.xaml', '.csproj', '.props') { return }

    $now = [DateTime]::Now
    $elapsed = ($now - $script:lastTrigger).TotalSeconds
    if ($elapsed -lt $script:DebounceSeconds) { return }

    $script:lastTrigger = $now
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Change: $path — triggering rebuild..."
    "rebuild" | Out-File $script:cmdFile -Encoding utf8
}

Register-ObjectEvent $watcher Changed -Action $action | Out-Null
Register-ObjectEvent $watcher Created -Action $action | Out-Null
Register-ObjectEvent $watcher Renamed -Action $action | Out-Null

Write-Host "Watching $WatchDir for .cs/.xaml/.csproj changes (debounce: ${DebounceSeconds}s)"
Write-Host "Press Ctrl+C to stop."

try { while ($true) { Start-Sleep -Seconds 1 } }
finally {
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()
}
