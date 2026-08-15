param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [ValidatePattern('^[a-z0-9-]+$')]
    [string]$AssetName = "cuepilot",

    [string]$BrandingDirectory = (Join-Path $PSScriptRoot "..\assets\branding"),

    [string]$UiPublicDirectory = (Join-Path $PSScriptRoot "..\ui\public")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing

function New-CuePilotIconPng {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap]$SourceBitmap,

        [Parameter(Mandatory = $true)]
        [int]$Size
    )

    $cropSize = [Math]::Min(960, [Math]::Min($SourceBitmap.Width, $SourceBitmap.Height))
    $sourceX = [int](($SourceBitmap.Width - $cropSize) / 2)
    $sourceY = [int](($SourceBitmap.Height - $cropSize) / 2)
    $cornerRadius = [Math]::Max(2, [int][Math]::Round($Size * 0.18))

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $clip = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $stream = [System.IO.MemoryStream]::new()

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

        $diameter = $cornerRadius * 2
        $edge = $Size - 1
        $clip.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $clip.AddArc($edge - $diameter, 0, $diameter, $diameter, 270, 90)
        $clip.AddArc($edge - $diameter, $edge - $diameter, $diameter, $diameter, 0, 90)
        $clip.AddArc(0, $edge - $diameter, $diameter, $diameter, 90, 90)
        $clip.CloseFigure()
        $graphics.SetClip($clip)

        $destination = [System.Drawing.Rectangle]::new(0, 0, $Size, $Size)
        $graphics.DrawImage(
            $SourceBitmap,
            $destination,
            $sourceX,
            $sourceY,
            $cropSize,
            $cropSize,
            [System.Drawing.GraphicsUnit]::Pixel
        )

        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $clip.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-MultiSizeIcon {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$PngBySize,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $sizes = @($PngBySize.Keys | Sort-Object)
    $stream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($stream)

    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        foreach ($size in $sizes) {
            [byte[]]$png = $PngBySize[$size]
            $dimension = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$png.Length)
            $writer.Write([uint32]$offset)
            $offset += $png.Length
        }

        foreach ($size in $sizes) {
            [byte[]]$png = $PngBySize[$size]
            $writer.Write($png)
        }

        [System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$brandingPath = [System.IO.Path]::GetFullPath($BrandingDirectory)
$uiPublicPath = [System.IO.Path]::GetFullPath($UiPublicDirectory)

[System.IO.Directory]::CreateDirectory($brandingPath) | Out-Null
[System.IO.Directory]::CreateDirectory($uiPublicPath) | Out-Null

$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)
$sourceBitmap = [System.Drawing.Bitmap]::new($sourceImage)

try {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $pngBySize = @{}
    foreach ($size in $sizes) {
        $pngBySize[$size] = [byte[]](New-CuePilotIconPng -SourceBitmap $sourceBitmap -Size $size)
    }

    [byte[]]$applicationPng = New-CuePilotIconPng -SourceBitmap $sourceBitmap -Size 512
    [System.IO.File]::WriteAllBytes(
        (Join-Path $brandingPath "$AssetName-icon.png"),
        $applicationPng
    )
    [System.IO.File]::WriteAllBytes(
        (Join-Path $uiPublicPath "$AssetName-icon.png"),
        $pngBySize[128]
    )
    Write-MultiSizeIcon -PngBySize $pngBySize -Path (Join-Path $brandingPath "$AssetName.ico")
}
finally {
    $sourceBitmap.Dispose()
    $sourceImage.Dispose()
}

Write-Host "$AssetName brand assets generated from $sourcePath"
