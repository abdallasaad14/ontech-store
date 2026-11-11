# Starts / stops the two AUDIT test servers. Never touches the live server on 5080.
#   5081 = SQL Server .\SQLEXPRESS\OnTechStore, Production config
#   5082 = SQLite copy in audit\build\api-sqlite, Development config
# Usage:
#   .\servers.ps1 start            # start both from audit\build\api-*
#   .\servers.ps1 stop             # stop only processes listening on 5081/5082
#   .\servers.ps1 publish          # rebuild audit\build\api-* from THIS worktree's source (Debug), keeps the SQLite copy
param([ValidateSet('start','stop','publish')][string]$Action = 'start', [string]$JwtKey = '', [ValidateSet('','sqlite','sqlserver')][string]$Only = '')

$ErrorActionPreference = 'Stop'
$audit = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$root  = Split-Path -Parent $audit
$build = Join-Path $audit 'build'
$ev    = Join-Path $audit 'evidence'
New-Item -ItemType Directory -Force $ev | Out-Null

function Stop-Port([int]$port) {
    if ($port -eq 5080) { throw 'refusing to touch 5080' }
    Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
}

function Start-Api([string]$name, [int]$port, [string]$envName, [hashtable]$extra) {
    $dir = Join-Path $build "api-$name"
    $exe = Join-Path $dir 'OnTech.Store.Api.exe'
    $out = Join-Path $ev "api_${name}_stdout.log"; $err = Join-Path $ev "api_${name}_stderr.log"
    $sets = "set ASPNETCORE_ENVIRONMENT=$envName&& "
    foreach ($k in $extra.Keys) { $sets += "set $k=$($extra[$k])&& " }
    $cmd = "/c $sets`"$exe`" --urls http://127.0.0.1:$port > `"$out`" 2> `"$err`""
    Start-Process cmd.exe -ArgumentList $cmd -WorkingDirectory $dir -WindowStyle Hidden
    for ($i = 0; $i -lt 60; $i++) {
        if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) { return "$name listening on $port ($envName)" }
        Start-Sleep -Milliseconds 500
    }
    "$name did NOT start on $port, see $err"
}

switch ($Action) {
    'stop' { Stop-Port 5081; Stop-Port 5082; 'stopped 5081/5082' }
    'publish' {
        Stop-Port 5081; Stop-Port 5082
        $proj = Join-Path $root 'src\OnTech.Store.Api\OnTech.Store.Api.csproj'
        foreach ($n in 'sqlserver','sqlite') {
            $dir = Join-Path $build "api-$n"
            dotnet build $proj -c Debug -o $dir --nologo -v q | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "build failed for $n" }
        }
        'published'
    }
    'start' {
        $extra = @{}
        if ($JwtKey) { $extra['Jwt__SigningKey'] = $JwtKey }
        if ($Only -ne 'sqlite')    { Start-Api 'sqlserver' 5081 'Production'  $extra }
        if ($Only -ne 'sqlserver') { Start-Api 'sqlite'    5082 'Development' $extra }
    }
}
