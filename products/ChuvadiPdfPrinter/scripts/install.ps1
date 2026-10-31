<#
.SYNOPSIS
  Installs the Chuvadi PDF virtual printer (run as Administrator).

.DESCRIPTION
  -Mode Auto   (default) Windows 11 24H2+ with a built package: the Print Support App printers;
               otherwise the compatibility queue.
  -Mode Queue  "Chuvadi PDF" queue on the inbox "Microsoft Print To PDF" driver whose port is the agent's
               named pipe (works on Windows 10 and 11). Every job is its own pipe connection, so jobs never
               share an output file.
  -Mode Psa    Windows 11 24H2+: "Chuvadi PDF" (Save As) and "Chuvadi PDF (Auto-save)" in every print dialog.
  -Mode Both   both; the queue is then named "<QueueName> (Compatibility)" to avoid a name clash.

  The agent service (ChuvadiPdfAgent, LocalSystem) is installed in every mode: it saves the queue's and the
  Auto-save printer's PDFs, writes the audit trail and optionally sends each PDF to physical printers.

  Files:   %ProgramFiles%\Chuvadi\PdfPrinter        (agent)
           %ProgramData%\Chuvadi\PdfPrinter         (config.json, audit.log, agent.log)
           <OutputFolder>\<user>\                    (PDFs; each user's folder is private to that user)

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1
  powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Mode Queue -OutputFolder "D:\Scans\Chuvadi PDF"
#>
[CmdletBinding()]
param(
    [ValidateSet("Auto", "Queue", "Psa", "Both")][string]$Mode = "Auto",
    [string]$SourceFolder = "",
    [string]$QueueName = "Chuvadi PDF",
    [string]$PipeName = "ChuvadiPdfPrinter",
    [string]$OutputFolder = "%PUBLIC%\Documents\Chuvadi PDF"
)

$ErrorActionPreference = "Stop"
# Windows PowerShell 5.1 leaves $PSScriptRoot empty while it evaluates parameter defaults, so the default
# source folder (products\ChuvadiPdfPrinter\out, next to this scripts folder) is worked out here instead.
if ([string]::IsNullOrEmpty($SourceFolder)) { $SourceFolder = Join-Path (Split-Path -Parent $PSScriptRoot) "out" }
$serviceName = "ChuvadiPdfAgent"
$installDir = Join-Path $env:ProgramFiles "Chuvadi\PdfPrinter"
$dataDir = Join-Path $env:ProgramData "Chuvadi\PdfPrinter"
$configPath = Join-Path $dataDir "config.json"
$pdfDriver = "Microsoft Print To PDF"

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script from an elevated PowerShell (Run as Administrator)."
    }
}

function Write-Utf8NoBom([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-JsonString($value) {
    # Untyped on purpose: a [string] parameter would turn $null into "".
    if ($null -eq $value) { return "null" }
    return '"' + ([string]$value).Replace('\', '\\').Replace('"', '\"') + '"'
}

Assert-Admin
$build = [Environment]::OSVersion.Version.Build
$agentSource = Join-Path $SourceFolder "agent"
$msix = Join-Path $SourceFolder "psa\ChuvadiPdf.msix"
$cer = Join-Path $SourceFolder "psa\ChuvadiPdf.cer"

if ($Mode -eq "Auto") {
    if ($build -ge 26100 -and (Test-Path $msix)) { $Mode = "Psa" } else { $Mode = "Queue" }
}
if (($Mode -eq "Psa" -or $Mode -eq "Both") -and $build -lt 26100) {
    throw "The Print Support App needs Windows 11 24H2 (build 26100) or later; this is build $build. Use -Mode Queue."
}
if (($Mode -eq "Psa" -or $Mode -eq "Both") -and -not (Test-Path $msix)) {
    throw "Package not found: $msix. Build it with scripts\build.ps1 -Psa, or use -Mode Queue."
}
if (-not (Test-Path (Join-Path $agentSource "ChuvadiPdfAgent.exe"))) {
    throw "Agent not found in $agentSource. Build it first: scripts\build.ps1"
}
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$runtimes = @()
if ($dotnet) { $runtimes = @(& $dotnet.Source --list-runtimes 2>$null) }
if (-not ($runtimes -match '^Microsoft\.NETCore\.App 10\.')) {
    throw ".NET 10 runtime not found. Install the .NET 10 Desktop or .NET Runtime (x64) from https://dotnet.microsoft.com/download/dotnet/10.0"
}
Write-Host "Installing Chuvadi PDF: mode $Mode on Windows build $build"

# 1. Agent files (stop the service first if it is running)
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne "Stopped") { Stop-Service -Name $serviceName -Force; Start-Sleep -Seconds 2 }
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item (Join-Path $agentSource "*") $installDir -Recurse -Force
$agentExe = Join-Path $installDir "ChuvadiPdfAgent.exe"

# 2. Windows 11 Print Support App
$familyName = $null
if ($Mode -eq "Psa" -or $Mode -eq "Both") {
    Write-Host "Trusting the package certificate (LocalMachine\TrustedPeople)"
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host "Installing the package for all users and the current user"
    Add-AppxProvisionedPackage -Online -PackagePath $msix -SkipLicense | Out-Null
    Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
    $package = Get-AppxPackage -Name "Chuvadi.PdfPrinter" | Select-Object -First 1
    if (-not $package) { throw "The package did not install (Get-AppxPackage Chuvadi.PdfPrinter is empty)." }
    $familyName = $package.PackageFamilyName
}

# 3. Configuration (kept if it already exists; installer-managed values updated)
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null
$queueForConfig = $QueueName
if ($Mode -eq "Both") { $queueForConfig = "$QueueName (Compatibility)" }
if (-not (Test-Path $configPath)) {
    $json = "{`r`n" +
        "  ""QueueName"": " + (Get-JsonString $queueForConfig) + ",`r`n" +
        "  ""PipeName"": " + (Get-JsonString $PipeName) + ",`r`n" +
        "  ""OutputFolder"": " + (Get-JsonString $OutputFolder) + ",`r`n" +
        "  ""PerUserSubfolder"": true,`r`n" +
        "  ""FileNamePattern"": ""{date:yyyy-MM-dd HHmmss} {title}"",`r`n" +
        "  ""Collision"": ""AppendNumber"",`r`n" +
        "  ""PsaPackageFamilyName"": " + (Get-JsonString $familyName) + ",`r`n" +
        "  ""ForwardTo"": []`r`n" +
        "}`r`n"
    Write-Utf8NoBom $configPath $json
    Write-Host "Wrote $configPath"
} else {
    $config = [System.IO.File]::ReadAllText($configPath) | ConvertFrom-Json
    $config.QueueName = $queueForConfig
    $config.PipeName = $PipeName
    if ($familyName) { $config | Add-Member -NotePropertyName PsaPackageFamilyName -NotePropertyValue $familyName -Force }
    Write-Utf8NoBom $configPath ($config | ConvertTo-Json -Depth 6)
    Write-Host "Updated $configPath (your other settings were kept)"
}
& $agentExe --validate --config $configPath
if ($LASTEXITCODE -ne 0) { throw "Configuration is invalid; fix $configPath and run the installer again." }

# 4. Agent service
if (-not $existing) {
    New-Service -Name $serviceName -BinaryPathName ('"' + $agentExe + '" --service') -DisplayName "Chuvadi PDF Agent" `
        -Description "Saves jobs printed to the Chuvadi PDF virtual printer as PDF files." -StartupType Automatic | Out-Null
    & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}
Start-Service -Name $serviceName
Start-Sleep -Seconds 2
if (-not (Test-Path ("\\.\pipe\" + $PipeName))) { throw "The agent did not start; see $(Join-Path $dataDir 'agent.log') and the System event log." }
Write-Host "Agent service running"

# 5. Compatibility queue on Microsoft Print To PDF with the pipe as its port
if ($Mode -eq "Queue" -or $Mode -eq "Both") {
    if (-not (Get-PrinterDriver -Name $pdfDriver -ErrorAction SilentlyContinue)) {
        Write-Host "Enabling Microsoft Print to PDF"
        Enable-WindowsOptionalFeature -Online -FeatureName "Printing-PrintToPDFServices-Features" -NoRestart | Out-Null
    }
    $portName = "\\.\pipe\" + $PipeName
    if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $portName }
    if (Get-Printer -Name $queueForConfig -ErrorAction SilentlyContinue) {
        Set-Printer -Name $queueForConfig -DriverName $pdfDriver -PortName $portName
    } else {
        Add-Printer -Name $queueForConfig -DriverName $pdfDriver -PortName $portName
    }
    Write-Host "Printer '$queueForConfig' -> $portName"
}

Write-Host ""
Write-Host "Installed. Check it with:"
if ($Mode -eq "Queue" -or $Mode -eq "Both") {
    Write-Host ('  & "' + $agentExe + '" --self-test --printer "' + $queueForConfig + '"')
}
if ($Mode -eq "Psa" -or $Mode -eq "Both") {
    Write-Host ('  & "' + $agentExe + '" --self-test --printer "Chuvadi PDF (Auto-save)"')
    Write-Host '  and print anything to "Chuvadi PDF" to see the Save As prompt.'
}
