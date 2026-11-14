#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$instance = 'ProjectZPurchaseTests'
$localDb = Get-Command SqlLocalDB.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $localDb) {
    $localDb = Get-ChildItem (Join-Path $env:ProgramFiles 'Microsoft SQL Server/*/Tools/Binn/SqlLocalDB.exe') -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $localDb) { throw 'SqlLocalDB.exe is required. The Windows CI image includes the LocalDB runtime.' }

# Only this disposable test instance is touched; never the application's configured LAN server.
& $localDb stop $instance -i 2>$null
& $localDb delete $instance 2>$null
$global:LASTEXITCODE = 0
& $localDb create $instance
if ($LASTEXITCODE -ne 0) { throw 'Could not create the disposable LocalDB instance.' }
& $localDb start $instance
if ($LASTEXITCODE -ne 0) { throw 'Could not start the disposable LocalDB instance.' }

try {
    $env:PROJECTZ_TEST_ADMIN_CONNECTION = "Data Source=(localdb)\$instance;Initial Catalog=master;Integrated Security=true;Encrypt=True;TrustServerCertificate=True;Connect Timeout=30;Pooling=False"
    Push-Location $repo
    try {
        dotnet run --project tests/ProjectZ.Purchase.Sql.Tests -c Release
        if ($LASTEXITCODE -ne 0) { throw "Purchase SQL integration tests failed (exit $LASTEXITCODE)." }
    }
    finally { Pop-Location }
}
finally {
    Remove-Item Env:PROJECTZ_TEST_ADMIN_CONNECTION -ErrorAction SilentlyContinue
    & $localDb stop $instance -i
    & $localDb delete $instance
}
