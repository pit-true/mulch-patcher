param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($msbuildCommand) { $msbuildPath = $msbuildCommand.Source }
else {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswherePath)) { throw 'Install Visual Studio Build Tools with .NET desktop build tools and the .NET Framework 4.8 targeting pack.' }
    $msbuildPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (!$msbuildPath) { throw 'MSBuild was not found.' }
& (Join-Path $PSScriptRoot 'scripts\BuildIcon.ps1')
& $msbuildPath MulchPatcher.csproj /t:Rebuild /p:Configuration=Release /p:ApplicationIcon=obj\MulchPatcher.ico /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
if (!$SkipTests) {
    & $msbuildPath tests\MulchPatcher.Tests.csproj /t:Rebuild /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & .\tests\bin\Release\MulchPatcher.Tests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
}
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Copy-Item -LiteralPath 'bin\Release\MulchPatcher.exe' -Destination $dist -Force
Write-Output ('Release files: ' + $dist)
