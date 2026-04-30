param(
  [string]$AndroidSdkDirectory
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

Write-Host '=== Android Preflight ===' -ForegroundColor Cyan

$dotnetVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
  Write-Error 'dotnet SDK is not available.'
}
Write-Host "dotnet: $dotnetVersion" -ForegroundColor Green

$resolvedSdk = Resolve-AndroidSdkPath -InputPath $AndroidSdkDirectory
if (-not $resolvedSdk) {
  Write-Error 'Android SDK path not found. Set ANDROID_SDK_ROOT or pass -AndroidSdkDirectory.'
}

$adbPath = Join-Path $resolvedSdk 'platform-tools\adb.exe'
if (-not (Test-Path $adbPath)) {
  Write-Error "adb not found at $adbPath"
}

$workloads = dotnet workload list | Out-String
if ($workloads -notmatch 'maui' -and $workloads -notmatch 'android') {
  Write-Error 'Required .NET MAUI/Android workload is not installed.'
}

$devices = & $adbPath devices | Out-String
Write-Host 'adb devices:' -ForegroundColor Green
Write-Host $devices

if ($devices -notmatch '\tdevice') {
  Write-Warning 'No online device/emulator detected. Build can proceed, deployment may fail.'
}

Write-Host "Android SDK: $resolvedSdk" -ForegroundColor Green
Write-Host 'Preflight passed.' -ForegroundColor Green
