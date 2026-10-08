# Generates package/icon.png (256x256): two interlocked pairing rings (cyan=supply / orange=demand).
# Usage (execution policy friendly, via stdin):
#   cat tools/make-icon.ps1 | powershell -NoProfile -Command -
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# Locate project dir without hardcoding non-ASCII path:
# E:\Mod build\<chinese-name>\StationFlow  -> resolve via child folders under E:\Mod build
$modBuild = "E:\Mod build"
$sfDir = $null
foreach ($d in (Get-ChildItem $modBuild -Directory)) {
    $cand = Join-Path $d.FullName "StationFlow"
    if (Test-Path (Join-Path $cand "StationFlow.csproj")) { $sfDir = $cand; break }
}
if (-not $sfDir) { throw "StationFlow.csproj not found under $modBuild" }
$outPath = Join-Path $sfDir "package\icon.png"

$size = 256
$bmp = New-Object System.Drawing.Bitmap($size, $size)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Background: deep blue diagonal gradient (matches panel ColBg palette)
$rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
$c1 = [System.Drawing.Color]::FromArgb(255, 8, 20, 32)
$c2 = [System.Drawing.Color]::FromArgb(255, 18, 52, 66)
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 55)
$g.FillRectangle($brush, $rect)
$brush.Dispose()

# Tiny star dots
$starBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 140, 200, 220))
foreach ($p in @(@(34, 36), @(214, 48), @(48, 208), @(198, 200), @(230, 128), @(26, 118))) {
    $g.FillEllipse($starBrush, $p[0], $p[1], 3, 3)
}
$starBrush.Dispose()

# Two interlocked rings (supply / demand pairing)
$cya = [System.Drawing.Color]::FromArgb(255, 115, 217, 230)
$org = [System.Drawing.Color]::FromArgb(255, 255, 184, 89)

# soft glow
$glowC = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(45, 115, 217, 230), 22)
$glowO = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(45, 255, 184, 89), 22)
$g.DrawEllipse($glowC, 24, 46, 130, 130)
$g.DrawEllipse($glowO, 102, 46, 130, 130)
$glowC.Dispose(); $glowO.Dispose()

$penC = New-Object System.Drawing.Pen($cya, 9)
$penO = New-Object System.Drawing.Pen($org, 9)
$g.DrawEllipse($penC, 24, 46, 130, 130)
$g.DrawEllipse($penO, 102, 46, 130, 130)
$penC.Dispose(); $penO.Dispose()

# Nodes (stations) on the rings
$nodeW = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$g.FillEllipse($nodeW, 82, 40, 14, 14)   # node on left ring
$g.FillEllipse($nodeW, 160, 168, 14, 14) # node on right ring
$nodeW.Dispose()
$coreC = New-Object System.Drawing.SolidBrush($cya)
$g.FillEllipse($coreC, 86, 44, 6, 6)
$g.FillEllipse($coreC, 164, 172, 6, 6)
$coreC.Dispose()

# Bottom wordmark
$font = New-Object System.Drawing.Font("Segoe UI", 21, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 220, 235, 240))
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$textRect = New-Object System.Drawing.RectangleF(0, 198, $size, 40)
$g.DrawString("StationFlow", $font, $textBrush, $textRect, $fmt)
$font.Dispose(); $textBrush.Dispose()

$g.Dispose()
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host "[StationFlow] icon generated: $outPath"
