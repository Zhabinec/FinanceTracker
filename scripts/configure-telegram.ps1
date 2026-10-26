#Requires -Version 7.1
<#
.SYNOPSIS
Checks a Telegram bot and saves its token in the API project's local .NET User Secrets.
.DESCRIPTION
Run interactively. Enter a newly issued BotFather token at the masked prompt.
No token parameter is accepted, so the token need not appear in command history.
This checks getMe only; it does not publish a Mini App or configure bot messages.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$financeRoot = Split-Path -Parent $PSScriptRoot
$financeApiProject = Join-Path $financeRoot 'backend/src/Finance.Api/Finance.Api.csproj'
if (-not (Test-Path -LiteralPath $financeApiProject)) { throw 'Не найден проект Finance.Api.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Установите .NET SDK перед настройкой бота.' }

$financeToken = $null
$financeSecretJson = $null
$financeSecretOutput = $null
$financeClient = $null
$financeResponse = $null
try {
    $financeToken = (Read-Host 'Новый токен BotFather (ввод скрыт)' -MaskInput).Trim()
    if ($financeToken -notmatch '^\d{5,20}:[A-Za-z0-9_-]{20,100}$') {
        throw 'Неверный формат токена. Скопируйте новый токен из BotFather полностью.'
    }

    $financeHandler = [Net.Http.HttpClientHandler]::new()
    $financeHandler.AllowAutoRedirect = $false
    $financeClient = [Net.Http.HttpClient]::new($financeHandler)
    $financeClient.Timeout = [TimeSpan]::FromSeconds(15)
    try {
        $financeResponse = $financeClient.GetAsync('https://api.telegram.org/bot' + $financeToken + '/getMe').GetAwaiter().GetResult()
    }
    catch {
        # Do not print exception details: Telegram authenticates through the request URL.
        throw 'Не удалось обратиться к Telegram. Проверьте доступ к api.telegram.org и повторите настройку.'
    }
    if (-not $financeResponse.IsSuccessStatusCode) {
        throw 'Telegram не принял токен. Проверьте, что он действующий и скопирован полностью.'
    }
    try {
        $financeBot = $financeResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    }
    catch { throw 'Telegram вернул неожиданный ответ. Настройки не сохранены.' }
    if (-not $financeBot.ok -or -not $financeBot.result.is_bot -or
        $financeBot.result.username -notmatch '^[A-Za-z0-9_]{5,32}$') {
        throw 'Не удалось подтвердить данные бота. Настройки не сохранены.'
    }

    # JSON is passed on stdin rather than putting the secret in process arguments or a repository file.
    $financeSecretJson = @{ 'Telegram:BotToken' = $financeToken } | ConvertTo-Json -Compress
    $financeSecretOutput = $financeSecretJson | & dotnet user-secrets set --project $financeApiProject 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось сохранить токен в локальных .NET User Secrets.' }
    Write-Host ('Проверен бот @{0}. Токен сохранён в локальных секретах Finance.Api.' -f $financeBot.result.username)
    Write-Host 'Настройка токена завершена. Вход через Telegram и публикация Mini App подключаются отдельно.'
}
finally {
    if ($null -ne $financeResponse) { $financeResponse.Dispose() }
    if ($null -ne $financeClient) { $financeClient.Dispose() }
    $financeToken = $null
    $financeSecretJson = $null
    $financeSecretOutput = $null
}
