$ErrorActionPreference = "Stop"

$Root = $PSScriptRoot
$FFmpeg = Join-Path $Root "ffmpeg.exe"
$Log = Join-Path $Root "ogg_conversion_errors.txt"

Write-Host "=========================================="
Write-Host "       OGG Stereo -> Mono converter"
Write-Host "=========================================="
Write-Host ""
Write-Host "Folder: $Root"
Write-Host ""

# Find FFmpeg
if (-not (Test-Path -LiteralPath $FFmpeg)) {
    $Command = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue

    if ($Command) {
        $FFmpeg = $Command.Source
    }
    else {
        Write-Host "ERROR: ffmpeg.exe was not found!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Put ffmpeg.exe next to this script."
        Read-Host "Press Enter to exit"
        exit 1
    }
}

Write-Host "FFmpeg: $FFmpeg"
Write-Host ""

# Remove old log
if (Test-Path -LiteralPath $Log) {
    Remove-Item -LiteralPath $Log -Force
}

# Find all OGG files recursively
$Files = @(
    Get-ChildItem -LiteralPath $Root -Filter "*.ogg" -File -Recurse |
    Where-Object { $_.Name -notlike "*.mono.ogg" }
)

$Total = $Files.Count

if ($Total -eq 0) {
    Write-Host "No OGG files found." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 0
}

Write-Host "Found files: $Total"
Write-Host ""

$Success = 0
$Errors = 0
$Number = 0

# ==========================================
# CONVERSION
# ==========================================

Write-Host "=========================================="
Write-Host "CONVERSION"
Write-Host "=========================================="
Write-Host ""

foreach ($File in $Files) {

    $Number++

    Write-Host "[$Number/$Total] $($File.FullName)"

    $Output = "$($File.FullName).mono.ogg"

    if (Test-Path -LiteralPath $Output) {
        Remove-Item -LiteralPath $Output -Force
    }

    & $FFmpeg `
        -hide_banner `
        -loglevel error `
        -y `
        -i $File.FullName `
        -ac 1 `
        -c:a libvorbis `
        -q:a 5 `
        $Output 2>> $Log

    if ($LASTEXITCODE -ne 0) {
        Write-Host "    ERROR: FFmpeg failed" -ForegroundColor Red
        Add-Content -LiteralPath $Log -Value $File.FullName

        if (Test-Path -LiteralPath $Output) {
            Remove-Item -LiteralPath $Output -Force
        }

        $Errors++
        continue
    }

    if (-not (Test-Path -LiteralPath $Output)) {
        Write-Host "    ERROR: output file was not created" -ForegroundColor Red
        Add-Content -LiteralPath $Log -Value $File.FullName
        $Errors++
        continue
    }

    $Size = (Get-Item -LiteralPath $Output).Length

    if ($Size -le 0) {
        Write-Host "    ERROR: output file is empty" -ForegroundColor Red
        Add-Content -LiteralPath $Log -Value $File.FullName
        Remove-Item -LiteralPath $Output -Force
        $Errors++
        continue
    }

    Write-Host "    OK" -ForegroundColor Green
    $Success++
}

# ==========================================
# RESULT
# ==========================================

Write-Host ""
Write-Host "=========================================="
Write-Host "RESULT"
Write-Host "=========================================="
Write-Host "Total:     $Total"
Write-Host "Success:   $Success"
Write-Host "Errors:    $Errors"
Write-Host "=========================================="
Write-Host ""

# Do not replace anything if there were errors
if ($Errors -gt 0) {

    Write-Host "ERRORS FOUND!" -ForegroundColor Red
    Write-Host "Original files will NOT be replaced."
    Write-Host ""
    Write-Host "Log:"
    Write-Host $Log

    Read-Host "Press Enter to exit"
    exit 1
}

# ==========================================
# CONFIRMATION
# ==========================================

Write-Host "All $Total files were converted successfully."
Write-Host ""
Write-Host "Original files have NOT been changed yet."
Write-Host ""

$Answer = Read-Host "Replace original files? [Y/N]"

if ($Answer -notmatch "^[Yy]$") {

    Write-Host ""
    Write-Host "Cancelled."
    Write-Host "Original files were not changed."
    Write-Host ""
    Write-Host "Mono files remain next to the originals."
    Write-Host ""

    Read-Host "Press Enter to exit"
    exit 0
}

# ==========================================
# REPLACE ORIGINALS
# ==========================================

Write-Host ""
Write-Host "=========================================="
Write-Host "REPLACING ORIGINAL FILES"
Write-Host "=========================================="
Write-Host ""

$Replaced = 0
$ReplaceErrors = 0

foreach ($File in $Files) {

    $Output = "$($File.FullName).mono.ogg"

    if (-not (Test-Path -LiteralPath $Output)) {
        Write-Host "ERROR: missing $Output" -ForegroundColor Red
        $ReplaceErrors++
        continue
    }

    try {

        Remove-Item -LiteralPath $File.FullName -Force

        Move-Item `
            -LiteralPath $Output `
            -Destination $File.FullName `
            -Force

        Write-Host "OK: $($File.Name)" -ForegroundColor Green
        $Replaced++

    }
    catch {

        Write-Host "ERROR: $($File.FullName)" -ForegroundColor Red
        Write-Host $_.Exception.Message
        $ReplaceErrors++
    }
}

Write-Host ""
Write-Host "=========================================="
Write-Host "DONE"
Write-Host "=========================================="
Write-Host "Replaced: $Replaced"
Write-Host "Errors:   $ReplaceErrors"
Write-Host ""

if ($ReplaceErrors -eq 0) {

    if (Test-Path -LiteralPath $Log) {
        Remove-Item -LiteralPath $Log -Force
    }

    Write-Host "All files replaced successfully."
}
else {
    Write-Host "Some files could not be replaced." -ForegroundColor Red
}

Read-Host "Press Enter to exit"
