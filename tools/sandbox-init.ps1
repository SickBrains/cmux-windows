# Sandbox init script — installs .NET and builds cmux
$ErrorActionPreference = 'Continue'

# Log to file for debugging
$log = "C:\cmux\tools\sandbox-log.txt"
"[$(Get-Date)] Sandbox init starting..." | Out-File $log

# Install .NET 10 SDK
"[$(Get-Date)] Installing .NET SDK..." | Out-File $log -Append
try {
    $dotnetInstaller = "$env:TEMP\dotnet-install.ps1"
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $dotnetInstaller
    & $dotnetInstaller -Channel 10.0 -InstallDir 'C:\dotnet'
    $env:PATH = "C:\dotnet;$env:PATH"
    $env:DOTNET_ROOT = "C:\dotnet"
    [Environment]::SetEnvironmentVariable('PATH', "C:\dotnet;$([Environment]::GetEnvironmentVariable('PATH', 'Machine'))", 'Machine')
    [Environment]::SetEnvironmentVariable('DOTNET_ROOT', 'C:\dotnet', 'Machine')
    "[$(Get-Date)] .NET installed: $(C:\dotnet\dotnet.exe --version)" | Out-File $log -Append
} catch {
    "[$(Get-Date)] .NET install failed: $_" | Out-File $log -Append
}

# Copy source to local sandbox filesystem (host DLLs are locked)
"[$(Get-Date)] Copying source to sandbox local..." | Out-File $log -Append
try {
    Copy-Item -Path "C:\cmux\src" -Destination "C:\sandbox-build\src" -Recurse -Force
    Copy-Item -Path "C:\cmux\tests" -Destination "C:\sandbox-build\tests" -Recurse -Force
    Copy-Item -Path "C:\cmux\*.sln" -Destination "C:\sandbox-build\" -Force
    Copy-Item -Path "C:\cmux\Directory.Build.props" -Destination "C:\sandbox-build\" -Force -ErrorAction SilentlyContinue
    Copy-Item -Path "C:\cmux\Directory.Packages.props" -Destination "C:\sandbox-build\" -Force -ErrorAction SilentlyContinue
    Copy-Item -Path "C:\cmux\nuget.config" -Destination "C:\sandbox-build\" -Force -ErrorAction SilentlyContinue
    "[$(Get-Date)] Copy complete" | Out-File $log -Append
} catch {
    "[$(Get-Date)] Copy failed: $_" | Out-File $log -Append
}

# Build cmux from local copy
"[$(Get-Date)] Building cmux..." | Out-File $log -Append
try {
    Set-Location C:\sandbox-build
    C:\dotnet\dotnet.exe build 2>&1 | Out-File $log -Append
    "[$(Get-Date)] Build complete" | Out-File $log -Append
} catch {
    "[$(Get-Date)] Build failed: $_" | Out-File $log -Append
}

# Launch cmux from local build
"[$(Get-Date)] Launching cmux..." | Out-File $log -Append
try {
    Start-Process "C:\sandbox-build\src\Cmux\bin\Debug\net10.0-windows10.0.17763.0\cmuxw.exe"
    "[$(Get-Date)] cmux launched" | Out-File $log -Append
} catch {
    "[$(Get-Date)] Launch failed: $_" | Out-File $log -Append
}

# Start the relay (watches for commands from host via mapped folder)
"[$(Get-Date)] Starting relay..." | Out-File $log -Append
Start-Process powershell -ArgumentList "-ExecutionPolicy Bypass -File C:\cmux\tools\sandbox-relay.ps1" -WindowStyle Minimized
"[$(Get-Date)] Sandbox init complete" | Out-File $log -Append
