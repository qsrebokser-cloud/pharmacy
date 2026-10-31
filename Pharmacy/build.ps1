$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$sdk = Join-Path (Split-Path $projectRoot) '.dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) {
    $sdk = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_HOME = Join-Path (Split-Path $projectRoot) '.dotnet-home'
& $sdk publish (Join-Path $projectRoot 'Pharmacy.csproj') -c Release -o (Join-Path $projectRoot 'Deliverable') --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed. Close the application and retry.'
}
& $sdk (Join-Path $projectRoot 'Deliverable/ZharPtitsa.dll') --check
if ($LASTEXITCODE -ne 0) {
    throw 'Checks failed. See Deliverable/error.log'
}
Write-Host 'Ready: Deliverable/ZharPtitsa.exe. Checks: Deliverable/checks.txt'
