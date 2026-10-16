param(
    [Parameter(Mandatory=$true)][int]$ParentId,
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$Sha256,
    [Parameter(Mandatory=$true)][string]$DataFolder,
    [string]$InstallDirectory = ''
)
$ErrorActionPreference = 'Stop'
$statusPath = Join-Path $DataFolder 'application-update-status.txt'
try {
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
    $arguments = @('/SILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-')
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
    Set-Content -LiteralPath $statusPath -Value 'Update installed. Starting RelayDesk.'
    $appProcess = Start-Process -FilePath $appPath -WorkingDirectory $InstallDirectory -PassThru
    Set-Content -LiteralPath $statusPath -Value ('Update installed. RelayDesk PID ' + $appProcess.Id)
} catch {
    Set-Content -LiteralPath $statusPath -Value $_.Exception.Message
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Не удалось установить обновление RelayDesk: ' + $_.Exception.Message + "`nПрежнюю версию можно открыть через ярлык.",'RelayDesk') | Out-Null
    exit 1
}
