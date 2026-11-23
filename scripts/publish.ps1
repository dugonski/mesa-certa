# Gera executáveis Windows autocontidos (não exigem .NET instalado) em .\release.
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
param(
    [string]$Runtime = "win-x64",
    [string]$Output = "release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $common = @("-c", "Release", "-r", $Runtime, "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true")
    dotnet publish src/MesaCerta.Desktop @common -o "$Output/desktop"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o desktop." }
    dotnet publish src/MesaCerta.Server @common -o "$Output/server"
    if ($LASTEXITCODE -ne 0) { throw "Falha ao publicar o servidor." }
    Copy-Item .env.example "$Output/server/.env.example" -Force
    Write-Host "Executáveis em $Output\desktop\MesaCerta.exe e $Output\server\MesaCerta.Server.exe"
}
finally {
    Pop-Location
}
