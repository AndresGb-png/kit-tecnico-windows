# Compila ChequeadorPC.exe con el compilador que ya trae Windows (.NET Framework 4).
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$out = Join-Path (Split-Path $src -Parent) 'ChequeadorPC.exe'
$icon = Join-Path $src 'app.ico'

if (-not (Test-Path $icon)) {
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object Drawing.Bitmap 256, 256
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([Drawing.Color]::Transparent)
    $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(0x1B, 0x7F, 0x4B))), 8, 8, 240, 240)
    $pen = New-Object Drawing.Pen ([Drawing.Color]::White), 30
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [Drawing.Point[]]@((New-Object Drawing.Point 66, 134), (New-Object Drawing.Point 110, 178), (New-Object Drawing.Point 190, 86)))
    $g.Dispose()
    $png = New-Object IO.MemoryStream
    $bmp.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $png.ToArray()
    $fs = [IO.File]::Create($icon)
    $w = New-Object IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$bytes.Length); $w.Write([uint32]22)
    $w.Write($bytes)
    $w.Close()
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /unsafe /codepage:65001 `
    "/out:$out" "/win32manifest:$src\app.manifest" "/win32icon:$icon" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Xml.dll `
    "$src\Core.cs" "$src\Native.cs" "$src\Probes.cs" "$src\Report.cs" "$src\MainForm.cs" "$src\Tests.cs" "$src\Program.cs" "$src\..\..\comun\Credits.cs"
if ($LASTEXITCODE -ne 0) { throw "La compilación falló" }
Get-Item $out | Select-Object Name, Length
