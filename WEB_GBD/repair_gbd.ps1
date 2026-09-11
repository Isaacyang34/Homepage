$templateFile = "260826-162120_UG.GBD"
$corruptFile  = "SIMW132N-15-08_S6_20260908_083725_NoLoad.gbd"
$fixedFile    = "SIMW132N-15-08_S6_20260908_083725_NoLoad_repaired.gbd"

$tBytes = [System.IO.File]::ReadAllBytes($templateFile)
$cBytes = [System.IO.File]::ReadAllBytes($corruptFile)

# Header template: 12288 bytes
$enc = [System.Text.Encoding]::ASCII
$hdrStr = $enc.GetString($tBytes, 0, 12288)

# Replace Counts
$counts = ($cBytes.Length - 12288) / 36
Write-Host "Counts: $counts"

# Start time: 2026-09-08 08:37:25
$startDT = [DateTime]::ParseExact("2026-09-08 08:37:25", "yyyy-MM-dd HH:mm:ss", $null)
$stopDT = $startDT.AddSeconds($counts)
$startStr = $startDT.ToString("yyyy-MM-dd,HH:mm:ss")
$stopStr = $stopDT.ToString("yyyy-MM-dd,HH:mm:ss")

Write-Host "Start: $startStr"
Write-Host "Stop:  $stopStr"

# Update header lines
$hdrStr = [regex]::Replace($hdrStr, "Counts\s*=\s*\d+", ("Counts    =        " + $counts))
$hdrStr = [regex]::Replace($hdrStr, "Start\s*=\s*[\d-]+,[\d:]+", ("Start     = " + $startStr))
$hdrStr = [regex]::Replace($hdrStr, "Stop\s*=\s*[\d-]+,[\d:]+", ("Stop      = " + $stopStr))

# Pad or truncate header to exactly 12288 bytes
$hdrBytes = $enc.GetBytes($hdrStr)
if ($hdrBytes.Length -lt 12288) {
    $pad = New-Object byte[] (12288 - $hdrBytes.Length)
    # Fill pad with spaces or zeros
    for ($i = 0; $i -lt $pad.Length; $i++) { $pad[$i] = 32 }
    $hdrBytes = $hdrBytes + $pad
} elseif ($hdrBytes.Length -gt 12288) {
    $hdrBytes = $hdrBytes[0..12287]
}

# Create fixed file = hdrBytes + raw binary data from corruptFile[12288..end]
$rawBinary = $cBytes[12288..($cBytes.Length - 1)]
$outBytes = New-Object byte[] ($hdrBytes.Length + $rawBinary.Length)
[Array]::Copy($hdrBytes, 0, $outBytes, 0, $hdrBytes.Length)
[Array]::Copy($rawBinary, 0, $outBytes, $hdrBytes.Length, $rawBinary.Length)

[System.IO.File]::WriteAllBytes($fixedFile, $outBytes)
Write-Host "Fixed file written: $fixedFile (Total bytes: $($outBytes.Length))"
