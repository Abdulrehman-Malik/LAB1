# Loads infra/.env into this PowerShell session so services started with `dotnet run` can log in to the lab's SQL Server.
# Usage (from the repo root, note the leading dot):  . .\scripts\dev-env.ps1
$envFile = Join-Path $PSScriptRoot '..\infra\.env'
Get-Content $envFile | Where-Object { $_ -match '^(\w+)=(.*)$' } | ForEach-Object { Set-Item "env:$($Matches[1])" $Matches[2] }
$env:Database__Password = $env:MSSQL_SA_PASSWORD
Write-Host 'Loaded infra/.env (Database__Password is set)'
