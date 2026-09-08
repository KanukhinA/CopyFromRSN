$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dest = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2022"
# РџРµСЂРµСѓСЃС‚Р°РЅРѕРІРєР°: СЃРЅР°С‡Р°Р»Р° СѓР±РёСЂР°РµРј СЃС‚Р°СЂС‹Рµ С„Р°Р№Р»С‹ РїР»Р°РіРёРЅР° РІ СЌС‚РѕР№ РІРµСЂСЃРёРё Revit
$names = @('SP_CopyFromRSN.dll', 'SP_CopyFromRSN.addin', 'SP_CopyFromRSN.deps.json', 'SP_CopyFromRSN.pdb', 'CommunityToolkit.Mvvm.dll', 'System.Text.Json.dll')
if (Test-Path $dest) {
    foreach ($n in $names) {
        $p = Join-Path $dest $n
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }
    Get-ChildItem $dest -Filter 'SP_CopyFromRSN*' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src '*') $dest -Recurse -Force -Exclude 'Install-Standalone.ps1'
Write-Host "РЈСЃС‚Р°РЅРѕРІР»РµРЅРѕ РІ $dest"
Write-Host "РџРµСЂРµР·Р°РїСѓСЃС‚РёС‚Рµ Revit РґР»СЏ Р·Р°РіСЂСѓР·РєРё РїР»Р°РіРёРЅР°."