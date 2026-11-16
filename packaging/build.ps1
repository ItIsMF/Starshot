[CmdletBinding()]
param(
    [string]$Version = '2.6.5-hdr.1',
    [string]$PlatformToolset = 'v143',
    [string]$MSBuildPath,
    [string]$IsccPath,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$buildRoot = Join-Path $repo 'build'
$packageRoot = Join-Path $buildRoot 'package'
$artifacts = Join-Path $buildRoot 'artifacts'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw 'Version must be a version such as 2.6.5-hdr.1.'
}
if (-not $IsccPath) {
    $IsccPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Pass -IsccPath with the path to the Inno Setup compiler.'
}
if (-not $MSBuildPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe'
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'Visual C++ build tools not found.' }

# Only this script's staging directory is replaced. Reject links before recursive deletion.
if (Test-Path -LiteralPath $packageRoot) {
    $resolved = (Resolve-Path -LiteralPath $packageRoot).Path
    $expected = [IO.Path]::GetFullPath((Join-Path $repo 'build\package'))
    $links = @(Get-Item -LiteralPath $buildRoot, $resolved; Get-ChildItem -LiteralPath $resolved -Recurse -Force) |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }
    if ($resolved -ne $expected -or $links) { throw 'Unsafe staging directory; inspect build/package.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$portable = Join-Path $packageRoot 'portable'
$installer = Join-Path $packageRoot 'installer'
New-Item -ItemType Directory -Force -Path $portable, $installer, $artifacts | Out-Null

Push-Location $repo
try {
    & $MSBuildPath src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64 "-p:PlatformToolset=$PlatformToolset" -m:1 -nr:false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    $publishArgs = @('publish', 'src/Starshot/Starshot.csproj', '-c', 'Release', '-p:Platform=x64',
        '-p:RuntimeIdentifier=win-x64', "-p:Version=$Version", '-p:MSBuildEnableWorkloadResolver=false',
        '-m:1', '-nr:false', '-v:minimal', '-o', (Join-Path $portable 'app'))
    if ($NoRestore) { $publishArgs += '--no-restore' }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    Copy-Item -LiteralPath (Join-Path $buildRoot 'Starshot.exe') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $portable
    Copy-Item -LiteralPath (Join-Path $repo 'packaging\README.md') -Destination (Join-Path $portable 'HDR-Fork-README.md')
    Get-ChildItem -LiteralPath $portable -Recurse -Filter '*.pdb' | Remove-Item -Force

    $zip = Join-Path $artifacts "Starshot-HDR-Fork-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -Force
    Copy-Item -Path (Join-Path $portable '*') -Destination $installer -Recurse
    # The installed app reads this existing DOS-stub marker to choose its isolated LocalAppData folder.
    $appExe = Join-Path $installer 'app\Starshot.exe'
    $bytes = [IO.File]::ReadAllBytes($appExe)
    [Text.Encoding]::ASCII.GetBytes('STARSHOT-INSTALLER').CopyTo($bytes, 0x40)
    [IO.File]::WriteAllBytes($appExe, $bytes)
    & $IsccPath /Qp "/DAppVersion=$Version" "/DPayloadDir=$installer" "/DOutputDir=$artifacts" packaging/Starshot.HDRFork.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setup = Join-Path $artifacts "Starshot-HDR-Fork-$Version-win-x64-setup.exe"
    Get-FileHash -Algorithm SHA256 -LiteralPath $zip, $setup |
        ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
        Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Artifacts: $artifacts"
}
finally { Pop-Location }
