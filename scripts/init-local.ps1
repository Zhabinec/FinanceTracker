$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $projectRoot '.env'
if (Test-Path -LiteralPath $envPath) {
    Write-Host '.env already exists; existing settings were preserved.'
    exit 0
}
$key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
[System.IO.File]::WriteAllText($envPath, "JWT_SIGNING_KEY=$key`n")
Write-Host 'Created .env with a random local signing key.'
