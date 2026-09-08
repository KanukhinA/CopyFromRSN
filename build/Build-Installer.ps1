# Сборка дистрибутива SP_CopyFromRSN
param(
    [switch]$SkipBuild,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$configs = @('R2022', 'R2023', 'R2025', 'R2026')

function Get-AssemblyVersion {
    $info = Join-Path $root 'AssemblyVersionInfo.cs'
    $content = Get-Content $info -Raw
    if ($content -match 'AssemblyVersion\("([^"]+)"\)') { return $Matches[1] }
    return '1.0.0'
}

if (-not $SkipBuild) {
    foreach ($cfg in $configs) {
        Write-Host "Building $cfg..." -ForegroundColor Cyan
        dotnet build (Join-Path $root 'SP_CopyFromRSN.csproj') -c $cfg
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $cfg" }
    }
}

foreach ($cfg in $configs) {
    $year = $cfg.Substring(1)
    $src = Join-Path $root "bin\$cfg\net48"
    if ($cfg -in 'R2025', 'R2026') { $src = Join-Path $root "bin\$cfg\net8.0-windows" }
    $dest = Join-Path $dist $cfg
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item (Join-Path $src '*') $dest -Recurse -Force

    $ps1 = @"
`$ErrorActionPreference = 'Stop'
`$src = `$PSScriptRoot
`$dest = Join-Path `$env:APPDATA "Autodesk\Revit\Addins\$year"
# Переустановка: сначала убираем старые файлы плагина в этой версии Revit
`$names = @('SP_CopyFromRSN.dll', 'SP_CopyFromRSN.addin', 'SP_CopyFromRSN.deps.json', 'SP_CopyFromRSN.pdb', 'CommunityToolkit.Mvvm.dll', 'System.Text.Json.dll')
if (Test-Path `$dest) {
    foreach (`$n in `$names) {
        `$p = Join-Path `$dest `$n
        if (Test-Path `$p) { Remove-Item `$p -Force -ErrorAction SilentlyContinue }
    }
    Get-ChildItem `$dest -Filter 'SP_CopyFromRSN*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path `$dest | Out-Null
Copy-Item (Join-Path `$src '*') `$dest -Recurse -Force -Exclude 'Install-Standalone.ps1'
Write-Host "Установлено в `$dest"
Write-Host "Перезапустите Revit для загрузки плагина."
"@
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText((Join-Path $dest 'Install-Standalone.ps1'), $ps1, $utf8Bom)
}

$version = Get-AssemblyVersion
$iconScript = Join-Path $PSScriptRoot 'Update-IconIco.ps1'
if (Test-Path $iconScript) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $iconScript
}

$icon = Join-Path $root 'Assets\icon.ico'
$templatePath = Join-Path $root 'InnoSetup\SP_CopyFromRSN.template.iss'
$issPath = Join-Path $root 'InnoSetup\SP_CopyFromRSN.generated.iss'

if (-not (Test-Path $templatePath)) {
    throw "Не найден шаблон: $templatePath"
}

$filesSection = @()
if (Test-Path $icon) {
    Copy-Item $icon (Join-Path $dist 'icon.ico') -Force
    $filesSection += "Source: `"$icon`"; DestDir: `"{app}`"; Flags: ignoreversion"
}
foreach ($cfg in $configs) {
    $year = $cfg.Substring(1)
    $component = "revit$year"
    $filesSection += "Source: `"$dist\$cfg\*`"; DestDir: `"{userappdata}\Autodesk\Revit\Addins\$year`"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: $component; Excludes: `"Install-Standalone.ps1`""
}

$iconLines = ''
if (Test-Path $icon) {
    $iconLines = "SetupIconFile=$icon`r`nUninstallDisplayIcon={app}\icon.ico"
}

# Русский текст — только в template.iss (UTF-8), здесь подставляем пути и версию
$iss = [System.IO.File]::ReadAllText($templatePath, [System.Text.UTF8Encoding]::new($false))
$iss = $iss.Replace('{{VERSION}}', $version)
$iss = $iss.Replace('{{OUTPUT_DIR}}', $dist)
$iss = $iss.Replace('{{ICON_LINES}}', $iconLines)
$iss = $iss.Replace('{{FILES_SECTION}}', ($filesSection -join "`r`n"))

New-Item -ItemType Directory -Force -Path (Join-Path $root 'InnoSetup') | Out-Null
$utf8WithBom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($issPath, $iss, $utf8WithBom)
Write-Host "Generated $issPath" -ForegroundColor Green

if ($SkipInstaller) { return }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "ISCC.exe не найден. Установите Inno Setup 6 или запустите: ISCC.exe `"$issPath`""
    return
}

& $iscc $issPath
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Write-Host "Installer: $dist\SP_CopyFromRSN-$version-Setup.exe" -ForegroundColor Green
