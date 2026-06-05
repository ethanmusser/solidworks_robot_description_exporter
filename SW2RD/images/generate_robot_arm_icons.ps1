Add-Type -AssemblyName System.Drawing

# Generates the robot_arm_<size>.png add-in icon set (the add-in's
# representative logo, replacing the former ROS logo) by high-quality
# downscaling the master robot_arm_source.png. The master is the Flaticon
# "Robotic arm" (Special Lineal color) icon #1839269 by Freepik; see the
# attribution in the add-in's About box (SW2RD/UI/AboutForm.cs).
#
# Also (re)writes SW2RD.png, the project's loose representative PNG, from the
# same source so the repo logo matches the add-in icon everywhere.

$imagesDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcPath = Join-Path $imagesDir "robot_arm_source.png"
$sizes = @(16, 20, 32, 40, 64, 96, 128)

$src = [System.Drawing.Image]::FromFile($srcPath)

function Save-Resized {
    param($src, $size, $outPath)
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
    $g.Dispose()
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

foreach ($s in $sizes) {
    $outPath = Join-Path $imagesDir ("robot_arm_{0}x{0}.png" -f $s)
    Save-Resized $src $s $outPath
    Write-Host "Wrote $outPath ($s x $s)"
}

# Project-root representative PNG (loose content next to the DLL).
$swrdPng = Join-Path (Split-Path -Parent $imagesDir) "SW2RD.png"
Save-Resized $src 64 $swrdPng
Write-Host "Wrote $swrdPng (64 x 64)"

$src.Dispose()
