<#
.SYNOPSIS
  Builds the Chuvadi PDF virtual printer: the agent (always) and, with -Psa, the signed Windows 11
  Print Support App package (.msix) with its self-signed certificate (.cer).

.DESCRIPTION
  Output goes to products\ChuvadiPdfPrinter\out:
    out\agent\              ChuvadiPdfAgent.exe and its libraries (framework-dependent, needs .NET 10)
    out\psa\ChuvadiPdf.msix the Print Support App package (with -Psa)
    out\psa\ChuvadiPdf.cer  the public certificate the package is signed with (with -Psa)

  -Psa needs the Windows SDK 10.0.26100 or later (makeappx.exe, makepri.exe, signtool.exe).
  Forwarding to Windows printers uses Chuvadi.Print.Pdf, which needs the Chuvadi.Pdf packages in the local feed
  (C:\Users\aruns\Documents\local-nuget, see nuget.config).
  The self-signed certificate is created once in Cert:\CurrentUser\My (subject = -Publisher) and reused.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build.ps1
  powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Psa
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$Psa,
    [string]$Publisher = "CN=Chuvadi",
    [ValidateSet("x64", "arm64")][string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "out"
$rid = "win-$Arch"

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

Write-Host "== Building agent ($rid)"
$agentOut = Join-Path $out "agent"
if (Test-Path $agentOut) { Remove-Item $agentOut -Recurse -Force }
Invoke-Checked "dotnet" @("publish", (Join-Path $root "src\ChuvadiPdfPrinter.Agent\ChuvadiPdfPrinter.Agent.csproj"),
    "-c", $Configuration, "-r", $rid, "--self-contained", "false", "-o", $agentOut)
Write-Host "Agent: $agentOut"

if (-not $Psa) { return }

Write-Host "== Locating Windows SDK tools"
$kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$sdk = Get-ChildItem $kits -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName "x64\makeappx.exe")) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw "Windows SDK not found under $kits. Install the Windows 11 SDK (10.0.26100 or later)." }
$tools = Join-Path $sdk.FullName "x64"
$makeappx = Join-Path $tools "makeappx.exe"
$makepri = Join-Path $tools "makepri.exe"
$signtool = Join-Path $tools "signtool.exe"
Write-Host "Windows SDK $($sdk.Name)"

Write-Host "== Building Print Support App layout"
$layout = Join-Path $out "psa-layout"
$psaOut = Join-Path $out "psa"
foreach ($d in @($layout, $psaOut)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Path $psaOut | Out-Null
Invoke-Checked "dotnet" @("publish", (Join-Path $root "src\ChuvadiPdfPrinter.Psa\ChuvadiPdfPrinter.Psa.csproj"),
    "-c", $Configuration, "-r", $rid, "--self-contained", "false", "-p:Platform=$Arch", "-o", $layout)
Copy-Item (Join-Path $agentOut "*") $layout -Recurse -Force
foreach ($required in @("WinRT.Host.dll", "ChuvadiPdfPrinter.Psa.dll", "ChuvadiPdfAgent.exe")) {
    if (-not (Test-Path (Join-Path $layout $required))) { throw "Layout is missing $required (CsWinRT component build did not produce it)." }
}

# The package files (manifest, PDC, strings, logos) were copied by the publish into layout\Package.
$packageSrc = Join-Path $layout "Package"
Copy-Item (Join-Path $packageSrc "*") $layout -Recurse -Force
Remove-Item $packageSrc -Recurse -Force
$manifestPath = Join-Path $layout "AppxManifest.xml"
$manifest = [System.IO.File]::ReadAllText($manifestPath)
$manifest = $manifest.Replace('Publisher="CN=Chuvadi"', 'Publisher="' + $Publisher + '"')
$manifest = $manifest.Replace('ProcessorArchitecture="x64"', 'ProcessorArchitecture="' + $Arch + '"')
[System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "== Indexing resources (makepri)"
$priConfig = Join-Path $out "priconfig.xml"
Invoke-Checked $makepri @("createconfig", "/cf", $priConfig, "/dq", "en-US", "/o")
Invoke-Checked $makepri @("new", "/pr", $layout, "/cf", $priConfig, "/mn", $manifestPath, "/of", (Join-Path $layout "resources.pri"), "/o")

Write-Host "== Packing (makeappx)"
$msix = Join-Path $psaOut "ChuvadiPdf.msix"
Invoke-Checked $makeappx @("pack", "/d", $layout, "/p", $msix, "/o")

Write-Host "== Signing (signtool) with $Publisher"
$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $Publisher -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating a self-signed code-signing certificate $Publisher (Cert:\CurrentUser\My, valid 5 years)"
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Publisher -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
}
Invoke-Checked $signtool @("sign", "/fd", "SHA256", "/sha1", $cert.Thumbprint, "/s", "My", $msix)
Export-Certificate -Cert $cert -FilePath (Join-Path $psaOut "ChuvadiPdf.cer") | Out-Null

Write-Host ""
Write-Host "Package:     $msix"
Write-Host "Certificate: $(Join-Path $psaOut 'ChuvadiPdf.cer')  (thumbprint $($cert.Thumbprint))"
