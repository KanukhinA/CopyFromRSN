# Сборка icon.ico из PNG (16/32) для Inno Setup
param(
    [string]$AssetsDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets')
)

$ErrorActionPreference = 'Stop'
$icon16 = Join-Path $AssetsDir 'icon16.png'
$icon32 = Join-Path $AssetsDir 'icon32.png'
$iconOut = Join-Path $AssetsDir 'icon.ico'

if (-not (Test-Path $icon16) -or -not (Test-Path $icon32)) {
    throw "Не найдены icon16.png / icon32.png в $AssetsDir"
}

Add-Type -AssemblyName System.Drawing

function Write-IcoFromPngs([string]$path, [string[]]$pngPaths) {
    $pngBytes = foreach ($p in $pngPaths) { [IO.File]::ReadAllBytes($p) }
    $sizes = foreach ($p in $pngPaths) {
        $img = [Drawing.Image]::FromFile($p)
        try { [pscustomobject]@{ W = $img.Width; H = $img.Height } }
        finally { $img.Dispose() }
    }

    $count = $pngBytes.Length
    $headerSize = 6 + 16 * $count
    $offset = $headerSize
    $entries = @()

    for ($i = 0; $i -lt $count; $i++) {
        $w = $sizes[$i].W
        $h = $sizes[$i].H
        $widthByte = if ($w -ge 256) { [byte]0 } else { [byte]$w }
        $heightByte = if ($h -ge 256) { [byte]0 } else { [byte]$h }
        $entries += [pscustomobject]@{
            Width  = $widthByte
            Height = $heightByte
            Size   = $pngBytes[$i].Length
            Offset = $offset
        }
        $offset += $pngBytes[$i].Length
    }

    $fs = [IO.File]::Create($path)
    try {
        $bw = New-Object IO.BinaryWriter($fs)
        $bw.Write([uint16]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]$count)
        foreach ($e in $entries) {
            $bw.Write($e.Width)
            $bw.Write($e.Height)
            $bw.Write([byte]0)
            $bw.Write([byte]0)
            $bw.Write([uint16]1)
            $bw.Write([uint16]32)
            $bw.Write([uint32]$e.Size)
            $bw.Write([uint32]$e.Offset)
        }
        foreach ($bytes in $pngBytes) { $bw.Write($bytes) }
        $bw.Close()
    }
    finally { $fs.Close() }
}

Write-IcoFromPngs $iconOut @($icon16, $icon32)
Write-Host "Created $iconOut ($((Get-Item $iconOut).Length) bytes)" -ForegroundColor Green
