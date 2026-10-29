param([string]$Compiler = "$PSScriptRoot\tools\InnoSetup\ISCC.exe", [switch]$TestBuild, [string]$Repository = '')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
[xml]$definition = Get-Content -LiteralPath (Join-Path $project 'RelayDesk.csproj')
$version = $definition.Project.PropertyGroup.Version
& (Join-Path $PSScriptRoot 'Test-TextEncoding.ps1') -ProjectDir $project
$publish = Join-Path $project ('release\' + $version)
if (!(Test-Path -LiteralPath $Compiler)) { throw 'Install Inno Setup 6 and pass -Compiler with the path to ISCC.exe.' }
if ($Repository -and $Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$') { throw 'Repository must be owner/repository.' }
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish "-p:GitHubRepository=$Repository"
if ($LASTEXITCODE) { throw 'Publish failed' }
& (Join-Path $PSScriptRoot 'Prepare-Cores.ps1') -PublishDir $publish
$arguments = @("/DAppVersion=$version", "/DPublishDir=$publish")
if ($TestBuild) { $arguments += '/DTestBuild=1' }
& $Compiler @arguments (Join-Path $PSScriptRoot 'RelayDesk.iss')
if ($LASTEXITCODE) { throw 'Installer compilation failed' }
if (!$TestBuild) {
    $releaseRoot = Join-Path $project 'release'
    Set-Content -LiteralPath (Join-Path $releaseRoot 'current-pending-version.txt') -Value $version
    $syncScript = Join-Path $releaseRoot 'Switch-Current.ps1'
    if ((Test-Path -LiteralPath $syncScript) -and (Test-Path -LiteralPath (Join-Path $releaseRoot 'current'))) {
        $marker = '-File "' + $syncScript + '"'
        $existing = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($marker) })
        if (!$existing.Count) {
            $running = @(Get-Process RelayDesk -ErrorAction SilentlyContinue)
            $waitId = if ($running.Count) { $running[0].Id } else { 0 }
            Start-Process powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$syncScript+'"'),'-WaitForPid',$waitId) -WindowStyle Hidden | Out-Null
        }
    }
}
$setupName = if ($TestBuild) { 'RelayDesk-Setup-QA.exe' } else { 'RelayDesk-Setup-'+$version+'-x64.exe' }
Get-FileHash -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'output') $setupName) -Algorithm SHA256
