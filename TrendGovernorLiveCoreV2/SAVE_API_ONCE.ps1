$ErrorActionPreference = 'Stop'

Write-Host 'TRENDGOVERNOR - LUU API MOT LAN TREN MAY WINDOWS' -ForegroundColor Cyan
Write-Host 'Credential duoc luu trong bien moi truong User cua Windows, khong ghi vao source/ZIP.' -ForegroundColor Yellow

$apiKey = Read-Host 'Nhap Binance API Key'
$secureSecret = Read-Host 'Nhap Binance API Secret' -AsSecureString
$secretPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)

try {
    $apiSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPtr)
    if ([string]::IsNullOrWhiteSpace($apiKey) -or [string]::IsNullOrWhiteSpace($apiSecret)) {
        throw 'API Key/Secret khong duoc de trong.'
    }

    [Environment]::SetEnvironmentVariable('BINANCE_API_KEY', $apiKey.Trim(), 'User')
    [Environment]::SetEnvironmentVariable('BINANCE_API_SECRET', $apiSecret.Trim(), 'User')

    Write-Host 'DA LUU. Dong tool neu dang mo, sau do mo lai START_TOOL.cmd.' -ForegroundColor Green
}
finally {
    if ($secretPtr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPtr)
    }
    $apiSecret = $null
    $secureSecret = $null
}
