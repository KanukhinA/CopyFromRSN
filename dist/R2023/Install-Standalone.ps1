\xef\xbb\xbf$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2023"
# Переустановка: сначала убираем старые файлы плагина в этой версии Revit
$names = @('SP_CopyFromRSN.dll', 'SP_CopyFromRSN.addin', 'SP_CopyFromRSN.deps.json', 'SP_CopyFromRSN.pdb', 'CommunityToolkit.Mvvm.dll', 'System.Text.Encoding.CodePages.dll', 'System.Text.Json.dll')
if (Test-Path $dest) {
    foreach ($n in $names) {
        $p = Join-Path $dest $n
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }
    # Папку SP_CopyFromRSN с конфигами профилей (Configs, active-profile.txt) не трогаем —
    # при обновлении версии настройки пользователя должны сохраняться.
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src '*') $dest -Recurse -Force -Exclude 'Install-Standalone.ps1'
Write-Host "Установлено в $dest"
Write-Host "Перезапустите Revit для загрузки плагина."
