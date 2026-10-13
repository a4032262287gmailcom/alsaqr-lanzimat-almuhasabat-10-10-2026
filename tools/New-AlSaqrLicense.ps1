[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PrivateKeyPath,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Customer,
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Fa-f0-9]{32}$')][string]$MachineId,
    [ValidateRange(1,3650)][int]$Days = 365,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) {
    throw "Private key file not found: $PrivateKeyPath"
}
if ([string]::IsNullOrWhiteSpace($Customer)) { throw 'Customer name is required.' }

# The private signing key is intentionally external to this source repository.
$keyPem = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $PrivateKeyPath))
$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($keyPem)
    $now = [DateTimeOffset]::UtcNow
    $payload = [ordered]@{
        product = 'ALSAQR-ACCOUNTING-DESKTOP'
        licenseId = [Guid]::NewGuid().ToString('N')
        customer = $Customer.Trim()
        machineId = $MachineId.ToUpperInvariant()
        issuedUtc = $now.ToString('O')
        expiresUtc = $now.AddDays($Days).ToString('O')
        features = @('accounting', 'cashier', 'reports', 'inventory')
    }
    $json = $payload | ConvertTo-Json -Depth 6 -Compress
    $payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $signature = $rsa.SignData(
        $payloadBytes,
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
    )
    $envelope = [ordered]@{
        payload = [Convert]::ToBase64String($payloadBytes)
        signature = [Convert]::ToBase64String($signature)
    } | ConvertTo-Json -Compress

    $destination = [System.IO.Path]::GetFullPath($OutputPath)
    $parent = [System.IO.Path]::GetDirectoryName($destination)
    if (-not [string]::IsNullOrWhiteSpace($parent)) { [System.IO.Directory]::CreateDirectory($parent) | Out-Null }
    [System.IO.File]::WriteAllText($destination, $envelope, [System.Text.UTF8Encoding]::new($false))
    Write-Host "License created: $destination" -ForegroundColor Green
    Write-Host "Customer: $Customer"
    Write-Host "Machine: $($MachineId.ToUpperInvariant())"
    Write-Host "Expires: $($now.AddDays($Days).ToLocalTime().ToString('yyyy-MM-dd'))"
}
finally {
    $rsa.Dispose()
}
