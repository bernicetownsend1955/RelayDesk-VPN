param([Parameter(Mandatory=$true)][string]$ProjectDir)
$ErrorActionPreference='Stop'
$decoder=New-Object System.Text.UTF8Encoding($false,$true)
[xml]$project=Get-Content -LiteralPath (Join-Path $ProjectDir 'RelayDesk.csproj')
$version=[string]$project.Project.PropertyGroup.Version
$files=@(Get-ChildItem -LiteralPath $ProjectDir -File | Where-Object {$_.Extension -in '.cs','.xaml'})
$files+=Get-Item -LiteralPath (Join-Path $ProjectDir "installer\release-notes-$version.md")
foreach($file in $files){
 $content=$decoder.GetString([IO.File]::ReadAllBytes($file.FullName))
 if($content -match '\?{3,}' -or $content.Contains([char]0xFFFD)){
  throw ('Damaged text encoding: '+$file.Name)
 }
}
Write-Output ('PASS UTF-8 source and release text: '+$files.Count+' files')
