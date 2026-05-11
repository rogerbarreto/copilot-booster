# Validation Script for Warp E2E Tests
# Run this AFTER closing all Warp instances to verify tests work correctly

$ErrorActionPreference = "Stop"

Write-Host "=== WARP E2E TEST VALIDATION ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check no Warp is running
Write-Host "[1/5] Checking for existing Warp processes..." -ForegroundColor Yellow
$existingWarp = Get-Process warp -ErrorAction SilentlyContinue
if ($existingWarp) {
    Write-Host "  ✗ FAIL: Warp is still running. Please close all Warp instances first." -ForegroundColor Red
    Write-Host "  Current PIDs: $($existingWarp.Id -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ No Warp processes found" -ForegroundColor Green
Write-Host ""

# Step 2: Run tests
Write-Host "[2/5] Running Warp E2E tests..." -ForegroundColor Yellow
$env:COPILOT_BOOSTER_RUN_LOCALONLY = "1"
$testOutput = dotnet run --project tests\CopilotBooster.IntegrationTests.csproj -c Release -- -class CopilotBooster.IntegrationTests.Integration.WarpMultiTabE2ETests 2>&1 | Out-String

if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ FAIL: Tests failed" -ForegroundColor Red
    Write-Host $testOutput
    exit 1
}

$testOutput | Select-String -Pattern "Total:|Failed:|Time:" | ForEach-Object { Write-Host "  $_" }

if ($testOutput -match "Failed: 0") {
    Write-Host "  ✓ All tests passed" -ForegroundColor Green
} else {
    Write-Host "  ✗ FAIL: Some tests failed" -ForegroundColor Red
    Write-Host $testOutput
    exit 1
}
Write-Host ""

# Step 3: Check no Warp leaked
Write-Host "[3/5] Checking for leaked Warp processes..." -ForegroundColor Yellow
Start-Sleep -Seconds 2
$leakedWarp = Get-Process warp -ErrorAction SilentlyContinue
if ($leakedWarp) {
    Write-Host "  ⚠ WARNING: Warp process still running after test" -ForegroundColor Yellow
    Write-Host "  PIDs: $($leakedWarp.Id -join ', ')" -ForegroundColor Yellow
    Write-Host "  This may be normal if tests are still cleaning up..." -ForegroundColor Gray
    
    # Give cleanup more time
    Write-Host "  Waiting 5 more seconds for cleanup..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    $leakedWarp = Get-Process warp -ErrorAction SilentlyContinue
    if ($leakedWarp) {
        Write-Host "  ✗ FAIL: Warp leaked (PID: $($leakedWarp.Id -join ', '))" -ForegroundColor Red
        Write-Host "  Run cleanup: .\tests\Integration\CleanupWarpTestWindows.ps1" -ForegroundColor Yellow
    } else {
        Write-Host "  ✓ Cleanup successful (delayed)" -ForegroundColor Green
    }
} else {
    Write-Host "  ✓ No Warp processes leaked" -ForegroundColor Green
}
Write-Host ""

# Step 4: Check for orphaned windows
Write-Host "[4/5] Checking for orphaned test windows..." -ForegroundColor Yellow
& ".\tests\Integration\CleanupWarpTestWindows.ps1" -DryRun
Write-Host ""

# Step 5: Summary
Write-Host "[5/5] Validation Summary" -ForegroundColor Cyan
Write-Host "  ✓ Pre-flight check works (no warp.exe = tests run)"
Write-Host "  ✓ Tests execute and pass"
if (-not $leakedWarp) {
    Write-Host "  ✓ Cleanup works (no leaked processes)"
}
Write-Host ""
Write-Host "=== VALIDATION COMPLETE ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next: Test with Warp RUNNING to verify skip behavior:" -ForegroundColor Yellow
Write-Host "  1. Start Warp"
Write-Host "  2. Run this script again"
Write-Host "  3. Verify tests skip instantly (no 45s runtime)"
