param([Parameter(Mandatory=$true)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
$PublishDir = (Resolve-Path -LiteralPath $PublishDir).Path
$cache = Join-Path $PSScriptRoot 'core-cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$destination = Join-Path $PublishDir 'bundled-cores'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$manifest = @()
foreach ($core in (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cores.lock.json') -Raw | ConvertFrom-Json)) {
    $prefix = 'https://github.com/'+$core.Repository+'/releases/download/'
    if (!$core.Url.StartsWith($prefix) -or $core.Sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid core lock entry' }
    $archive = Join-Path $cache ($core.Id+'-'+$core.Version+'.zip')
    if (!(Test-Path -LiteralPath $archive)) {
        if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
            & curl.exe --fail --location --silent --show-error --max-time 120 --output ($archive+'.partial') $core.Url
            if ($LASTEXITCODE) { throw ('Download failed: '+$core.Id) }
        } else { Invoke-WebRequest -Uri $core.Url -OutFile ($archive+'.partial') -UseBasicParsing -TimeoutSec 120 }
        if ((Get-FileHash -LiteralPath ($archive+'.partial') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $core.Sha256) { throw ('Core checksum mismatch: '+$core.Id) }
        Move-Item -LiteralPath ($archive+'.partial') -Destination $archive -Force
    }
    if ((Get-Item -LiteralPath $archive).Length -ne $core.Size -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $core.Sha256) { throw ('Cached core differs: '+$core.Id) }
    $folder = Join-Path $destination $core.Id
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $folder -Force
    $executables = @(Get-ChildItem -LiteralPath $folder -File -Recurse | Where-Object { ($_.Name -eq $core.Executable) -or ($core.Id -eq 'mihomo' -and $_.Name -like 'mihomo*.exe') })
    if ($executables.Count -ne 1) { throw ('Executable not found or ambiguous: '+$core.Id) }
    $exe = $executables[0]
    if ($core.Id -eq 'zapret') {
        foreach ($dependency in @('cygwin1.dll','WinDivert.dll','WinDivert64.sys')) {
            if (!(Test-Path -LiteralPath (Join-Path $exe.DirectoryName $dependency))) { throw ('Missing zapret dependency: '+$dependency) }
        }
    }
    $license = Join-Path $folder 'UPSTREAM-LICENSE.txt'
    if (!(Test-Path -LiteralPath $license)) {
        $metadata = Invoke-RestMethod -Uri ('https://api.github.com/repos/'+$core.Repository+'/license?ref='+[uri]::EscapeDataString($core.Version)) -Headers @{'User-Agent'='RelayDesk-Builder'}
        [IO.File]::WriteAllBytes($license,[Convert]::FromBase64String($metadata.content))
    }
    $notice = 'Source: https://github.com/'+$core.Repository+'/tree/'+$core.Version+[Environment]::NewLine+'Release: '+$core.Url+[Environment]::NewLine+'Archive SHA256: '+$core.Sha256
    [IO.File]::WriteAllText((Join-Path $folder 'SOURCE.txt'),$notice)
    $files = @(Get-ChildItem -LiteralPath $folder -Recurse -File | ForEach-Object { @{Path=$_.FullName.Substring($destination.Length+1).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()} })
    $manifest += @{Id=$core.Id;Version=$core.Version;Executable=$exe.FullName.Substring($destination.Length+1).Replace('\','/');Files=$files}
    Write-Output ('Bundled '+$core.Id+' '+$core.Version)
}
[IO.File]::WriteAllText((Join-Path $destination 'manifest.json'),(ConvertTo-Json -InputObject $manifest -Depth 6))
