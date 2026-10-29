$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $projectRoot '.env'
if (Test-Path -LiteralPath $envPath) {
    Write-Host '.env already exists; existing settings were preserved.'
    exit 0
}
$key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
$wrappingKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
[System.IO.File]::WriteAllText($envPath, "JWT_SIGNING_KEY=$key`nFINANCIAL_WRAPPING_KEY=$wrappingKey`nTELEGRAM_ONLY=true`nMANAGED_FINANCIAL_KEYS=true`nSERVER_FINANCIAL_RECORDS=true`nTELEGRAM_BOT_TOKEN=`nTELEGRAM_BOT_USERNAME=PetProjectFinanceBot`n")
Write-Host 'Created .env with independent local signing and financial keys. Set TELEGRAM_BOT_TOKEN and TELEGRAM_BOT_USERNAME before starting the API.'
