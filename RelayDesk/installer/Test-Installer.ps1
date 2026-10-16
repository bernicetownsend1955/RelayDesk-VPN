param([string]$Setup = "$PSScriptRoot\output\RelayDesk-Setup-QA.exe")
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $env:TEMP ('RelayDesk-installer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$destination = Join-Path $env:ProgramFiles 'RelayDesk Installer QA'
$registryPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RelayDesk-Installer-QA-4F386E9C_is1'
if (Test-Path -LiteralPath $destination) { throw 'QA installation already exists; refusing to replace it.' }
$userData = Join-Path $env:LOCALAPPDATA 'RelayDesk'
$before = @{}
foreach ($file in @('profiles.json','settings.json')) {
    $path = Join-Path $userData $file
    if (Test-Path -LiteralPath $path) { $before[$file] = (Get-FileHash -LiteralPath $path).Hash }
}
$setupProcess = Start-Process -FilePath (Resolve-Path -LiteralPath $Setup).Path -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=',('/DIR="'+$destination+'"'),('/LOG="'+(Join-Path $fixture 'install.log')+'"')) -Verb RunAs -WindowStyle Hidden -PassThru
$setupProcess.WaitForExit()
if ($setupProcess.ExitCode) { throw ('Installation failed: ' + $setupProcess.ExitCode) }
try {
    if (!(Test-Path -LiteralPath $registryPath)) { throw 'Uninstall registration missing' }
    [xml]$definition = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'RelayDesk.csproj')
    $build = Join-Path (Split-Path $PSScriptRoot -Parent) ('release\'+$definition.Project.PropertyGroup.Version)
    foreach ($file in @('RelayDesk.exe','RelayDesk.dll','RelayDesk.runtimeconfig.json','coreclr.dll')) {
        if ((Get-FileHash -LiteralPath (Join-Path $destination $file)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $build $file)).Hash) { throw ('Installed file differs: '+$file) }
    }
    foreach ($file in @('profiles.json','settings.json','support-token.dpapi','sing-box.exe','winws.exe')) {
        if (Test-Path -LiteralPath (Join-Path $destination $file)) { throw ('Unexpected bundled user data or core: '+$file) }
    }
    Write-Output 'PASS Program Files install, runtime, hashes and uninstall registration'
    $uiCheck = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'RelayDesk.Checks\UiSmoke.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $uiCheck -Executable (Join-Path $destination 'RelayDesk.exe') -CheckRouting -CheckTun
    if ($LASTEXITCODE) { throw 'Installed application UI checks failed' }
} finally {
    $uninstaller = Join-Path $destination 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $removeProcess = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $fixture 'uninstall.log')+'"')) -Verb RunAs -WindowStyle Hidden -PassThru
        $removeProcess.WaitForExit()
        if ($removeProcess.ExitCode) { throw ('Uninstall failed: '+$removeProcess.ExitCode) }
    }
}
if (Test-Path -LiteralPath $registryPath) { throw 'Uninstall entry remained' }
if (Test-Path -LiteralPath (Join-Path $destination 'RelayDesk.exe')) { throw 'Executable remained after uninstall' }
foreach ($file in $before.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $userData $file)).Hash -ne $before[$file]) { throw ('Production user data changed: '+$file) }
}
Write-Output 'PASS uninstall and existing profile preservation'
Write-Output ('Installer logs: '+$fixture)
