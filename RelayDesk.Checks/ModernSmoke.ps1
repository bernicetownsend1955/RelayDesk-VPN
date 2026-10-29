param([string]$Executable,[switch]$ExpectBundledCores,[switch]$CheckTun,[switch]$CheckRouting)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$folder=Join-Path $env:TEMP ('RelayDesk-modern-smoke-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'settings.json'),'{"QuietMode":false,"CheckComponentUpdates":false,"TrayEnabled":false}')
if($CheckTun){
 $profiles=@(@{Id=[guid]::NewGuid().ToString('N');Name='QA profile';Core='sing-box';Config='{"inbounds":[{"type":"tun","tag":"qa-tun"}]}';Managed=$false;Tun=$false})
 [IO.File]::WriteAllText((Join-Path $folder 'profiles.json'),(ConvertTo-Json -InputObject $profiles -Depth 8))
}
$start=New-Object Diagnostics.ProcessStartInfo
$start.FileName=(Resolve-Path -LiteralPath $Executable).Path;$start.UseShellExecute=$false;$start.EnvironmentVariables['RELAYDESK_DATA_DIR']=$folder
$process=[Diagnostics.Process]::Start($start)
try {
 Start-Sleep -Seconds 2;$process.Refresh()
 if($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero){throw 'Missing owned application window'}
 $window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
 if($window.Current.ProcessId -ne $process.Id){throw 'Incorrect window owner'}
 function Find([string]$name){
  $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)
  $element=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
  if(!$element){throw ('Missing control: '+$name)};return $element
 }
 function Invoke([string]$name){
  $nameCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$name)
  $invokeCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::IsInvokePatternAvailableProperty,$true)
  $target=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(New-Object System.Windows.Automation.AndCondition($nameCondition,$invokeCondition)))
  if(!$target){throw ('Missing action: '+$name)}
  $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 200
 }
 foreach($name in @('VPN','zapret','Проверить TCP','Проверить HTTPS','Журнал')){$null=Find $name;'PASS modern control: '+$name}
 foreach($name in @('≡    Профили','↔    Соединения','⊞    Приложения','⚙    Настройки','⌂    Главная')){
  Invoke $name;'PASS main navigation: '+$name
  if($name -eq '↔    Соединения'){
   foreach($column in @('Приложение','Назначение','Тип','Маршрут','Выбрано правилом')){Invoke $column}
   foreach($label in @('Фильтр: приложение','Фильтр: назначение','Фильтр: тип','Фильтр: маршрут','Фильтр: правило','Настроить выбранные…')){
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$label)
    $extra=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
    if($extra -and !$extra.Current.IsOffscreen){throw ('Unexpected always-visible table control '+$label)}
   }
   'PASS compact column headers sort without permanent filter fields or batch toolbar'
  }
 }
 Invoke 'Настройки'
 foreach($name in @('Стратегия zapret','Обновить список стратегий','Применить стратегию','Настроить трафик выбранной подписки')){$null=Find $name}
 'PASS published settings expose readable Russian captions'
 Invoke 'Автозапуск Windows…'
 foreach($name in @('Запускать RelayDesk при входе в Windows','Автоматически включать VPN','Автоматически включать zapret','Профиль VPN для автозапуска')){$null=Find $name}
 $enabled=(Find 'Запускать RelayDesk при входе в Windows').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
 if($enabled.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off){throw 'Startup unexpectedly enabled'}
 Invoke 'Сохранить автозапуск'
 $settings=Get-Content -LiteralPath (Join-Path $folder 'settings.json') -Raw | ConvertFrom-Json
 if($settings.WindowsStartup){throw 'Startup save unexpectedly enabled task'}
 $null=Find '⌂    Главная';'PASS Windows startup saves inside modern shell and remains opt-in'
 Invoke 'Расширенные настройки  ›';Invoke 'TUN, DNS, JSON, ядра и автоматика…'
 $null=Find 'Раздел параметров';$null=Find '⌂    Главная'
 $legacy=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Управление подключением')
 if($window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$legacy)){throw 'Legacy sidebar is exposed'}
 'PASS parameter forms keep modern sidebar'
 if($CheckTun){
  $tun=Find 'TUN: подключать приложения без настройки прокси'
  if($tun.Current.IsEnabled -or $tun.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On){throw 'Raw TUN state incorrect'}
  Invoke 'Настроить TUN в JSON'
  $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'ConfigEditor')
  if(!$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)){throw 'TUN did not open JSON editor'}
  'PASS TUN action opens actual JSON editor inside modern shell'
 }
 Invoke '⌂    Главная';$null=Find 'VPN';'PASS main navigation exits parameter forms'
 if($CheckRouting){Invoke '⊞    Приложения';$null=Find 'Добавить приложение…';$null=Find 'Добавить сайт / IP…';$null=Find 'Применить правила';'PASS routing actions available in modern rules page'}
 if($ExpectBundledCores){
  $settings=Get-Content -LiteralPath (Join-Path $folder 'settings.json') -Raw | ConvertFrom-Json
  foreach($core in @('sing-box','Xray','mihomo')){if(!(Test-Path -LiteralPath $settings.Engines.$core)){throw ('Missing bundled core '+$core)}}
  if(!(Test-Path -LiteralPath $settings.ZapretExe) -or !$settings.ZapretArgs){throw 'Bundled zapret not initialized'}
  'PASS four bundled cores initialized without downloads'
 }
 if(Test-Path -LiteralPath (Join-Path $folder 'components')){throw 'Unexpected download during UI smoke'}
 'PASS isolated smoke completed without starting network cores'
} finally {
 if(!$process.HasExited){$null=$process.CloseMainWindow();$null=$process.WaitForExit(8000)}
 if(!$process.HasExited){throw 'Owned QA window did not close'}
 $process.Dispose()
}
