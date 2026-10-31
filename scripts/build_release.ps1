$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location -LiteralPath $projectRoot
try {
    dotnet restore PaperBatchDownloader.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
    dotnet publish PaperBatchDownloader/PaperBatchDownloader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    Write-Output (Join-Path $projectRoot 'publish-win-x64/PaperBatchDownloader.exe')
}
finally { Pop-Location }
