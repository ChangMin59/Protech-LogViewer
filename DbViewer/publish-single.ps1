$ErrorActionPreference = "Stop"

# 현재 스크립트가 있는 폴더를 프로젝트 폴더로 사용
$ProjectDir = $PSScriptRoot
Set-Location $ProjectDir

# 프로젝트 전용 dotnet / NuGet 작업 폴더
$env:APPDATA = Join-Path $ProjectDir ".appdata"
$env:NUGET_PACKAGES = Join-Path $ProjectDir ".nuget\packages"
$env:DOTNET_CLI_HOME = Join-Path $ProjectDir ".dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

# 필요한 폴더 생성
New-Item -ItemType Directory -Force "$env:APPDATA\NuGet" | Out-Null
New-Item -ItemType Directory -Force "$env:NUGET_PACKAGES" | Out-Null
New-Item -ItemType Directory -Force "$env:DOTNET_CLI_HOME" | Out-Null

# 프로젝트 전용 NuGet.Config 생성
$NuGetConfig = Join-Path $ProjectDir "NuGet.Config"

@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
'@ | Set-Content $NuGetConfig -Encoding UTF8

# dotnet이 AppData 쪽 NuGet.Config를 찾을 때도 프로젝트 내부 경로를 보게 복사
Copy-Item $NuGetConfig "$env:APPDATA\NuGet\NuGet.Config" -Force

# win-x64 기준 복원
dotnet restore .\DbViewer.csproj --configfile .\NuGet.Config -r win-x64

# 단일 EXE publish
dotnet publish .\DbViewer.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  --no-restore `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true

Write-Host ""
Write-Host "빌드 완료" -ForegroundColor Green
Write-Host "결과 폴더:"
Write-Host "$ProjectDir\bin\Release\net10.0-windows\win-x64\publish"