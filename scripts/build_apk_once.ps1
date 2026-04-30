param(
  [string]$ProjectPath = '.\PulseMonitor\PulseMonitor.csproj',
  [string]$Configuration = 'Release',
  [string]$TargetFramework = 'net8.0-android',
  [string]$AndroidSdkDirectory,
  [switch]$InstallToDevice,
  [string]$DeviceId = 'emulator-5556',
  [switch]$SkipPreflight
)

$ErrorActionPreference = 'Stop'

function Resolve-AndroidSdkPath {
  param([string]$InputPath)

  $candidates = @()

  if ($InputPath) { $candidates += $InputPath }
  if ($env:ANDROID_SDK_ROOT) { $candidates += $env:ANDROID_SDK_ROOT }
  if ($env:ANDROID_HOME) { $candidates += $env:ANDROID_HOME }
  $candidates += 'C:\Users\ADMIN\AppData\Local\Android\Sdk'
  $candidates += 'C:\Android\sdk'

  foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path $candidate)) {
      return (Resolve-Path $candidate).Path
    }
  }

  return $null
}

$projectFullPath = (Resolve-Path $ProjectPath).Path
$projectDir = Split-Path $projectFullPath -Parent
$projectName = Split-Path $projectDir -Leaf

$resolvedSdk = Resolve-AndroidSdkPath -InputPath $AndroidSdkDirectory
if (-not $resolvedSdk) {
  throw 'Android SDK path not found. Set ANDROID_SDK_ROOT or pass -AndroidSdkDirectory.'
}

if (-not $SkipPreflight) {
  & (Join-Path $PSScriptRoot 'android_preflight.ps1') -AndroidSdkDirectory $resolvedSdk
}

Write-Host '=== One-shot APK Build ===' -ForegroundColor Cyan
Write-Host "Project: $projectFullPath"
Write-Host "Framework: $TargetFramework"
Write-Host "Configuration: $Configuration"
Write-Host "Android SDK: $resolvedSdk"

$targetBin = Join-Path $projectDir "bin\$Configuration\$TargetFramework"
$targetObj = Join-Path $projectDir "obj\$Configuration\$TargetFramework"

if (Test-Path $targetBin) {
  Remove-Item -Recurse -Force $targetBin
}
if (Test-Path $targetObj) {
  Remove-Item -Recurse -Force $targetObj
}

$publishArgs = @(
  'publish', $projectFullPath,
  '-f', $TargetFramework,
  '-c', $Configuration,
  "-p:AndroidSdkDirectory=$resolvedSdk",
  '-p:EmbedAssembliesIntoApk=true',
  '-p:AndroidUseSharedRuntime=false',
  '-p:AndroidPackageFormat=apk'
)

$maxAttempts = 2
$attempt = 0
$buildSucceeded = $false

while (-not $buildSucceeded -and $attempt -lt $maxAttempts) {
  $attempt++
  Write-Host "Build attempt $attempt/$maxAttempts..." -ForegroundColor Yellow

  & dotnet @publishArgs

  if ($LASTEXITCODE -eq 0) {
    $buildSucceeded = $true
    break
  }

  if ($attempt -lt $maxAttempts) {
    Write-Warning 'Build failed, cleaning output and retrying once.'
    if (Test-Path $targetBin) { Remove-Item -Recurse -Force $targetBin }
    if (Test-Path $targetObj) { Remove-Item -Recurse -Force $targetObj }
  }
}

if (-not $buildSucceeded) {
  throw 'APK build failed after retry.'
}

$apkCandidates = Get-ChildItem -Path $targetBin -Recurse -File -Filter '*Signed.apk' -ErrorAction SilentlyContinue
if (-not $apkCandidates) {
  $apkCandidates = Get-ChildItem -Path $targetBin -Recurse -File -Filter '*.apk' -ErrorAction SilentlyContinue
}

if (-not $apkCandidates) {
  throw 'No APK artifact found after build.'
}

$apk = $apkCandidates | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "APK ready: $($apk.FullName)" -ForegroundColor Green

if ($InstallToDevice) {
  $adbPath = Join-Path $resolvedSdk 'platform-tools\adb.exe'

  & $adbPath devices | Out-String | Write-Host
  & $adbPath -s $DeviceId install -r $apk.FullName

  if ($LASTEXITCODE -ne 0) {
    throw 'APK installation failed.'
  }

  & $adbPath -s $DeviceId shell monkey -p com.medtech.pulsemonitor -c android.intent.category.LAUNCHER 1 | Out-Null
  Write-Host "Installed and launched on $DeviceId" -ForegroundColor Green
}

Write-Host 'One-shot APK build completed successfully.' -ForegroundColor Green
