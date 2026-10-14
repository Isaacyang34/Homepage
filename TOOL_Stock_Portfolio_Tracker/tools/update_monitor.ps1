# ==============================================================================
# Stock Portfolio Tracker - External Update Lifecycle Monitor
# Runs independently outside the main application process.
# Equipped with Global Named Mutex to prevent multi-instance flooding.
# Equipped with Liveness Watchdog to cleanly exit if user normally closes window.
# ==============================================================================
param(
    [string]$TargetDir = "",
    [int]$TargetPid = 0
)

# 1. Global Named Mutex - Single Instance Guard
$createdNew = $false
$mutexName = "Global\StockPortfolioTracker_UpdateMonitor_Mutex"
$mutex = New-Object System.Threading.Mutex($true, $mutexName, [ref]$createdNew)
if (-not $createdNew) {
    # Another instance is already active, exit immediately without duplicate logging
    exit 0
}

if (-not $TargetDir) {
    $TargetDir = Split-Path -Parent $PSScriptRoot
}
if (-not $TargetDir) {
    $TargetDir = Get-Location
}

$LogFile = Join-Path $TargetDir "update_monitor.log"

function Write-MonitorLog {
    param([string]$Tag, [string]$Message)
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff")
    $line = "[$ts] [$Tag] $Message"
    Add-Content -Path $LogFile -Value $line
    Write-Output $line
}

# Win32 API for dialog window inspection
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class Win32Helper {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public static string GetDialogText(IntPtr hWnd) {
        StringBuilder sb = new StringBuilder(1024);
        GetWindowText(hWnd, sb, 1024);
        return sb.ToString();
    }
}
"@ -ErrorAction SilentlyContinue

Write-MonitorLog "MONITOR_START" ("=" * 70)
Write-MonitorLog "MONITOR_START" "External Update Lifecycle Monitor started (Monitor PID: $PID, Mutex: Acquired)"
Write-MonitorLog "MONITOR_START" "Target directory: $TargetDir"
Write-MonitorLog "MONITOR_START" "Target child PID: $TargetPid"

$parentPid = 0
$parentExe = "unknown"

try {
    $proc = Get-CimInstance Win32_Process -Filter "ProcessId = $TargetPid" -ErrorAction SilentlyContinue
    if ($proc) {
        $parentPid = [int]$proc.ParentProcessId
        $pProc = Get-CimInstance Win32_Process -Filter "ProcessId = $parentPid" -ErrorAction SilentlyContinue
        if ($pProc) {
            $parentExe = $pProc.Name
        }
        Write-MonitorLog "PROC_AUDIT" "Target process: $($proc.Name), Parent PID: $parentPid ($parentExe)"
    }
} catch {
    Write-MonitorLog "PROC_WARN" "Could not parse parent process: $_"
}

$tempPath = [System.IO.Path]::GetTempPath()
$initMeiList = Get-ChildItem -Path $tempPath -Filter "_MEI*" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
Write-MonitorLog "ENV_AUDIT" "TEMP path: $tempPath"
Write-MonitorLog "ENV_AUDIT" "Active _MEI directories count: $($initMeiList.Count)"

$startTime = Get-Date
$childExited = $false
$childExitTime = $null
$parentExited = $false
$parentExitTime = $null
$newExeDetected = $false
$newExePid = 0
$finished = $false

try {
    # Monitor loop (max 120 seconds)
    while ((Get-Date) -lt $startTime.AddSeconds(120) -and -not $finished) {
        Start-Sleep -Milliseconds 250

        # 1. Check if target child PID has exited
        if (-not $childExited -and $TargetPid -gt 0) {
            $checkChild = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
            if (-not $checkChild) {
                $childExited = $true
                $childExitTime = Get-Date
                Write-MonitorLog "EVENT_EXIT" "Target child process (PID: $TargetPid) has exited."

                if ($parentPid -gt 0) {
                    $checkParent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
                    if ($checkParent) {
                        Write-MonitorLog "PARENT_STATUS" "Parent bootloader (PID: $parentPid) is ALIVE (cleaning up)..."
                    } else {
                        $parentExited = $true
                        $parentExitTime = Get-Date
                        Write-MonitorLog "PARENT_STATUS" "Parent bootloader (PID: $parentPid) already terminated."
                    }
                }
            }
        }

        # 2. Check when parent bootloader exits
        if ($childExited -and -not $parentExited -and $parentPid -gt 0) {
            $checkParent = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
            if (-not $checkParent) {
                $parentExited = $true
                $parentExitTime = Get-Date
                $elapsedMs = [math]::Round(($parentExitTime - $childExitTime).TotalMilliseconds)
                Write-MonitorLog "PARENT_EXIT" "Parent bootloader (PID: $parentPid) has terminated. Cleanup delay: ${elapsedMs} ms"
            }
        }

        # 3. Liveness Watchdog: if child exited and no new process spawned within 8 seconds, normal exit detected
        if ($childExited -and -not $newExeDetected) {
            if ((Get-Date) -gt $childExitTime.AddSeconds(8)) {
                Write-MonitorLog "TIMEOUT_NORMAL_CLOSE" "Target process exited without restart request. User closed application. Monitor exiting cleanly."
                $finished = $true
                break
            }
        }

        # 4. Detect new Stock_Portfolio_Tracker.exe process
        if ($childExited) {
            $activeExes = Get-CimInstance Win32_Process -Filter "Name like 'Stock_Portfolio_Tracker%'" -ErrorAction SilentlyContinue
            foreach ($exe in $activeExes) {
                $ePid = [int]$exe.ProcessId
                if ($ePid -ne $TargetPid -and $ePid -ne $parentPid -and -not $newExeDetected) {
                    $newExeDetected = $true
                    $newExePid = $ePid
                    $parentStatusStr = "TERMINATED"
                    if (-not $parentExited) {
                        $parentStatusStr = "STILL_ALIVE_COLLISION"
                    }
                    Write-MonitorLog "NEW_PROC_SPAWNED" "New process detected! PID: $newExePid, ParentPID: $($exe.ParentProcessId), OldParentStatus: $parentStatusStr"
                    Write-MonitorLog "NEW_PROC_CMD" "CommandLine: $($exe.CommandLine)"
                }
            }
        }

        # 5. Check for Error dialogs or Success windows
        [Win32Helper]::EnumWindows({
            param($hWnd, $lParam)
            if ([Win32Helper]::IsWindowVisible($hWnd)) {
                $title = [Win32Helper]::GetDialogText($hWnd)
                if ($title -eq "Error" -or $title -like "*Failed to load Python*") {
                    $wPid = 0
                    [Win32Helper]::GetWindowThreadProcessId($hWnd, [ref]$wPid)
                    Write-MonitorLog "ERROR_DIALOG_DETECTED" "Error dialog detected! Title: '$title', Window PID: $wPid"
                    
                    $curMei = Get-ChildItem -Path $tempPath -Filter "_MEI*" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name
                    Write-MonitorLog "ERROR_ENV_DUMP" "Active _MEI in TEMP at failure: $($curMei -join ', ')"
                    $script:finished = $true
                    return $false
                }
                if ($title -like "*Stock Portfolio Tracker*" -or $title -like "*本地端台美股庫存*") {
                    $wPid = 0
                    [Win32Helper]::GetWindowThreadProcessId($hWnd, [ref]$wPid)
                    if ($wPid -ne $TargetPid) {
                        Write-MonitorLog "SUCCESS_DETECTED" "SUCCESS! New main window rendered properly. PID: $wPid, Title: '$title'"
                        $script:finished = $true
                        return $false
                    }
                }
            }
            return $true
        }, [IntPtr]::Zero) | Out-Null
    }
}
finally {
    try {
        $mutex.ReleaseMutex()
        $mutex.Dispose()
    } catch {}
}

Write-MonitorLog "MONITOR_END" "External monitor loop finished."
Write-MonitorLog "MONITOR_END" ("=" * 70)
