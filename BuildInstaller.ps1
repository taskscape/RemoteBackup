# BuildInstaller.ps1
# Script to build the project and create an Inno Setup installer

$ProjectName = "BackupService.csproj"
$PublishDir = "publish"
$InnoSetupPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# Try to extract version from csproj
[xml]$csproj = Get-Content $ProjectName
$Version = "1.0.0"
if ($csproj.Project.PropertyGroup.Version) {
    $Version = $csproj.Project.PropertyGroup.Version
}

# Override version if running in GitHub Actions
if ($env:GITHUB_RUN_NUMBER) {
    $Version = "1.0." + $env:GITHUB_RUN_NUMBER
}

Write-Host "--- 1. Cleaning up old build artifacts ---" -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
if (Test-Path "Installer\Output") { Remove-Item -Recurse -Force "Installer\Output" }

Write-Host "--- 2. Publishing project (Self-Contained, Win-x64, Version $Version) ---" -ForegroundColor Cyan
# Using -p:PublishSingleFile=true for a cleaner installation folder
dotnet publish $ProjectName -c Release -r win-x64 --self-contained true -o $PublishDir -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish failed!"
    exit $LASTEXITCODE
}

Write-Host "--- 3. Building Inno Setup Installer ---" -ForegroundColor Cyan
$ISCC = Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $ISCC -and (Test-Path $InnoSetupPath)) {
    $ISCC = $InnoSetupPath
}

if ($ISCC) {
    Write-Host "Using ISCC at: $ISCC"
    & $ISCC "/dAppVersion=$Version" "Installer\RemoteBackup.iss"
} else {
    Write-Error "Inno Setup Compiler (ISCC.exe) not found in PATH or at $InnoSetupPath."
    exit 1
}

Write-Host "--- Done! ---" -ForegroundColor Green
if (Test-Path "Installer\Output\RemoteBackupSetup.exe") {
    Write-Host "Installer created: Installer\Output\RemoteBackupSetup.exe" -ForegroundColor Green
    Write-Host "App Version: $Version" -ForegroundColor Green
} else {
    Write-Error "Installer file was not created!"
    exit 1
}

