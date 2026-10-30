<#
.SYNOPSIS
  Removes the Chuvadi PDF virtual printer (run as Administrator). Saved PDFs are never deleted.

.DESCRIPTION
  Removes the compatibility queue(s) and pipe port, the agent service and program files, and the
  Print Support App package. Configuration, audit trail and logs in %ProgramData%\Chuvadi\PdfPrinter are
  kept unless -RemoveData is given (the audit trail may be needed for NABH / AERB records).
  -RemoveCertificate also removes the package certificate from LocalMachine\TrustedPeople.
#>
[CmdletBinding()]
param(
    [string]$QueueName = "Chuvadi PDF",
    [string]$PipeName = "ChuvadiPdfPrinter",
    [switch]$RemoveData,
    [switch]$RemoveCertificate,
    [string]$Publisher = "CN=Chuvadi"
)

$ErrorActionPreference = "Stop"
$serviceName = "ChuvadiPdfAgent"
$installDir = Join-Path $env:ProgramFiles "Chuvadi\PdfPrinter"
$dataDir = Join-Path $env:ProgramData "Chuvadi\PdfPrinter"

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell (Run as Administrator)."
}

$portName = "\\.\pipe\" + $PipeName
foreach ($name in @($QueueName, "$QueueName (Compatibility)")) {
    if (Get-Printer -Name $name -ErrorAction SilentlyContinue) {
        $printer = Get-Printer -Name $name
        if ($printer.PortName -eq $portName) { Remove-Printer -Name $name; Write-Host "Removed printer '$name'" }
        else { Write-Host "Kept printer '$name' (it is not on the Chuvadi port)" }
    }
}
if (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue) {
    Remove-PrinterPort -Name $portName
    Write-Host "Removed port $portName"
}

$package = Get-AppxPackage -AllUsers -Name "Chuvadi.PdfPrinter" -ErrorAction SilentlyContinue
foreach ($p in @($package)) {
    if ($p) { Remove-AppxPackage -Package $p.PackageFullName -AllUsers; Write-Host "Removed package $($p.PackageFullName)" }
}
Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -eq "Chuvadi.PdfPrinter" } |
    ForEach-Object { Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName | Out-Null; Write-Host "Removed provisioning $($_.PackageName)" }

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") { Stop-Service -Name $serviceName -Force }
    & sc.exe delete $serviceName | Out-Null
    Write-Host "Removed service $serviceName"
}
if (Test-Path $installDir) {
    Start-Sleep -Seconds 2
    Remove-Item $installDir -Recurse -Force
    Write-Host "Removed $installDir"
}

if ($RemoveData -and (Test-Path $dataDir)) {
    Remove-Item $dataDir -Recurse -Force
    Write-Host "Removed $dataDir"
} elseif (Test-Path $dataDir) {
    Write-Host "Kept $dataDir (config, audit trail, logs). Use -RemoveData to delete it."
}

if ($RemoveCertificate) {
    Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq $Publisher } |
        ForEach-Object { Remove-Item $_.PSPath; Write-Host "Removed certificate $($_.Thumbprint)" }
}
Write-Host "Uninstalled. Saved PDFs were not touched."
