$ErrorActionPreference = 'Stop'
$solutionPath = Join-Path $PSScriptRoot 'FFmpegStudio.sln'
dotnet test $solutionPath --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Die Tests sind fehlgeschlagen ($LASTEXITCODE)." }
