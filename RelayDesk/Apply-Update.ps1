param(
    [Parameter(Mandatory=$true)][int]$ParentId,
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$Sha256,
    [Parameter(Mandatory=$true)][string]$DataFolder,
    [string]$InstallDirectory = '',
    [string]$ResumeToken = ''
)
$ErrorActionPreference = 'Stop'
$statusPath = Join-Path $DataFolder 'application-update-status.txt'
$applyLock = $null
try {
    $lockPath = Join-Path $DataFolder 'application-update.lock'
    try { $applyLock = [IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
    catch [IO.IOException] { exit 0 }
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    Set-Content -LiteralPath $statusPath -Value 'Waiting for RelayDesk to close.'
    while (Get-Process -Id $ParentId -ErrorAction SilentlyContinue) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'RelayDesk did not close. Update cancelled.' }
        Start-Sleep -Milliseconds 400
    }
    $resolvedInstaller = [IO.Path]::GetFullPath($Installer)
    $downloadRoot = [IO.Path]::GetFullPath((Join-Path $DataFolder 'application-updates')).TrimEnd('\') + '\'
    if (!$resolvedInstaller.StartsWith($downloadRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedInstaller) -notmatch '^RelayDesk-Setup-\d+\.\d+\.\d+-x64\.exe$') { throw 'Unexpected installer path.' }
    if ($Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash -ne $Sha256) { throw 'Installer SHA-256 changed. Update cancelled.' }
    $arguments = @('/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOLAUNCH=1')
    if($ResumeToken){if($ResumeToken -notmatch '^[a-fA-F0-9]{32}$'){throw 'Invalid restart token'};$arguments+='/RESUMEUPDATE='+$ResumeToken}
    if ($InstallDirectory) {
        if ($InstallDirectory.Contains('"')) { throw 'Invalid install directory.' }
        $arguments += '/DIR="' + $InstallDirectory.TrimEnd('\') + '"'
    }
    Set-Content -LiteralPath $statusPath -Value 'Installing verified RelayDesk update.'
    $setupProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
    $setupProcess.WaitForExit()
    if ($setupProcess.ExitCode -ne 0) { throw ('Installer exited with code ' + $setupProcess.ExitCode) }
    if (!$InstallDirectory) {
        $registration = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RelayDesk-Desktop-A7B02E29-4D16-47F0-B8F3-3B829E1F024D_is1'
        $InstallDirectory = $registration.InstallLocation
    }
    $appPath = Join-Path $InstallDirectory 'RelayDesk.exe'
    if (!(Test-Path -LiteralPath $appPath)) { throw 'Installed application not found.' }
    $pendingPath = Join-Path $DataFolder 'pending-app-update.json'
    if (Test-Path -LiteralPath $pendingPath) { Remove-Item -LiteralPath $pendingPath -Force }
    Set-Content -LiteralPath $statusPath -Value 'Update installed. Starting RelayDesk.'
    $applyLock.Dispose(); $applyLock = $null
    $launchArguments=@()
    if($ResumeToken){$launchArguments=@('--resume-update',$ResumeToken)}
    if($env:RELAYDESK_DATA_DIR){$appStart=New-Object Diagnostics.ProcessStartInfo;$appStart.UseShellExecute=$false;$appStart.EnvironmentVariables['RELAYDESK_DATA_DIR']=$env:RELAYDESK_DATA_DIR}else{$appStart=New-Object Diagnostics.ProcessStartInfo;$appStart.UseShellExecute=$true}
    $appStart.FileName=$appPath;$appStart.WorkingDirectory=$InstallDirectory
    if($launchArguments.Count){$appStart.Arguments=$launchArguments -join ' '}
    $appProcess=[Diagnostics.Process]::Start($appStart)
    Set-Content -LiteralPath $statusPath -Value ('Update installed. RelayDesk PID ' + $appProcess.Id)
} catch {
    Set-Content -LiteralPath $statusPath -Value $_.Exception.Message
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Не удалось установить обновление RelayDesk: ' + $_.Exception.Message + "`nПрежнюю версию можно открыть через ярлык.",'RelayDesk') | Out-Null
    exit 1
}
finally { if ($applyLock) { $applyLock.Dispose() } }
