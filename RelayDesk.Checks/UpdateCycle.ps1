param([Parameter(Mandatory=$true)][string]$SourceDirectory,[Parameter(Mandatory=$true)][string]$Setup,[string]$BaselineDirectory='', [switch]$CheckPresets,[switch]$CheckOverlay)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
if(!$BaselineDirectory){$BaselineDirectory=$SourceDirectory}
$root=Join-Path $env:TEMP ('RelayDesk-update-cycle-'+[guid]::NewGuid().ToString('N'))
$data=Join-Path $root 'data';$installed=Join-Path $root 'installed'
New-Item -ItemType Directory -Path $data,$installed -Force | Out-Null
'QA root: '+$root
$production=Join-Path $env:LOCALAPPDATA 'RelayDesk'
$hashes=@{}
foreach($name in @('profiles.json','settings.json')){if(Test-Path -LiteralPath (Join-Path $production $name)){$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $production $name)).Hash}}
Copy-Item -Path (Join-Path $BaselineDirectory '*') -Destination $installed -Recurse -Force
$manifest=Get-Content -LiteralPath (Join-Path $SourceDirectory 'bundled-cores\manifest.json') -Raw | ConvertFrom-Json
$core=$manifest | Where-Object Id -eq 'sing-box'
$corePath=Join-Path (Join-Path $SourceDirectory 'bundled-cores') $core.Executable
$listener=New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$profileId=[guid]::NewGuid().ToString('N')
$config=@{inbounds=@(@{type='mixed';listen='127.0.0.1';listen_port=$port});outbounds=@(@{type='direct';tag='direct'})}|ConvertTo-Json -Depth 8
$profiles=@(@{Id=$profileId;Name='Update cycle fixture';Core='sing-box';Config=$config;Managed=$false;Tun=$false;SystemProxy=$false;ProxyUri=('http://127.0.0.1:'+$port)})
[IO.File]::WriteAllText((Join-Path $data 'profiles.json'),(ConvertTo-Json -InputObject $profiles -Depth 10))
$settings=@{ModernUiVersion=1;SharedQuickRoutes=@();LastConnectedProfileId=$profileId;CheckComponentUpdates=$false;QuietMode=$false;TrayEnabled=$false;InterfaceMotion=$false;AsciiBackground=$false;Engines=@{'sing-box'=$corePath}}
if($CheckOverlay){$settings.TrafficOverlay=$true}
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),(ConvertTo-Json -InputObject $settings -Depth 8))
$owned=@()
function Launch([string]$token=''){
 $info=New-Object Diagnostics.ProcessStartInfo;$info.FileName=Join-Path $installed 'RelayDesk.exe';$info.WorkingDirectory=$installed;$info.UseShellExecute=$false;$info.EnvironmentVariables['RELAYDESK_DATA_DIR']=$data
 if($token){$info.Arguments='--resume-update '+$token}
 return [Diagnostics.Process]::Start($info)
}
function Wait-For([scriptblock]$condition,[string]$message){
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 while(!(& $condition)){if([DateTime]::UtcNow -gt $deadline){throw $message};Start-Sleep -Milliseconds 100}
}
function Listening {return @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()|Where-Object Port -eq $port).Count -gt 0}
try {
 $first=Launch;$owned+=,$first
 Wait-For {$first.Refresh();$first.MainWindowHandle -ne [IntPtr]::Zero} 'QA window not created'
 if($CheckOverlay){Wait-For {@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object {$_.Name -eq 'RelayDesk.exe' -and $_.CommandLine -match '--overlay-host'}).Count -eq 1} 'Overlay worker not started';'PASS independent overlay worker started without another VPN instance'}
 $window=[System.Windows.Automation.AutomationElement]::FromHandle($first.MainWindowHandle)
 $name=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'VPN')
 $invoke=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsInvokePatternAvailableProperty,$true)
 $button=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition($name,$invoke)))
 if(!$button){throw 'VPN action missing'}
 $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Wait-For {Listening} 'Loopback core failed to start'
 'PASS isolated VPN core started without TUN or system proxy'
 if($CheckPresets){
  foreach($presetLabel in @('Всё напрямую','По правилам')){
   $previousCore=@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object Name -eq 'sing-box.exe' | Select-Object -ExpandProperty ProcessId)
   $presetName=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$presetLabel)
   $presetButton=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition($presetName,$invoke)))
   if(!$presetButton){throw 'Preset action missing'}
   Wait-For {$presetButton.Current.IsEnabled} 'Preset controls remained busy before selection'
   $presetButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
   Start-Sleep -Milliseconds 600
   Write-Host ('Preset QA: app='+$first.Id+'; port='+$port+'; before='+($previousCore -join ',')+'; after='+(@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object Name -eq 'sing-box.exe' | Select-Object -ExpandProperty ProcessId) -join ',')+'; listening='+(Listening))
   Wait-For {$now=@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object Name -eq 'sing-box.exe' | Select-Object -ExpandProperty ProcessId);(Listening) -and (@($now | Where-Object {$previousCore -notcontains $_}).Count -gt 0)} 'Preset did not reconnect owned core'
   Wait-For {$presetButton.Current.IsEnabled} 'Preset controls remained busy after reconnect'
  }
  'PASS presets reconnect active loopback core and returning to rules retains the connection'
  $appsName=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'⊞    Приложения')
  $apps=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition($appsName,$invoke)))
  $apps.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 400
  $fallbackName=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Маршрут: Остальные подключения')
  $fallback=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$fallbackName)
  $fallback.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Start-Sleep -Milliseconds 200
  $directName=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Напрямую')
  $direct=$fallback.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$directName)
  $direct.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Wait-For {(Get-Content -LiteralPath (Join-Path $data 'settings.json') -Raw | ConvertFrom-Json).FallbackRoute -eq 'direct'} 'Fallback choice did not persist'
  $previousCore=@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object Name -eq 'sing-box.exe' | Select-Object -ExpandProperty ProcessId)
  $applyName=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Применить правила')
  $apply=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition($applyName,$invoke)))
  $apply.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  Wait-For {$now=@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object Name -eq 'sing-box.exe' | Select-Object -ExpandProperty ProcessId);(Listening) -and (@($now | Where-Object {$previousCore -notcontains $_}).Count -gt 0)} 'Applying fallback did not reconnect owned core'
  'PASS editing fallback through modern selector and applying reconnects active loopback core'
 }
 [IO.File]::WriteAllText((Join-Path $data 'pending-app-update.json'),'{"fixture":"pending update must not start during manual installation"}')
 $args=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOLAUNCH=1',('/DIR="'+$installed+'"'),('/QAPID='+$first.Id),('/QADATA="'+$data+'"'),('/LOG="'+(Join-Path $root 'install.log')+'"'))
 $setupProcess=Start-Process -FilePath (Resolve-Path -LiteralPath $Setup).Path -ArgumentList $args -PassThru
 Wait-For {$setupProcess.Refresh();$setupProcess.HasExited} 'QA installation did not finish'
 if($setupProcess.ExitCode -ne 0){throw ('QA installer failed '+$setupProcess.ExitCode)}
 $first.Refresh();if(!$first.HasExited){throw 'Installer replaced files without closing QA app'}
 if($CheckOverlay -and @(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$first.Id) | Where-Object {$_.Name -eq 'RelayDesk.exe' -and $_.CommandLine -match '--overlay-host'}).Count){throw 'Overlay worker remained after installation handoff'}
 if(Listening){throw 'Old owned core remained after installer shutdown'}
 if(!(Test-Path -LiteralPath (Join-Path $data 'pending-app-update.json'))){throw 'Queued update metadata was lost'}
 $state=Get-Content -LiteralPath (Join-Path $data 'update-resume.json') -Raw | ConvertFrom-Json
 if(!$state.Vpn -or $state.Zapret -or $state.ProfileId -ne $profileId){throw 'Installer lost active component state'}
 'PASS installer closed app and core, captured selected VPN, and replaced locked files'
 if((Get-FileHash (Join-Path $installed 'RelayDesk.dll')).Hash -ne (Get-FileHash (Join-Path $SourceDirectory 'RelayDesk.dll')).Hash){throw 'Installed build mismatch'}
 $second=Launch $state.Token;$owned+=,$second
 Wait-For {Listening} 'Updated application did not restore VPN'
 if(Test-Path -LiteralPath (Join-Path $data 'update-resume.json')){throw 'Restart snapshot was not consumed'}
 'PASS updated app restored the previous VPN automatically using a one-time token'
 & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $SourceDirectory 'Prepare-Install.ps1') -OnlyProcessId $second.Id -DataFolder $data -ResultFile (Join-Path $root 'final-token.txt')
 if($LASTEXITCODE){throw 'Final QA app shutdown failed'}
 $restored=Get-Content -LiteralPath (Join-Path $data 'update-resume.json') -Raw | ConvertFrom-Json
 if(!$restored.Vpn -or $restored.ProfileId -ne $profileId){throw 'Restored session snapshot differs'}
 'PASS restored running session retains exact profile and component choices'
} finally {
 foreach($process in $owned){$process.Refresh();if(!$process.HasExited){$null=$process.CloseMainWindow();if(!$process.WaitForExit(10000)){$process.Kill()}}}
 $uninstaller=Join-Path $installed 'unins000.exe'
 if(Test-Path -LiteralPath $uninstaller){$uninstall=Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -PassThru;$null=$uninstall.WaitForExit(10000)}
 foreach($name in $hashes.Keys){if((Get-FileHash -LiteralPath (Join-Path $production $name)).Hash -ne $hashes[$name]){throw 'Production data changed during isolated update QA'}}
}
'PASS production profiles and settings unchanged'
'QA artifacts: '+$root
