Add-Type -AssemblyName System.Drawing

# Regenerates the sw2rd_toolbar_<size>.png sprite strips used by the
# CommandGroup.IconList. Each strip is a horizontal row of THREE sub-icons,
# all <size> wide:
#   sub-index 0 = robot arm           (Configure) - the add-in logo
#   sub-index 1 = trash-can glyph     (Clear Configuration) - drawn RED to
#                                       signal a destructive action
#   sub-index 2 = export tray + arrow (Export) - drawn GREEN to signal the
#                                       intended, low-consequence action
# AddCommandItem2's imageListIndex selects which sub-icon a command renders.
# The robot arm is composited from robot_arm_<size>.png (the Flaticon icon
# #1839269 by Freepik - see the About box for attribution); the trash and
# export glyphs are drawn vectorially so they stay crisp at every size.

$imagesDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = @(20, 32, 40, 64, 96, 128)
# Destructive (Clear) = red; affirmative (Export) = green.
$trashColor = [System.Drawing.Color]::FromArgb(255, 192, 47, 47)
$exportColor = [System.Drawing.Color]::FromArgb(255, 38, 140, 60)

function Draw-Trash {
    param($g, $ox, $s, $color)

    $penW = [Math]::Max(1.0, $s * 0.06)
    $pen = New-Object System.Drawing.Pen($color, $penW)
    $brush = New-Object System.Drawing.SolidBrush($color)

    $pad = $s * 0.20
    $left = $ox + $pad
    $right = $ox + $s - $pad
    $w = $right - $left

    # Lid (filled bar) just below the handle.
    $lidY = $oy + ($s * 0.30)
    $lidH = [Math]::Max(1.0, $s * 0.09)
    $g.FillRectangle($brush, [single]$left, [single]$lidY, [single]$w, [single]$lidH)

    # Handle (small bar centered on top of the lid).
    $handleW = $w * 0.36
    $handleH = [Math]::Max(1.0, $s * 0.07)
    $handleX = $ox + ($s * 0.5) - ($handleW * 0.5)
    $handleY = $lidY - $handleH
    $g.FillRectangle($brush, [single]$handleX, [single]$handleY, [single]$handleW, [single]$handleH)

    # Body (outlined can) tapering slightly inward.
    $bodyTop = $lidY + $lidH + ($s * 0.03)
    $bodyBottom = $oy + $s - $pad
    $bodyInset = $s * 0.04
    $blX = $left + $bodyInset
    $brX = $right - $bodyInset
    $g.DrawLine($pen, [single]($left + $bodyInset), [single]$bodyTop, [single]($left + ($s * 0.07)), [single]$bodyBottom)
    $g.DrawLine($pen, [single]($right - $bodyInset), [single]$bodyTop, [single]($right - ($s * 0.07)), [single]$bodyBottom)
    $g.DrawLine($pen, [single]($left + ($s * 0.07)), [single]$bodyBottom, [single]($right - ($s * 0.07)), [single]$bodyBottom)

    # Two vertical ribs inside the body.
    $ribTop = $bodyTop + ($s * 0.04)
    $ribBottom = $bodyBottom - ($s * 0.04)
    $ribX1 = $ox + ($s * 0.42)
    $ribX2 = $ox + ($s * 0.58)
    $g.DrawLine($pen, [single]$ribX1, [single]$ribTop, [single]$ribX1, [single]$ribBottom)
    $g.DrawLine($pen, [single]$ribX2, [single]$ribTop, [single]$ribX2, [single]$ribBottom)

    $pen.Dispose()
    $brush.Dispose()
}

function Draw-Export {
    param($g, $ox, $s, $color)

    $penW = [Math]::Max(1.0, $s * 0.07)
    $pen = New-Object System.Drawing.Pen($color, $penW)
    $brush = New-Object System.Drawing.SolidBrush($color)

    $cx = $ox + ($s * 0.5)

    # Upward arrow (shaft + filled head) = "send / out".
    $headTop = $oy + ($s * 0.14)
    $headBottom = $oy + ($s * 0.40)
    $headHalf = $s * 0.15
    $pts = @(
        (New-Object System.Drawing.PointF([single]$cx, [single]$headTop)),
        (New-Object System.Drawing.PointF([single]($cx - $headHalf), [single]$headBottom)),
        (New-Object System.Drawing.PointF([single]($cx + $headHalf), [single]$headBottom))
    )
    $g.FillPolygon($brush, $pts)

    $shaftTop = $headBottom
    $shaftBottom = $oy + ($s * 0.62)
    $g.DrawLine($pen, [single]$cx, [single]$shaftTop, [single]$cx, [single]$shaftBottom)

    # Open tray / outbox beneath the arrow (U-shape, open top).
    $pad = $s * 0.20
    $trayLeft = $ox + $pad
    $trayRight = $ox + $s - $pad
    $trayTop = $oy + ($s * 0.58)
    $trayBottom = $oy + $s - ($s * 0.16)
    $shoulder = $s * 0.12
    $g.DrawLine($pen, [single]$trayLeft, [single]$trayTop, [single]$trayLeft, [single]$trayBottom)
    $g.DrawLine($pen, [single]$trayRight, [single]$trayTop, [single]$trayRight, [single]$trayBottom)
    $g.DrawLine($pen, [single]$trayLeft, [single]$trayBottom, [single]$trayRight, [single]$trayBottom)

    $pen.Dispose()
    $brush.Dispose()
}

foreach ($s in $sizes) {
    $oy = 0
    $width = $s * 3
    $bmp = New-Object System.Drawing.Bitmap($width, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # Cell 0: robot arm from the add-in logo single-icon file.
    $logoPath = Join-Path $imagesDir ("robot_arm_{0}x{0}.png" -f $s)
    if (Test-Path $logoPath) {
        $logo = [System.Drawing.Image]::FromFile($logoPath)
        $g.DrawImage($logo, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)))
        $logo.Dispose()
    }

    # Cell 1: trash can (red = destructive).
    Draw-Trash $g $s $s $trashColor

    # Cell 2: export tray + arrow (green = affirmative).
    Draw-Export $g ($s * 2) $s $exportColor

    $g.Dispose()
    $outPath = Join-Path $imagesDir ("sw2rd_toolbar_{0}x{0}.png" -f $s)
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $outPath ($width x $s)"
}
