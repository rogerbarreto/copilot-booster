# Warp Test Window Cleanup Helper
#
# This script detects and optionally closes orphaned Warp windows created by WarpMultiTabE2ETests.
# Orphan detection heuristic: Window title matches pattern "TAB-{n}-{guid8}" or "echo.*TAB-"
# 
# SAFE: Only offers to close windows matching the test pattern. Never closes Roger's real work windows.

param(
    [switch]$DryRun = $false,
    [switch]$AutoClose = $false
)

Add-Type @"
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    
    public class WarpCleanup {
        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        public const uint WM_CLOSE = 0x0010;
    }
"@

$warpProcs = Get-Process warp -ErrorAction SilentlyContinue
if ($warpProcs.Count -eq 0) {
    Write-Host "No warp.exe processes running." -ForegroundColor Green
    exit 0
}

Write-Host "Scanning for orphaned test windows..." -ForegroundColor Cyan

$orphanedWindows = [System.Collections.Generic.List[object]]::new()

$callback = {
    param($hwnd, $lParam)
    $pidOut = 0
    [WarpCleanup]::GetWindowThreadProcessId($hwnd, [ref]$pidOut) | Out-Null
    
    if ($warpProcs.Id -contains $pidOut -and [WarpCleanup]::IsWindowVisible($hwnd)) {
        $sb = [System.Text.StringBuilder]::new(256)
        $len = [WarpCleanup]::GetWindowText($hwnd, $sb, $sb.Capacity)
        if ($len -gt 0) {
            $title = $sb.ToString()
            # Test pattern: "TAB-2-abc123ef" or "echo "TAB-..."
            if ($title -match 'TAB-\d+-[a-f0-9]{8}' -or $title -match 'echo.*TAB-') {
                $orphanedWindows.Add([PSCustomObject]@{ 
                    PID = $pidOut
                    HWND = $hwnd
                    Title = $title 
                })
            }
        }
    }
    return $true
}

$delegateType = [WarpCleanup+EnumWindowsProc]
[WarpCleanup]::EnumWindows(($callback -as $delegateType), [IntPtr]::Zero) | Out-Null

if ($orphanedWindows.Count -eq 0) {
    Write-Host "✓ No orphaned test windows found." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($orphanedWindows.Count) orphaned test window(s):" -ForegroundColor Yellow
foreach ($window in $orphanedWindows) {
    Write-Host "  HWND $($window.HWND): '$($window.Title)'" -ForegroundColor Yellow
}
Write-Host ""

if ($DryRun) {
    Write-Host "[DRY RUN] Would close $($orphanedWindows.Count) window(s)." -ForegroundColor Cyan
    exit 0
}

if (-not $AutoClose) {
    $response = Read-Host "Close these windows? (y/N)"
    if ($response -ne 'y' -and $response -ne 'Y') {
        Write-Host "Cancelled." -ForegroundColor Gray
        exit 0
    }
}

foreach ($window in $orphanedWindows) {
    try {
        [WarpCleanup]::SendMessage($window.HWND, [WarpCleanup]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Write-Host "✓ Closed: $($window.Title)" -ForegroundColor Green
    } catch {
        Write-Host "✗ Failed to close: $($window.Title) - $_" -ForegroundColor Red
    }
}

Write-Host "`nCleanup complete." -ForegroundColor Green
