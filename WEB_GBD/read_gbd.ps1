$f = 'c:\Users\peter\OneDrive\Desktop\AI_Projects\WEB_GBD\260826-162120_UG.GBD'
$b = [System.IO.File]::ReadAllBytes($f)
$enc = [System.Text.Encoding]::ASCII
$hdr = $enc.GetString($b, 0, 12288) -replace [char]0, ' '
Write-Host "=== KEY LINES ==="
foreach ($line in ($hdr -split "`n")) {
    $l = $line.Trim()
    if ($l -match 'Start|Stop|Count|Sample|Model|Vendor|Header') {
        Write-Host $l
    }
}
Write-Host "=== FIRST 1500 BYTES ==="
Write-Host ($hdr.Substring(0, 1500))
