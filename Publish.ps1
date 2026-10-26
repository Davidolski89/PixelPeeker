param(
    # Only use after the same source has already passed its relevant tests.
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src/FFmpegStudio.Desktop/FFmpegStudio.Desktop.csproj'
$solutionPath = Join-Path $PSScriptRoot 'FFmpegStudio.sln'
$latestPublishPath = Join-Path $PSScriptRoot 'artifacts/portable-win-x64'
[xml]$taskBuildProperties=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props')
$publishPath=Join-Path $PSScriptRoot ('artifacts/portable-win-x64-'+$taskBuildProperties.Project.PropertyGroup.Version)
if(-not $SkipTests) {
    dotnet test $solutionPath --configuration Release --nologo --maxcpucount:1
    if ($LASTEXITCODE -ne 0) { throw "Die Tests sind fehlgeschlagen ($LASTEXITCODE)." }
}
dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $publishPath -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --maxcpucount:1
if ($LASTEXITCODE -ne 0) { throw "Das x64 Windows-Publish ist fehlgeschlagen ($LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/STATUS.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/FRAME-ACCURATE-EDITING.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/ENCODE-COMPARISON.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/LANGUAGES.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/BRANDING.md') -Destination $publishPath -Force
$publishedDocs=Join-Path $publishPath 'docs'
New-Item -ItemType Directory -Path $publishedDocs -Force | Out-Null
foreach($document in @('STATUS.md','FRAME-ACCURATE-EDITING.md','ENCODE-COMPARISON.md','LANGUAGES.md','BRANDING.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('docs/'+$document)) -Destination $publishedDocs -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $publishPath -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/FFmpegStudio.Desktop/Assets/PixelPeeker.png'),(Join-Path $PSScriptRoot 'src/FFmpegStudio.Desktop/Assets/PixelPeeker.ico'),(Join-Path $PSScriptRoot 'src/FFmpegStudio.Desktop/Assets/PixelPeeker-prompt.txt') -Destination $publishPath -Force
New-Item -ItemType Directory -Path $latestPublishPath -Force | Out-Null
Get-ChildItem -LiteralPath $publishPath -Force | Copy-Item -Destination $latestPublishPath -Recurse -Force
Write-Output "Portable Windows-x64-Dateien: $publishPath"
Write-Output "Aktuelle portable Windows-x64-Version: $latestPublishPath"
