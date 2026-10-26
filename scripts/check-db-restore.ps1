[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BackupFile,
    [string]$ComposeFile = 'compose.yaml',
    [string]$EnvFile = '.env'
)
$ErrorActionPreference = 'Stop'
$financeRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $financeRoot
try {
    $financeCompose = (Resolve-Path -LiteralPath $ComposeFile).Path
    $financeEnv = (Resolve-Path -LiteralPath $EnvFile).Path
    $financeBackup = (Resolve-Path -LiteralPath $BackupFile).Path
    # A generated, new database is intentional: this check never overwrites finance_tracker.
    $financeSuffix = [guid]::NewGuid().ToString('N')
    $financeDatabase = 'finance_restore_' + $financeSuffix
    $financeRemote = '/tmp/restore-' + $financeSuffix + '.dump'
    & docker compose --env-file $financeEnv -f $financeCompose cp $financeBackup "postgres:$financeRemote"
    if ($LASTEXITCODE -ne 0) { throw 'Restore file copy failed.' }
    & docker compose --env-file $financeEnv -f $financeCompose exec -T postgres psql -U finance -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE $financeDatabase;"
    if ($LASTEXITCODE -ne 0) { throw 'Could not create a new verification database.' }
    & docker compose --env-file $financeEnv -f $financeCompose exec -T postgres pg_restore -U finance --dbname=$financeDatabase --no-owner --no-privileges --exit-on-error $financeRemote
    if ($LASTEXITCODE -ne 0) { throw "Restore failed. Verification database: $financeDatabase" }
    & docker compose --env-file $financeEnv -f $financeCompose exec -T postgres psql -U finance --dbname=$financeDatabase -v ON_ERROR_STOP=1 -c 'SELECT (SELECT count(*) FROM users) AS users, (SELECT count(*) FROM transactions) AS transactions, (SELECT count(*) FROM "__EFMigrationsHistory") AS migrations;'
    if ($LASTEXITCODE -ne 0) { throw 'Restored database query failed.' }
    & docker compose --env-file $financeEnv -f $financeCompose exec -T postgres rm -- $financeRemote
    if ($LASTEXITCODE -ne 0) { throw 'Temporary restore file cleanup failed.' }
    Write-Output "Restore verified in $financeDatabase. Main database was not changed. The verification database is retained for inspection."
}
finally { Pop-Location }
