[CmdletBinding()]
param(
    [string]$ComposeFile = 'compose.yaml',
    [string]$EnvFile = '.env',
    [string]$OutputDirectory = 'artifacts/backups'
)
$ErrorActionPreference = 'Stop'
$financeRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $financeRoot
try {
    $financeCompose = (Resolve-Path -LiteralPath $ComposeFile).Path
    $financeEnv = (Resolve-Path -LiteralPath $EnvFile).Path
    $financeOutput = New-Item -ItemType Directory -Path $OutputDirectory -Force
    $financeName = 'finance-{0}-{1}.dump' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 8))
    $financeRemote = '/tmp/' + $financeName
    $financeDestination = Join-Path $financeOutput.FullName $financeName
    & docker compose --env-file $financeEnv -f $financeCompose exec -T postgres pg_dump -U finance -d finance_tracker --format=custom --file=$financeRemote
    if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed.' }
    & docker compose --env-file $financeEnv -f $financeCompose cp "postgres:$financeRemote" $financeDestination
    if ($LASTEXITCODE -ne 0) { throw 'Backup copy failed.' }
    & docker compose --env-file $financeEnv -f $financeCompose exec -T postgres rm -- $financeRemote
    if ($LASTEXITCODE -ne 0) { throw 'Temporary backup cleanup failed; local copy was saved.' }
    Get-Item -LiteralPath $financeDestination | Select-Object FullName, Length
    $financeHash = (Get-FileHash -LiteralPath $financeDestination -Algorithm SHA256).Hash
    Write-Output "SHA256: $financeHash"
}
finally { Pop-Location }
