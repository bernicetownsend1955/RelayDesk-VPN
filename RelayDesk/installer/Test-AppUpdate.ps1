param([string]$Report = '')
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (!(New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $reportPath = Join-Path $env:TEMP ('RelayDesk-handoff-test-' + [guid]::NewGuid().ToString('N') + '.log')
    $test = Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-Report',('"'+$reportPath+'"')) -Verb RunAs -WindowStyle Hidden -PassThru
    $test.WaitForExit()
    if (Test-Path -LiteralPath $reportPath) { Get-Content -LiteralPath $reportPath }
    exit $test.ExitCode
}
if ($Report) { Start-Transcript -LiteralPath $Report | Out-Null }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HandoffWindow {
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
'@
$project = Split-Path $PSScriptRoot -Parent
[xml]$definition = Get-Content -LiteralPath (Join-Path $project 'RelayDesk.csproj')
$version = [string]$definition.Project.PropertyGroup.Version
$destination = Join-Path $env:ProgramFiles 'RelayDesk Installer QA'
if (Test-Path -LiteralPath $destination) { throw 'Existing QA installation must be removed first.' }
$fixture = Join-Path $env:TEMP ('RelayDesk-app-handoff-' + [guid]::NewGuid().ToString('N'))
$downloads = Join-Path $fixture ('application-updates\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
$installer = Join-Path $downloads ('RelayDesk-Setup-' + $version + '-x64.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'output\RelayDesk-Setup-QA.exe') -Destination $installer
$sha = (Get-FileHash -LiteralPath $installer).Hash
$parent = Start-Process powershell.exe -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 8') -WindowStyle Hidden -Verb RunAs -PassThru
$originalDataFolder = $env:RELAYDESK_DATA_DIR
$launched = $null
try {
    $env:RELAYDESK_DATA_DIR = $fixture
    $helperPath = Join-Path $project ('release\' + $version + '\Apply-Update.ps1')
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$helperPath+'"'),'-ParentId',$parent.Id,'-Installer',('"'+$installer+'"'),'-Sha256',$sha,'-DataFolder',('"'+$fixture+'"'),'-InstallDirectory',('"'+$destination+'"'))
    $helper = Start-Process powershell.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $env:RELAYDESK_DATA_DIR = $originalDataFolder
    Start-Sleep -Seconds 2
    if ((Get-Content -LiteralPath (Join-Path $fixture 'application-update-status.txt') -Raw) -notlike '*Waiting*' -or (Test-Path -LiteralPath $destination)) { throw 'Update did not wait for elevated parent.' }
    Write-Output 'PASS update helper waits for an elevated parent without interrupting other applications'
    if (!$helper.WaitForExit(90000)) { throw 'Update helper did not complete within 90 seconds.' }
    if ($helper.ExitCode) { throw 'Update helper failed.' }
    $status = Get-Content -LiteralPath (Join-Path $fixture 'application-update-status.txt') -Raw
    if ($status -notmatch 'RelayDesk PID (\d+)') { throw ('Application restart missing: '+$status) }
    $launched = Get-Process -Id ([int]$matches[1])
    if ((Get-Item -LiteralPath (Join-Path $destination 'RelayDesk.dll')).VersionInfo.ProductVersion -notlike ($version+'*')) { throw 'Installed version differs.' }
    Write-Output 'PASS verified installer applied and updated application restarted'
} finally {
    $env:RELAYDESK_DATA_DIR = $originalDataFolder
    if ($launched) {
        for ($attempt=0;$attempt -lt 30 -and !$launched.MainWindowHandle;$attempt++) { Start-Sleep -Milliseconds 200; $launched.Refresh() }
        if (!$launched.HasExited) {
            Start-Sleep -Seconds 2; $launched.Refresh()
            $null=[HandoffWindow]::PostMessage($launched.MainWindowHandle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
            if (!$launched.WaitForExit(10000)) {
                if ($launched.MainModule.FileName -ne (Join-Path $destination 'RelayDesk.exe')) { throw 'Unexpected process path; refusing QA cleanup.' }
                $launched.Kill(); $launched.WaitForExit()
                Write-Output 'QA window required forced cleanup after verified update.'
            }
        }
    }
    $uninstaller = Join-Path $destination 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $remove = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Verb RunAs -PassThru
        $remove.WaitForExit()
        if ($remove.ExitCode) { throw 'QA uninstall failed.' }
    }
}
Write-Output ('Handoff fixture: ' + $fixture)
if ($Report) { Stop-Transcript | Out-Null }
