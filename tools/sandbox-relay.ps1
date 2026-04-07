# Sandbox relay — runs INSIDE the sandbox, watches for commands from host
$ErrorActionPreference = 'Continue'
$cmdFile = "C:\cmux\tools\sandbox-cmd.txt"
$resultFile = "C:\cmux\tools\sandbox-result.txt"
$screenFile = "C:\cmux\tools\sandbox-screen.png"
$cmuxCli = "C:\sandbox-build\src\Cmux.Cli\bin\Debug\net10.0-windows\cmux.exe"
$logFile = "C:\cmux\tools\sandbox-relay-log.txt"

$env:PATH = "C:\dotnet;$env:PATH"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

public class SandboxCapture {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }

    public static void Capture(IntPtr hwnd, string path) {
        RECT r;
        GetWindowRect(hwnd, out r);
        int w = r.R - r.L;
        int h = r.B - r.T;
        if (w <= 0 || h <= 0) return;
        using (var bmp = new Bitmap(w, h)) {
            using (var g = Graphics.FromImage(bmp)) {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
"@ -ReferencedAssemblies System.Drawing

function Log($msg) { "[$(Get-Date -Format 'HH:mm:ss')] $msg" | Out-File $logFile -Append }
function TakeScreenshot {
    try {
        $p = Get-Process cmuxw -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) {
            [SandboxCapture]::Capture($p.MainWindowHandle, $screenFile)
        }
    } catch {}
}

Log "Relay starting..."

# Clear stale command file
if (Test-Path $cmdFile) { Remove-Item $cmdFile -Force }

# Main loop — poll for commands
while ($true) {
    Start-Sleep -Milliseconds 500

    if (Test-Path $cmdFile) {
        $raw = Get-Content $cmdFile -Raw -ErrorAction SilentlyContinue
        if ($raw) {
            Remove-Item $cmdFile -Force
            $cmd = $raw.Trim()
            Log "CMD: $cmd"

            $output = ""
            try {
                if ($cmd -eq "screenshot") {
                    TakeScreenshot
                    $output = "screenshot saved to $screenFile"
                }
                elseif ($cmd.StartsWith("cmux ")) {
                    $args = $cmd.Substring(5)
                    $output = & $cmuxCli $args.Split(' ') 2>&1 | Out-String
                }
                elseif ($cmd -eq "rebuild") {
                    Log "Rebuild requested"
                    # Kill running cmux
                    Get-Process cmuxw -ErrorAction SilentlyContinue | Stop-Process -Force
                    Get-Process cmux-daemon -ErrorAction SilentlyContinue | Stop-Process -Force
                    Start-Sleep -Seconds 1

                    # Re-copy source from mapped folder
                    Copy-Item -Path "C:\cmux\src" -Destination "C:\sandbox-build\src" -Recurse -Force
                    Copy-Item -Path "C:\cmux\tests" -Destination "C:\sandbox-build\tests" -Recurse -Force
                    Copy-Item -Path "C:\cmux\*.sln" -Destination "C:\sandbox-build\" -Force
                    Copy-Item -Path "C:\cmux\Directory.Build.props" -Destination "C:\sandbox-build\" -Force -ErrorAction SilentlyContinue
                    Copy-Item -Path "C:\cmux\Directory.Packages.props" -Destination "C:\sandbox-build\" -Force -ErrorAction SilentlyContinue

                    # Rebuild
                    Set-Location C:\sandbox-build
                    $buildOut = & C:\dotnet\dotnet.exe build 2>&1 | Out-String

                    # Relaunch
                    Start-Process "C:\sandbox-build\src\Cmux\bin\Debug\net10.0-windows10.0.17763.0\cmuxw.exe"
                    Start-Sleep -Seconds 2

                    $output = "Rebuild complete.`n$buildOut"
                }
                elseif ($cmd -eq "exit") {
                    Log "Exit requested"
                    $output = "relay exiting"
                    $output | Out-File $resultFile -Encoding utf8
                    break
                }
                else {
                    # Run as PowerShell command
                    $output = Invoke-Expression $cmd 2>&1 | Out-String
                }
            } catch {
                $output = "ERROR: $_"
            }

            Log "RESULT: $($output.Substring(0, [Math]::Min(200, $output.Length)))"
            $output | Out-File $resultFile -Encoding utf8

            # Always take a screenshot after command
            TakeScreenshot
        }
    }
}
