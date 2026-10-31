<#
.SYNOPSIS
  Runs the agent's self-test against every installed Chuvadi PDF printer that can be tested unattended:
  prints a test page, waits for the PDF, checks it and the audit trail. Run as the user who prints
  (not elevated is fine). Exit code 0 = all passed.
#>
[CmdletBinding()]
param([int]$TimeoutSeconds = 120)

$agent = Join-Path $env:ProgramFiles "Chuvadi\PdfPrinter\ChuvadiPdfAgent.exe"
if (-not (Test-Path $agent)) { Write-Host "Agent not installed: $agent"; exit 1 }

$candidates = @("Chuvadi PDF (Compatibility)", "Chuvadi PDF (Auto-save)")
$queue = Get-Printer -Name "Chuvadi PDF" -ErrorAction SilentlyContinue
if ($queue -and $queue.PortName -like "\\.\pipe\*") { $candidates = @("Chuvadi PDF") + $candidates }

$failed = 0
$tested = 0
foreach ($name in $candidates) {
    if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) { continue }
    Write-Host ""
    Write-Host "===== $name"
    & $agent --self-test --printer $name --timeout $TimeoutSeconds
    if ($LASTEXITCODE -ne 0) { $failed++ }
    $tested++
}
if ($tested -eq 0) { Write-Host "No Chuvadi PDF printer that can be tested unattended is installed."; exit 1 }
Write-Host ""
if ($failed -eq 0) { Write-Host "ALL SELF-TESTS PASSED ($tested printer(s))"; exit 0 }
Write-Host "$failed of $tested printer self-test(s) FAILED"
exit 1
