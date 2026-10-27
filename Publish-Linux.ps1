param(
    # Only use after the same source has already passed its relevant tests.
    [switch]$SkipTests,
    # Package only. The caller runs --smoke itself.
    [switch]$SkipSmoke
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src/FFmpegStudio.Avalonia/FFmpegStudio.Avalonia.csproj'
$filter = Join-Path $root 'FFmpegStudio.Linux.slnf'
$icon = Join-Path $root 'src/FFmpegStudio.Desktop/Assets/PixelPeeker.png'
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Directory.Build.props has no Version.' }

if (-not $SkipTests) {
    dotnet test $filter --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Die Linux-Tests sind fehlgeschlagen ($LASTEXITCODE)." }
}

function Copy-ReleaseFiles([string]$Destination) {
    foreach ($name in @('README.md', 'CHANGELOG.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $root $name) -Destination $Destination -Force
    }
    foreach ($document in @('STATUS.md', 'FRAME-ACCURATE-EDITING.md', 'ENCODE-COMPARISON.md', 'LANGUAGES.md', 'BRANDING.md')) {
        Copy-Item -LiteralPath (Join-Path $root "docs/$document") -Destination $Destination -Force
    }
    $docs = Join-Path $Destination 'docs'
    New-Item -ItemType Directory -Path $docs -Force | Out-Null
    foreach ($document in @('STATUS.md', 'FRAME-ACCURATE-EDITING.md', 'ENCODE-COMPARISON.md', 'LANGUAGES.md', 'BRANDING.md')) {
        Copy-Item -LiteralPath (Join-Path $root "docs/$document") -Destination $docs -Force
    }
    $licenseDir = Join-Path $Destination 'licenses'
    if (Test-Path -LiteralPath $licenseDir) { Remove-Item -LiteralPath $licenseDir -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $Destination -Recurse -Force
    Copy-Item -LiteralPath $icon -Destination (Join-Path $Destination 'PixelPeeker.png') -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging/linux/pixelpeeker.desktop') -Destination $Destination -Force
}

function Get-AppImageTool {
    $dir = Join-Path $root 'artifacts/tools'
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    $tool = Join-Path $dir 'appimagetool-x86_64.AppImage'
    if (-not (Test-Path -LiteralPath $tool)) {
        & curl -fsSL -o $tool 'https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage'
        if ($LASTEXITCODE -ne 0) { throw "appimagetool konnte nicht geladen werden ($LASTEXITCODE)." }
    }
    & chmod +x $tool
    return $tool
}

function New-PortableAppImage([string]$PublishDir, [string]$Output, [string]$Arch) {
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('pixelpeeker-appimage-' + [guid]::NewGuid().ToString('N'))
    $appDir = Join-Path $work 'PixelPeeker.AppDir'
    $bin = Join-Path $appDir 'usr/bin'
    New-Item -ItemType Directory -Path $bin -Force | Out-Null
    Copy-Item -Path (Join-Path $PublishDir '*') -Destination $bin -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging/linux/AppRun') -Destination (Join-Path $appDir 'AppRun') -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging/linux/pixelpeeker.desktop') -Destination (Join-Path $appDir 'pixelpeeker.desktop') -Force
    $icons = Join-Path $appDir 'usr/share/icons/hicolor/256x256/apps'
    New-Item -ItemType Directory -Path $icons -Force | Out-Null
    Copy-Item -LiteralPath $icon -Destination (Join-Path $appDir 'pixelpeeker.png') -Force
    Copy-Item -LiteralPath $icon -Destination (Join-Path $appDir '.DirIcon') -Force
    Copy-Item -LiteralPath $icon -Destination (Join-Path $icons 'pixelpeeker.png') -Force
    & chmod +x (Join-Path $appDir 'AppRun')
    & chmod +x (Join-Path $bin 'PixelPeeker.Avalonia')
    $tool = Get-AppImageTool
    $previousArch = $env:ARCH
    $previousExtract = $env:APPIMAGE_EXTRACT_AND_RUN
    try {
        $env:ARCH = if ($Arch -eq 'arm64') { 'aarch64' } else { 'x86_64' }
        $env:APPIMAGE_EXTRACT_AND_RUN = '1'
        & $tool --no-appstream $appDir $Output
        if ($LASTEXITCODE -ne 0) { throw "appimagetool für $Arch ist fehlgeschlagen ($LASTEXITCODE)." }
    }
    finally {
        $env:ARCH = $previousArch
        $env:APPIMAGE_EXTRACT_AND_RUN = $previousExtract
        if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
    }
}

function Invoke-Smoke([string]$Executable, [string]$Label) {
    $data = Join-Path ([System.IO.Path]::GetTempPath()) ('pixelpeeker-smoke-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    $previous = $env:FFMPEG_STUDIO_DATA
    $previousExtract = $env:APPIMAGE_EXTRACT_AND_RUN
    try {
        $env:FFMPEG_STUDIO_DATA = $data
        $env:APPIMAGE_EXTRACT_AND_RUN = '1'
        $log = Join-Path $data 'smoke.log'
        if (Get-Command xvfb-run -ErrorAction SilentlyContinue) {
            & xvfb-run -a --server-args='-screen 0 1280x720x24' $Executable --smoke *> $log
        }
        else {
            & $Executable --smoke *> $log
        }
        $output = Get-Content -LiteralPath $log -Raw -ErrorAction SilentlyContinue
        if ($LASTEXITCODE -ne 0 -or $output -notmatch 'PixelPeeker Avalonia smoke ready') {
            if ($output) { Write-Output $output }
            throw "$Label startet nicht mit --smoke ($LASTEXITCODE)."
        }
        Write-Output ($output.Trim())
    }
    finally {
        $env:FFMPEG_STUDIO_DATA = $previous
        $env:APPIMAGE_EXTRACT_AND_RUN = $previousExtract
        if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
    }
}

New-Item -ItemType Directory -Path (Join-Path $root 'artifacts') -Force | Out-Null
foreach ($rid in @('linux-x64', 'linux-arm64')) {
    $arch = if ($rid -eq 'linux-arm64') { 'arm64' } else { 'x64' }
    $versioned = Join-Path $root "artifacts/portable-linux-$arch-$version"
    $current = Join-Path $root "artifacts/portable-linux-$arch"
    if (Test-Path -LiteralPath $versioned) { Remove-Item -LiteralPath $versioned -Recurse -Force }
    dotnet publish $project --configuration Release --runtime $rid --self-contained true --output $versioned -p:PublishSingleFile=false -p:UseAppHost=true --nologo
    if ($LASTEXITCODE -ne 0) { throw "Das Publish für $rid ist fehlgeschlagen ($LASTEXITCODE)." }
    Copy-ReleaseFiles $versioned
    $tarName = "PixelPeeker-$version-linux-$arch.tar.gz"
    $tarPath = Join-Path $root "artifacts/$tarName"
    $currentTar = Join-Path $root "artifacts/PixelPeeker-linux-$arch.tar.gz"
    if (Test-Path -LiteralPath $tarPath) { Remove-Item -LiteralPath $tarPath -Force }
    & tar -C $versioned -czf $tarPath .
    if ($LASTEXITCODE -ne 0) { throw "tar für $arch ist fehlgeschlagen ($LASTEXITCODE)." }
    Copy-Item -LiteralPath $tarPath -Destination $currentTar -Force
    $imageName = "PixelPeeker-$version-linux-$arch.AppImage"
    $imagePath = Join-Path $root "artifacts/$imageName"
    $currentImage = Join-Path $root "artifacts/PixelPeeker-linux-$arch.AppImage"
    if (Test-Path -LiteralPath $imagePath) { Remove-Item -LiteralPath $imagePath -Force }
    New-PortableAppImage $versioned $imagePath $arch
    Copy-Item -LiteralPath $imagePath -Destination $currentImage -Force
    Copy-Item -LiteralPath $tarPath -Destination (Join-Path $versioned $tarName) -Force
    Copy-Item -LiteralPath $imagePath -Destination (Join-Path $versioned $imageName) -Force
    if (Test-Path -LiteralPath $current) { Remove-Item -LiteralPath $current -Recurse -Force }
    Copy-Item -LiteralPath $versioned -Destination $current -Recurse -Force
    foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'pixelpeeker.desktop', 'PixelPeeker.png')) {
        if (-not (Test-Path -LiteralPath (Join-Path $versioned $required))) { throw "$required fehlt in $versioned." }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $versioned 'licenses'))) { throw "licenses/ fehlt in $versioned." }
    Write-Output "Portable Linux-${arch}: $versioned"
    Write-Output "Aktuelles Linux-${arch}: $current"
    Write-Output "Archiv: $tarPath"
    Write-Output "AppImage: $imagePath"
    if (-not $SkipSmoke -and $arch -eq 'x64' -and $IsLinux) {
        $extract = Join-Path ([System.IO.Path]::GetTempPath()) ('pixelpeeker-tar-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $extract -Force | Out-Null
        try {
            & tar -xzf $tarPath -C $extract
            if ($LASTEXITCODE -ne 0) { throw "Das x64-Archiv lässt sich nicht entpacken ($LASTEXITCODE)." }
            & chmod +x (Join-Path $extract 'PixelPeeker.Avalonia')
            Invoke-Smoke (Join-Path $extract 'PixelPeeker.Avalonia') 'Das x64-Archiv'
            & chmod +x $imagePath
            Invoke-Smoke $imagePath 'Das x64-AppImage'
        }
        finally {
            if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
        }
    }
}
