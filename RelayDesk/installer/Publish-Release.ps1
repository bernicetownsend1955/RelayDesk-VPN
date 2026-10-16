param(
    [Parameter(Mandatory=$true)][string]$Repository,
    [Parameter(Mandatory=$true)][string]$NotesFile,
    [string]$Compiler = "$PSScriptRoot\tools\InnoSetup\ISCC.exe",
    [switch]$Publish,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9_.-]{1,100}$' -or $Repository.Split('/')[1] -in @('.','..')) { throw 'Repository must be owner/repository.' }
$notes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile).Path)
if ([string]::IsNullOrWhiteSpace($notes)) { throw 'Release notes are required.' }
$token = if ($env:GH_TOKEN) { $env:GH_TOKEN } else { $env:GITHUB_TOKEN }
if (!$token) { throw 'Set GH_TOKEN or GITHUB_TOKEN with repository Contents write access. The token is never bundled with the application.' }
[xml]$definition = Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'RelayDesk.csproj')
$version = [string]$definition.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have three numeric components.' }
$tag = 'v' + $version
if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -Compiler $Compiler -Repository $Repository
    if ($LASTEXITCODE) { throw 'Build failed.' }
}
$installer = Join-Path $PSScriptRoot ('output\RelayDesk-Setup-' + $version + '-x64.exe')
if (!(Test-Path -LiteralPath $installer)) { throw 'Installer missing.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$checksum = $installer + '.sha256'
[IO.File]::WriteAllText($checksum,($hash + '  ' + [IO.Path]::GetFileName($installer) + "`n"),[Text.Encoding]::ASCII)
$headers = @{Authorization=('Bearer ' + $token);Accept='application/vnd.github+json';'User-Agent'='RelayDesk-Publisher';'X-GitHub-Api-Version'='2022-11-28'}
$api = 'https://api.github.com/repos/' + $Repository
$repo = Invoke-RestMethod -Uri $api -Headers $headers
if ($repo.private) { throw 'Public repository required for update clients without GitHub credentials.' }
$release = $null
try { $release = Invoke-RestMethod -Uri ($api+'/releases/tags/'+$tag) -Headers $headers }
catch { if (!$_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 404) { throw } }
if ($release -and !$release.draft) { throw 'This version is already published. Increase the version; published packages are not overwritten.' }
foreach ($file in @($installer,$checksum)) {
    if ($release -and @($release.assets | Where-Object {$_.name -eq [IO.Path]::GetFileName($file)}).Count) { throw 'Draft already contains an asset with this name. Review it on GitHub before retrying.' }
}
$body = @{tag_name=$tag;name=('RelayDesk '+$version);body=$notes;draft=$true;prerelease=$false} | ConvertTo-Json
if (!$release) { $release = Invoke-RestMethod -Method Post -Uri ($api+'/releases') -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) }
else { $release = Invoke-RestMethod -Method Patch -Uri ($api+'/releases/'+$release.id) -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) }
foreach ($file in @($installer,$checksum)) {
    $assetName = [uri]::EscapeDataString([IO.Path]::GetFileName($file))
    $upload = 'https://uploads.github.com/repos/'+$Repository+'/releases/'+$release.id+'/assets?name='+$assetName
    $asset = Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType 'application/octet-stream' -InFile $file
    $expected = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.digest -ne ('sha256:'+$expected)) { throw 'GitHub asset digest differs. The release remains a draft.' }
}
if ($Publish) {
    $body = @{draft=$false;make_latest='true'} | ConvertTo-Json
    $release = Invoke-RestMethod -Method Patch -Uri ($api+'/releases/'+$release.id) -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
}
[pscustomobject]@{Version=$version;Draft=$release.draft;Page=$release.html_url}
