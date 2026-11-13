param([string]$Executable="$PSScriptRoot\..\RelayDesk\bin\Release\net9.0-windows\RelayDesk.exe",[string]$Core='C:\Program Files\RelayDesk\bundled-cores\sing-box\sing-box-1.14.3-windows-amd64\sing-box.exe')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class OrbitQaMouse { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra); }'
$Executable=(Resolve-Path -LiteralPath $Executable).Path
$folder=Join-Path $env:TEMP ('RelayDesk-UI-Restart-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$id=[guid]::NewGuid().ToString('N');$token=[guid]::NewGuid().ToString('N')
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$config=@{log=@{level='error'};inbounds=@(@{type='mixed';listen='127.0.0.1';listen_port=$port});outbounds=@(@{type='direct';tag='direct'});route=@{final='direct'}} | ConvertTo-Json -Depth 10 -Compress
$qaProfile=@{Id=$id;Name='Sweden UI restart QA';Core='sing-box';Config=$config;Managed=$false;Tun=$false;SystemProxy=$false;LocalPort=$port;ProxyUri="http://127.0.0.1:$port";QuickRoutes=@();Executable=$Core}
$settings=@{UiMode='orbit';OrbitBackground='none';ModernUiVersion=1;SharedQuickRoutes=@();Connection=@{Tun=$false;SystemProxy=$false;LocalPort=$port};LastConnectedProfileId=$id;Engines=@{'sing-box'=$Core};InterfaceMotion=$false;AsciiBackground=$false;QuietMode=$false;TrayEnabled=$false;CheckComponentUpdates=$false;ConditionalRouting=$false;AutoServer=$false;AutoZapret=$false}
function Write-Json($Name,$Value){[IO.File]::WriteAllText((Join-Path $folder $Name),(ConvertTo-Json -InputObject $Value -Depth 20),[Text.UTF8Encoding]::new($false))}
Write-Json 'profiles.json' @($qaProfile);Write-Json 'settings.json' $settings
Write-Json 'update-resume.json' @{Token=$token;CreatedUtc=[DateTime]::UtcNow.ToString('O');Vpn=$true;Zapret=$false;ProfileId=$id;SelectedProfileId=$id;Page=0;Section=-1;Draft=@{}}
$proxyKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
function Proxy-Snapshot{Get-ItemProperty -LiteralPath $proxyKey | Select-Object ProxyEnable,ProxyServer,ProxyOverride,AutoConfigURL | ConvertTo-Json -Compress}
$proxyBefore=Proxy-Snapshot
function Wait-For($Check,[int]$Seconds=35){$deadline=[DateTime]::UtcNow.AddSeconds($Seconds);do{try{$value=&$Check;if($value){return $value}}catch [System.Windows.Automation.ElementNotAvailableException]{};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline);throw 'Timed out waiting for isolated interface restart'}
function Port-Ready{try{$client=[Net.Sockets.TcpClient]::new();$task=$client.ConnectAsync('127.0.0.1',$port);$ready=$task.Wait(150)-and $client.Connected;$client.Dispose();return $ready}catch{return $false}}
function App-Process([int]$Except=0,[string]$ResumeToken=""){Get-CimInstance Win32_Process -Filter "Name='RelayDesk.exe'" | Where-Object {$_.ExecutablePath -eq $Executable -and $_.ProcessId -ne $Except -and (!$ResumeToken -or $_.CommandLine.Contains($ResumeToken))} | Select-Object -First 1}
function Root-Window($Process){$p=Get-Process -Id $Process.ProcessId -ErrorAction SilentlyContinue;if($p -and $p.MainWindowHandle -ne [IntPtr]::Zero){return [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)}}
function By-Id($Root,$Name){$Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$Name))}
function Invoke-Button($Button){$Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
function Switch-Ui($Process,$Target){
 $root=Wait-For {Root-Window $Process}
 $settingsButton=@($root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Button))) | Where-Object {!$_.Current.IsOffscreen -and $_.Current.Name -eq 'Настройки'} | Select-Object -First 1
 if(!$settingsButton){throw 'Settings button not found'}
 Invoke-Button $settingsButton
 $choice=Wait-For {By-Id $root 'InterfaceChoice'}
 $choice.SetFocus()
 Wait-For {$choice.Current.HasKeyboardFocus}|Out-Null
 if($Target -eq 'Классический'){[System.Windows.Forms.SendKeys]::SendWait('{END}')}else{[System.Windows.Forms.SendKeys]::SendWait('{HOME}')}
 $apply=Wait-For {$b=By-Id $root 'ApplyInterfaceChoice';if($b -and $b.Current.IsEnabled){$b}}
 Invoke-Button $apply
 $nextToken=Wait-For {if(Test-Path -LiteralPath (Join-Path $folder "update-resume.json")){(Get-Content -LiteralPath (Join-Path $folder "update-resume.json") -Raw|ConvertFrom-Json).Token}}; $next=Wait-For {App-Process $Process.ProcessId $nextToken}
 Wait-For {Root-Window $next}|Out-Null;Wait-For {Port-Ready}|Out-Null
 return $next
}
$oldOverride=$env:RELAYDESK_DATA_DIR;$owned=$null
try{
 $env:RELAYDESK_DATA_DIR=$folder
 $launch=Start-Process -FilePath $Executable -ArgumentList @('--resume-update',$token) -WorkingDirectory (Split-Path $Executable) -PassThru
 $owned=Wait-For {App-Process 0 $token};Wait-For {Root-Window $owned}|Out-Null;Wait-For {Port-Ready}|Out-Null
 Start-Sleep -Milliseconds 500
 'PASS isolated native VPN core is listening without system proxy or TUN'
 $owned=Switch-Ui $owned 'Классический'
 $saved=Get-Content -LiteralPath (Join-Path $folder 'settings.json') -Raw | ConvertFrom-Json
 if($saved.UiMode -ne 'classic'){throw 'Classic preference was not persisted'}
 'PASS Orbit to classic restarts the executable and restores the VPN core'
 Start-Sleep -Milliseconds 400
 $owned=Switch-Ui $owned 'Новый · Orbit'
 $saved=Get-Content -LiteralPath (Join-Path $folder 'settings.json') -Raw | ConvertFrom-Json
 if($saved.UiMode -ne 'orbit'){throw 'Orbit preference was not persisted'}
 $profiles=@(Get-Content -LiteralPath (Join-Path $folder 'profiles.json') -Raw | ConvertFrom-Json)
 if($profiles.Count -ne 1 -or $profiles[0].Id -ne $id -or $profiles[0].Config -ne $config){throw 'Restart changed profile identity or original configuration'}
 if((Proxy-Snapshot) -ne $proxyBefore){throw 'System proxy changed during isolated QA'}
 'PASS classic to Orbit restores VPN, selected server and original JSON; Windows proxy unchanged'
 'COMPLETE: native two-way UI restart verified'
}finally{
 if($owned){try{$root=Root-Window $owned;$close=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Закрыть RelayDesk'));if($close){Invoke-Button $close}}catch{}}
 $env:RELAYDESK_DATA_DIR=$oldOverride
}
