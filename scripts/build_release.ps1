$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location -LiteralPath $projectRoot
try {
    dotnet restore PaperBatchDownloader.sln
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
    dotnet publish PaperBatchDownloader/PaperBatchDownloader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    [xml]$project = Get-Content -LiteralPath 'PaperBatchDownloader/PaperBatchDownloader.csproj' -Raw
    $releaseVersion = [string]$project.Project.PropertyGroup.Version
    $exePath = Join-Path $projectRoot "publish-win-x64/PaperBatchDownloader-v$releaseVersion.exe"
    if (-not (Test-Path -LiteralPath $exePath)) { throw "Missing release executable: $exePath" }
    Write-Output $exePath
}
finally { Pop-Location }
