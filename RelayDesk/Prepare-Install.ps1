param([string]$ResultFile,[string]$ResumeToken='', [string]$DataFolder='', [int]$OnlyProcessId=0)
$ErrorActionPreference='Stop'
if(!$DataFolder){$DataFolder=$env:RELAYDESK_DATA_DIR;if(!$DataFolder){$DataFolder=Join-Path $env:LOCALAPPDATA 'RelayDesk'}}
New-Item -ItemType Directory -Path $DataFolder -Force | Out-Null
$resumePath=Join-Path $DataFolder 'update-resume.json'
if($ResumeToken -and $ResumeToken -notmatch '^[a-fA-F0-9]{32}$'){throw 'Invalid restart token'}
if($ResultFile){[IO.File]::WriteAllText($ResultFile,'')}
$identity=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$sessionId=(Get-Process -Id $PID).SessionId
$apps=@(Get-CimInstance Win32_Process -Filter "Name='RelayDesk.exe'" | Where-Object {
 if($OnlyProcessId -and $_.ProcessId -ne $OnlyProcessId){return $false}
 if($_.SessionId -ne $sessionId){return $false}
 $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid
 return $owner.Sid -eq $identity
})
foreach($app in $apps){
 $token=[guid]::NewGuid().ToString('N')
 $prepared=$false;$modern=$false;$deadline=[DateTime]::UtcNow.AddSeconds(60)
 do {
  $pipe=New-Object IO.Pipes.NamedPipeClientStream('.',('RelayDesk.Update.'+$app.ProcessId),[IO.Pipes.PipeDirection]::InOut)
  try {
   $pipe.Connect(700)
   $writer=New-Object IO.StreamWriter($pipe);$writer.AutoFlush=$true
   $reader=New-Object IO.StreamReader($pipe)
   $writer.WriteLine($token)
   $read=$reader.ReadLineAsync()
   if(!$read.Wait(10000)){throw 'RelayDesk did not acknowledge update request'}
   $reply=$read.Result;$modern=$true
   if($reply -eq 'READY'){$prepared=$true;break}
   if($reply -ne 'BUSY'){throw ('RelayDesk update request failed: '+$reply)}
  } catch [TimeoutException] {break}
  finally {$pipe.Dispose()}
  Start-Sleep -Milliseconds 250
 } while([DateTime]::UtcNow -lt $deadline)
 if($modern -and !$prepared){throw 'RelayDesk is busy. Installation has not changed application files.'}
 if(!$prepared){
  # Compatibility with releases predating the installer handshake.
  $settingsPath=Join-Path $DataFolder 'settings.json'
  $settings=if(Test-Path -LiteralPath $settingsPath){Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json}else{$null}
  $children=@(Get-CimInstance Win32_Process -Filter ('ParentProcessId='+$app.ProcessId))
  $vpn=@($children | Where-Object {$_.Name -match 'sing.?box|xray|mihomo'})
  $zapret=@($children | Where-Object {$_.Name -match '^winws2?\.exe$'})
  $profileId=if($settings){[string]$settings.LastConnectedProfileId}else{''}
  foreach($core in $vpn){if($core.CommandLine -match '([a-fA-F0-9]{32})\.runtime\.(json|yaml)'){$profileId=$Matches[1];break}}
  $state=@{Token=$token;CreatedUtc=[DateTime]::UtcNow.ToString('o');Vpn=($vpn.Count -gt 0);Zapret=($zapret.Count -gt 0);ProfileId=$profileId;SelectedProfileId=$profileId;Page=0;Section=-1;Hidden=$false;Minimized=$false;Draft=@{}}
  [IO.File]::WriteAllText($resumePath+'.tmp',(ConvertTo-Json -InputObject $state -Depth 6));Move-Item -LiteralPath ($resumePath+'.tmp') -Destination $resumePath -Force
  Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class RelayDeskCloseWindow {
 delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindowsProc p,IntPtr l);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public static void Close(uint process) { EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);var title=new StringBuilder(512);GetWindowText(h,title,512);if(p==process&&title.ToString().StartsWith("RelayDesk"))PostMessage(h,0x0010,IntPtr.Zero,IntPtr.Zero);return true;},IntPtr.Zero); }
}
'@
  [RelayDeskCloseWindow]::Close($app.ProcessId)
 }
 $deadline=[DateTime]::UtcNow.AddSeconds(60)
 while(Get-Process -Id $app.ProcessId -ErrorAction SilentlyContinue){
  if([DateTime]::UtcNow -gt $deadline){throw 'RelayDesk did not close; application files have not been replaced'}
  Start-Sleep -Milliseconds 200
 }
 $ResumeToken=$token
}
if($ResumeToken){
 if(!(Test-Path -LiteralPath $resumePath)){throw 'Restart snapshot missing'}
 $state=Get-Content -LiteralPath $resumePath -Raw | ConvertFrom-Json
 if($state.Token -ne $ResumeToken){throw 'Restart snapshot token mismatch'}
}
if($ResultFile){[IO.File]::WriteAllText($ResultFile,$ResumeToken)}
Write-Output 'PASS RelayDesk closed for installation; restart snapshot prepared'
