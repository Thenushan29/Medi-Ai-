<#
.SYNOPSIS
    Wires MediTrail to Supabase and verifies it actually works.

.DESCRIPTION
    Asks for the two values only you can supply — the database password and the secret API key —
    then does the fiddly parts for you:

      • Builds the session-pooler connection string (the direct host is IPv6-only and will not
        connect from a normal network; see docs/SUPABASE_SETUP.md).
      • Tries both pooler hosts for the region and keeps whichever one authenticates, so you do
        not have to work out whether yours is aws-0 or aws-1.
      • Stores everything in dotnet user-secrets — outside the repository, so nothing can be
        committed by accident.
      • Starts the API and calls /health/ready to confirm the database and the storage bucket
        both answer.

.EXAMPLE
    ./scripts/setup-supabase.ps1
#>

[CmdletBinding()]
param(
    [string]$ProjectRef = 'xfhdukhtoixzswjidkhn',
    [string]$Region     = 'ap-southeast-1',
    [string]$Bucket     = 'documents',
    [int]$Port          = 5000
)

$ErrorActionPreference = 'Stop'

$apiDir = Join-Path $PSScriptRoot '..\backend\MediTrail.Api' | Resolve-Path
$projectUrl = "https://$ProjectRef.supabase.co"

function Write-Step { param([string]$Text) Write-Host "`n$Text" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "  OK   $Text" -ForegroundColor Green }
function Write-Bad  { param([string]$Text) Write-Host "  FAIL $Text" -ForegroundColor Red }
function Write-Info { param([string]$Text) Write-Host "       $Text" -ForegroundColor DarkGray }

Write-Host "MediTrail - Supabase setup" -ForegroundColor White
Write-Host "Project: $projectUrl"

# ---------------------------------------------------------------------------
# 1. Collect the two secrets
# ---------------------------------------------------------------------------
Write-Step "1. Database password"
Write-Info "Supabase dashboard -> Settings -> Database."
Write-Info "Forgotten it? 'Reset database password' there; it is shown only once."

$securePassword = Read-Host -Prompt "   Database password" -AsSecureString
$dbPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword))

if ([string]::IsNullOrWhiteSpace($dbPassword)) {
    Write-Bad "No password entered. Nothing was changed."
    exit 1
}

Write-Step "2. Secret API key"
Write-Info "Supabase dashboard -> Settings -> API Keys."
Write-Info "Take the key starting 'sb_secret_' (or the legacy 'service_role' JWT)."
Write-Info "NOT 'sb_publishable_' / 'anon' - those cannot write to storage."

$secureKey = Read-Host -Prompt "   Secret key" -AsSecureString
$serviceKey = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey))

if ([string]::IsNullOrWhiteSpace($serviceKey)) {
    Write-Bad "No key entered. Nothing was changed."
    exit 1
}

if ($serviceKey -like 'sb_publishable_*') {
    Write-Bad "That is the publishable key. It is public by design and cannot write to storage."
    Write-Info "Go back to Settings -> API Keys and copy the 'sb_secret_...' one."
    exit 1
}

# ---------------------------------------------------------------------------
# 2. Find a pooler host that resolves on IPv4
# ---------------------------------------------------------------------------
Write-Step "3. Locating the connection pooler"

$candidates = @("aws-0-$Region.pooler.supabase.com", "aws-1-$Region.pooler.supabase.com")
$reachable = @()

foreach ($host_ in $candidates) {
    $a = Resolve-DnsName $host_ -Type A -ErrorAction SilentlyContinue |
         Where-Object { $_.Type -eq 'A' } | Select-Object -First 1
    if ($a) {
        Write-Ok "$host_ -> $($a.IPAddress)"
        $reachable += $host_
    } else {
        Write-Info "$host_ - no IPv4 address"
    }
}

if ($reachable.Count -eq 0) {
    Write-Bad "No pooler host resolved. Check the region in your dashboard's Connect dialog."
    exit 1
}

# ---------------------------------------------------------------------------
# 3. Store configuration, then verify by actually connecting
# ---------------------------------------------------------------------------
function Set-Secrets {
    param([string]$PoolerHost)

    $connection = "Host=$PoolerHost;Port=5432;Database=postgres;Username=postgres.$ProjectRef;" +
                  "Password=$dbPassword;SSL Mode=Require;Trust Server Certificate=true"

    Push-Location $apiDir
    try {
        dotnet user-secrets set "ConnectionStrings:Postgres" $connection  | Out-Null
        dotnet user-secrets set "Supabase:Url"               $projectUrl  | Out-Null
        dotnet user-secrets set "Supabase:ServiceKey"        $serviceKey  | Out-Null
        dotnet user-secrets set "Supabase:Bucket"            $Bucket      | Out-Null
    } finally {
        Pop-Location
    }
}

function Test-Readiness {
    $log    = Join-Path $env:TEMP 'meditrail-setup.log'
    $errLog = Join-Path $env:TEMP 'meditrail-setup.err.log'

    # Start-Process has no -Environment on Windows PowerShell 5.1, so set it on this process;
    # child processes inherit it.
    $env:ASPNETCORE_URLS = "http://localhost:$Port"

    $process = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--no-launch-profile', '--project', "`"$apiDir`"" `
        -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $log -RedirectStandardError $errLog

    try {
        # Give the host time to build and bind before deciding it is broken.
        $deadline = (Get-Date).AddSeconds(90)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 3
            if ($process.HasExited) { break }
            try {
                return Invoke-RestMethod "http://localhost:$Port/health/ready" -TimeoutSec 10
            } catch {
                $response = $_.Exception.Response
                if ($response -and $response.StatusCode.value__ -eq 503) {
                    # 503 is a real answer - the app is up and telling us what is wrong.
                    $reader = New-Object IO.StreamReader($response.GetResponseStream())
                    return $reader.ReadToEnd() | ConvertFrom-Json
                }
                # Anything else means it has not finished starting; keep waiting.
            }
        }
        return $null
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
}

$result = $null
$chosenHost = $null

foreach ($host_ in $reachable) {
    Write-Step "4. Trying $host_"
    Set-Secrets -PoolerHost $host_

    $result = Test-Readiness

    if ($null -eq $result) {
        Write-Bad "The API did not start. See $env:TEMP\meditrail-setup.err.log"
        exit 1
    }

    if ("$($result.database)" -like 'ok*') {
        $chosenHost = $host_
        break
    }

    Write-Info "database: $($result.database)"
    if ("$($result.database)" -notlike '*authentication*') {
        # Not a wrong-host symptom - trying the other pooler will not help.
        break
    }
}

# ---------------------------------------------------------------------------
# 4. Report
# ---------------------------------------------------------------------------
Write-Step "Result"

$dbOk     = "$($result.database)" -like 'ok*'
$bucketOk = "$($result.bucket)"   -eq 'ok'

if ($dbOk)     { Write-Ok  "database: $($result.database)" }
else           { Write-Bad "database: $($result.database)" }

if ($bucketOk) { Write-Ok  "storage bucket '$Bucket': ok" }
else           { Write-Bad "storage bucket '$Bucket': $($result.bucket)" }

if ($dbOk -and $bucketOk) {
    Write-Host "`nReady. Connected via $chosenHost." -ForegroundColor Green
    Write-Host "Start the app with:  cd backend/MediTrail.Api; dotnet run" -ForegroundColor DarkGray
    exit 0
}

Write-Host "`nWhat to fix:" -ForegroundColor Yellow

if (-not $dbOk) {
    switch -Wildcard ("$($result.database)") {
        '*authentication*'  { Write-Info "Wrong password. Settings -> Database -> Reset database password." }
        '*does not exist*'  { Write-Info "Schema not applied. Run db/01_schema.sql then db/02_views.sql in the SQL editor." }
        '*relation*'        { Write-Info "Schema not applied. Run db/01_schema.sql then db/02_views.sql in the SQL editor." }
        default             { Write-Info "See docs/SUPABASE_SETUP.md troubleshooting table." }
    }
}

if (-not $bucketOk) {
    switch -Wildcard ("$($result.bucket)") {
        '*404*'  { Write-Info "Bucket missing. Storage -> New bucket -> name it '$Bucket', Public ON." }
        '*Bucket not found*' { Write-Info "Bucket missing. Storage -> New bucket -> name it '$Bucket', Public ON." }
        '*401*'  { Write-Info "Wrong key - that looks like the publishable/anon key. Use 'sb_secret_...'." }
        '*403*'  { Write-Info "Wrong key - that looks like the publishable/anon key. Use 'sb_secret_...'." }
        default  { Write-Info "See docs/SUPABASE_SETUP.md troubleshooting table." }
    }
}

exit 1
