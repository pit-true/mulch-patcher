$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MulchIconHandle {
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
}
'@ -ErrorAction SilentlyContinue
$projectDirectory = Split-Path -Parent $PSScriptRoot
$objectDirectory = Join-Path $projectDirectory 'obj'
New-Item -ItemType Directory -Path $objectDirectory -Force | Out-Null
$bitmap = New-Object Drawing.Bitmap 64,64
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$background = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(34,104,72))
$accent = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(190,229,130))
$line = New-Object Drawing.Pen ([Drawing.Color]::White),6
$line.StartCap = [Drawing.Drawing2D.LineCap]::Round
$line.EndCap = [Drawing.Drawing2D.LineCap]::Round
$handle = [IntPtr]::Zero
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.FillEllipse($background, 2,2,60,60)
    $graphics.DrawLines($line, [Drawing.PointF[]]@(
        (New-Object Drawing.PointF 17,44),(New-Object Drawing.PointF 17,22),
        (New-Object Drawing.PointF 32,36),(New-Object Drawing.PointF 47,22),(New-Object Drawing.PointF 47,44)))
    $graphics.FillEllipse($accent, 43,8,12,12)
    $handle = $bitmap.GetHicon()
    $icon = [Drawing.Icon]::FromHandle($handle)
    $stream = [IO.File]::Create((Join-Path $objectDirectory 'MulchPatcher.ico'))
    try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose() }
}
finally {
    if ($handle -ne [IntPtr]::Zero) { [void][MulchIconHandle]::DestroyIcon($handle) }
    $line.Dispose(); $accent.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
