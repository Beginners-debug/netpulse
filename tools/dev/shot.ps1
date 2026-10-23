# 截图工具：dot-source 或直接调用
# 用法: powershell -File tools/shot.ps1 -Name panel -X 1500 -Y 480 -W 420 -H 600 [-Zoom 2]
param(
    [string]$Name = "shot",
    [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0,
    [int]$Zoom = 1,
    [string]$Dir = "shots"
)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
$full = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
$g = [System.Drawing.Graphics]::FromImage($full)
$g.CopyFromScreen($vs.Left, $vs.Top, 0, 0, $full.Size)
$g.Dispose()

if ($W -le 0) { $W = $vs.Width; $X = 0 }
if ($H -le 0) { $H = $vs.Height; $Y = 0 }
if ($X -lt 0) { $X = 0 }
if ($Y -lt 0) { $Y = 0 }

$rect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
$crop = $full.Clone($rect, $full.PixelFormat)
$full.Dispose()

$out = $crop
if ($Zoom -gt 1) {
    $out = New-Object System.Drawing.Bitmap ($W * $Zoom), ($H * $Zoom)
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g2.DrawImage($crop, 0, 0, $out.Width, $out.Height)
    $g2.Dispose()
    $crop.Dispose()
}

if (-not (Test-Path $Dir)) { New-Item -ItemType Directory -Path $Dir | Out-Null }
$path = Join-Path $Dir "$Name.png"
$out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$out.Dispose()
Write-Output $path
