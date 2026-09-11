param(
    [string]$Version = "2.5.0"
)

$baseDir = "c:\Users\peter\OneDrive\Desktop\AI_Projects\Dyanmometer"
$modernDir = Join-Path $baseDir "Dyanmometer_Modern"
$releaseDir = Join-Path $baseDir "Release"
$targetDir = Join-Path $releaseDir "Dynamometer_HMI_V${Version}_Portable"

# 1. Compile unified all-in-one Dynamometer_HMI_Pro.exe (Target x86 for 32-bit vendor DLLs: tmctl.dll, protKEB.dll, USBTMCAPI.dll)
$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$srcList = Get-ChildItem $modernDir -Filter "Dynamometer_*.cs" | Where-Object { $_.Name -notlike "*BACKUP*" } | Select-Object -ExpandProperty FullName
$testerSrc = Join-Path $modernDir "tools\Dynamometer_Device_Tester_GUI.cs"
$outExe = Join-Path $modernDir "Dynamometer_HMI_Pro.exe"

Write-Host "🔨 Compiling unified All-In-One Dynamometer_HMI_Pro.exe for Release V$Version (x86 32-bit mode)..."
& $csc /target:winexe /platform:x86 /codepage:65001 /out:$outExe /r:System.Windows.Forms.DataVisualization.dll "/r:$modernDir\BouncyCastle.Crypto.dll" /nologo $srcList $testerSrc
if ($LASTEXITCODE -ne 0) {
    Write-Error "❌ Compilation failed!"
    exit 1
}
Write-Host "✅ Compilation succeeded: $outExe"

# 2. Package to dedicated version release directory only
if (!(Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }

# Clean old artifacts within this release directory
$oldTester = Join-Path $targetDir "Dynamometer_Device_Tester_GUI.exe"
if (Test-Path $oldTester) { Remove-Item $oldTester -Force }
$oldTools = Join-Path $targetDir "tools"
if (Test-Path $oldTools) { Remove-Item $oldTools -Recurse -Force }

# 2-1. Local Rolling Backup (Keep latest 5 versions)
$existingExe = Join-Path $targetDir "Dynamometer_HMI_Pro.exe"
if (Test-Path $existingExe) {
    $backupDir = Join-Path $targetDir "backups"
    if (!(Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir -Force | Out-Null }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $oldVer = $Version
    try {
        $fvi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($existingExe)
        if ($fvi.ProductVersion -and $fvi.ProductVersion -ne "1.0.0.0") { $oldVer = $fvi.ProductVersion }
        elseif ($fvi.FileVersion -and $fvi.FileVersion -ne "1.0.0.0") { $oldVer = $fvi.FileVersion }
        else {
            $webServerCs = Join-Path $modernDir "Dynamometer_WebServer.cs"
            $appVerMatch = [regex]::Match((Get-Content $webServerCs -Raw), 'APP_VERSION\s*=\s*"([^"]+)"')
            if ($appVerMatch.Success) { $oldVer = $appVerMatch.Groups[1].Value }
        }
    } catch { }
    $bakName = "Dynamometer_HMI_Pro_v${oldVer}_${stamp}.exe"
    Copy-Item $existingExe (Join-Path $backupDir $bakName) -Force
    Write-Host "[BACKUP] Archived previous release to backups/$bakName"

    # Prune backups: keep latest 5 versions
    $bakFiles = Get-ChildItem $backupDir -Filter "Dynamometer_HMI_Pro_*.exe" | Sort-Object LastWriteTime -Descending
    if ($bakFiles.Count -gt 5) {
        $bakFiles | Select-Object -Skip 5 | ForEach-Object {
            Write-Host "[BACKUP] Pruning oldest backup: $($_.Name)"
            Remove-Item $_.FullName -Force
        }
    }
}

# Copy unified single executable
Copy-Item $outExe "$targetDir\" -Force

# Copy WebMonitor.html
$webMonitorSrc = Join-Path $modernDir "WebMonitor.html"
if (Test-Path $webMonitorSrc) {
    Copy-Item $webMonitorSrc "$targetDir\" -Force
}

# Copy Motor_Characteristics_Viewer.html
$viewerSrc = Join-Path $modernDir "Motor_Characteristics_Viewer.html"
if (Test-Path $viewerSrc) {
    Copy-Item $viewerSrc "$targetDir\" -Force
}

# Copy rollback batch files to target directory
Get-ChildItem $modernDir -Filter "*.bat" | ForEach-Object {
    Copy-Item $_.FullName "$targetDir\" -Force
}

# Clean Release Policy: remove build/test batch files, preserve rollback tools
Get-ChildItem $targetDir -Filter "*.bat" | Where-Object { 
    $_.Name -like "*build*" -or $_.Name -like "*release*" -or $_.Name -like "*run_*" -or $_.Name -like "*test*"
} | ForEach-Object {
    Remove-Item $_.FullName -Force
}

# Copy all vendor DLLs into dedicated DLL/ folder (tmctl, USBTMCAPI, YKMUSBD, ykusbtmc, protKEB, protKEB_2)
$targetDllDir = Join-Path $targetDir "DLL"
if (!(Test-Path $targetDllDir)) { New-Item -ItemType Directory -Path $targetDllDir -Force | Out-Null }
Get-ChildItem $modernDir -Filter "*.dll" | ForEach-Object {
    Copy-Item $_.FullName "$targetDllDir\" -Force
}
# Remove any residual DLL files in the root folder to keep it clean
Get-ChildItem $targetDir -Filter "*.dll" | ForEach-Object {
    Remove-Item $_.FullName -Force
}

# Clean any residual .vbs files (Native winexe directly double-clickable, no VBS needed)
Get-ChildItem $targetDir -Filter "*.vbs" | ForEach-Object {
    Remove-Item $_.FullName -Force
}

# 3. Rule 1: Auto-Purge & Teardown (用畢即清 - 強制清空 logs 與暫存檔案)
$targetLogs = Join-Path $targetDir "logs"
if (Test-Path $targetLogs) {
    Get-ChildItem $targetLogs -Include *.csv,*.log,*.png,*.jpg -Recurse | ForEach-Object {
        Remove-Item $_.FullName -Force
    }
}
$projLogs = Join-Path $baseDir "logs"
if (Test-Path $projLogs) {
    Get-ChildItem $projLogs -Include *.csv,*.log,*.png,*.jpg -Recurse | ForEach-Object {
        Remove-Item $_.FullName -Force
    }
}
$toolsLogs = Join-Path $baseDir "tools\logs"
if (Test-Path $toolsLogs) {
    Get-ChildItem $toolsLogs -Include *.csv,*.log,*.png,*.jpg -Recurse | ForEach-Object {
        Remove-Item $_.FullName -Force
    }
}

# Unblock all files
Get-ChildItem $targetDir -Recurse | ForEach-Object { Unblock-File $_.FullName }

# Synchronize to workspace root Release folder (確保無論從 Dyanmometer/Release 還是 Release/ 執行皆能獲取最新驅動與主程式)
$rootReleaseDir = "c:\Users\peter\OneDrive\Desktop\AI_Projects\Release\Dynamometer_HMI_V${Version}_Portable"
if (!(Test-Path $rootReleaseDir)) { New-Item -ItemType Directory -Path $rootReleaseDir -Force | Out-Null }
Copy-Item (Join-Path $targetDir "*") $rootReleaseDir -Recurse -Force

Write-Host "🎉 Successfully packaged Release V$Version to:"
Write-Host "   👉 $targetDir"
Write-Host "   👉 $rootReleaseDir"

# 4. Auto Git Push to GitHub gh-pages
Write-Host ""
Write-Host "📤 Auto-pushing to GitHub (gh-pages)..."

$gitExe = $null
@("$env:LOCALAPPDATA\Programs\Git\cmd\git.exe", "C:\Program Files\Git\cmd\git.exe", "C:\Program Files (x86)\Git\cmd\git.exe") | ForEach-Object {
    if (!$gitExe -and (Test-Path $_)) { $gitExe = $_ }
}

if (!$gitExe) {
    Write-Warning "⚠️  Git not found. Skipping auto-push. Please run push_to_github.bat manually."
} else {
    $repoRoot = "c:\Users\peter\OneDrive\Desktop\AI_Projects"
    $commitMsg = "Release: Dynamometer HMI V$Version auto-packaged $(Get-Date -Format 'yyyy-MM-dd HH:mm')"

    & $gitExe -C $repoRoot add "Dyanmometer/Release/" 2>&1 | Write-Host
    & $gitExe -C $repoRoot add "Dyanmometer/CHANGELOG.md" 2>&1 | Write-Host
    & $gitExe -C $repoRoot add "Dyanmometer/Dyanmometer_Modern/" 2>&1 | Write-Host
    & $gitExe -C $repoRoot commit -m $commitMsg 2>&1 | Write-Host

    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 1) {
        # LASTEXITCODE 1 = nothing to commit (already up to date), treat as OK
        & $gitExe -C $repoRoot push origin gh-pages 2>&1 | Write-Host
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Successfully pushed to GitHub gh-pages!"

            # 5. Auto-update Firebase version manifest (download_url must include Dyanmometer/ prefix)
            Write-Host ""
            Write-Host "☁️ Updating Firebase version manifest..."
            $exeGhPagesPath = "Dyanmometer/Release/Dynamometer_HMI_V${Version}_Portable/Dynamometer_HMI_Pro.exe"
            $dlUrl = "https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/$exeGhPagesPath"
            $releaseDate = Get-Date -Format "yyyy-MM-dd"

            $webServerCs = Join-Path $modernDir "Dynamometer_WebServer.cs"
            $appVerMatch = [regex]::Match((Get-Content $webServerCs -Raw), 'APP_VERSION\s*=\s*"([^"]+)"')
            $appVer = if ($appVerMatch.Success) { $appVerMatch.Groups[1].Value } else { $Version }

            # 5-1. Dynamically extract the latest release notes from CHANGELOG.md
            $changelogPath = Join-Path $baseDir "CHANGELOG.md"
            $latestNotes = ""
            if (Test-Path $changelogPath) {
                $clContent = [System.IO.File]::ReadAllLines($changelogPath, [System.Text.Encoding]::UTF8)
                $inSection = $false
                $h3Count = 0
                $notesList = New-Object System.Collections.ArrayList
                $secTitle = ""
                for ($i = 0; $i -lt $clContent.Length; $i++) {
                    $line = $clContent[$i]
                    if ($line -match '^##\s*\[(V[^\]]+)\]') {
                        if ($inSection) { break }
                        $inSection = $true
                        $secTitle = $matches[1]
                        continue
                    }
                    if ($inSection) {
                        if ($line -match '^##\s*\[V') { break }
                        if ($line -match '^###\s*') {
                            $h3Count++
                            continue
                        }
                        # Section H3 #3 is always the solution section (Rule 5)
                        if ($h3Count -ge 3) {
                            if ($line -match '^\s*[\d]+\.\s*\*\*(.+?)\*\*[:\uFF1A]?\s*(.*)') {
                                $curTitle = $matches[1].Replace('`', '').Replace('"', '').Trim()
                                $curDesc = $matches[2].Replace('`', '').Replace('"', '').Trim()
                                if ([string]::IsNullOrEmpty($curDesc) -and ($i + 1) -lt $clContent.Length) {
                                    $nextLine = $clContent[$i + 1]
                                    if ($nextLine -match '^\s*-\s*(.+)') {
                                        $curDesc = $matches[1].Replace('`', '').Replace('"', '').Trim()
                                    }
                                }
                                if ($curDesc.Length -gt 80) { $curDesc = $curDesc.Substring(0, 80) + "..." }
                                $lineStr = if (![string]::IsNullOrEmpty($curDesc)) { ($curTitle + ": " + $curDesc) } else { $curTitle }
                                [void]$notesList.Add(($notesList.Count + 1).ToString() + ". " + $lineStr)
                            }
                        }
                    }
                }
                if ($notesList.Count -gt 0) {
                    $headerStr = if (![string]::IsNullOrEmpty($secTitle)) { ("[" + $secTitle + "]") } else { "[Release Notes]" }
                    $latestNotes = $headerStr + "`n" + ($notesList -join "`n")
                }
            }

            if ([string]::IsNullOrEmpty($latestNotes)) {
                $latestNotes = "V" + $appVer + " Update Completed."
            }

            # Convert all non-ASCII characters to \uXXXX Unicode escapes to eliminate PowerShell codepage corruption
            $sbNotes = New-Object System.Text.StringBuilder
            foreach ($ch in $latestNotes.ToCharArray()) {
                $code = [int][char]$ch
                if ($code -gt 127) {
                    [void]$sbNotes.Append("\u" + $code.ToString("x4"))
                } elseif ($ch -eq "`n") {
                    [void]$sbNotes.Append("\n")
                } elseif ($ch -eq "`r") {
                    # skip CR
                } elseif ($ch -eq '"') {
                    [void]$sbNotes.Append('\"')
                } elseif ($ch -eq '\') {
                    [void]$sbNotes.Append('\\')
                } else {
                    [void]$sbNotes.Append($ch)
                }
            }
            $notesEscaped = $sbNotes.ToString()

            $manifestJson = "{`"version`":`"$appVer`",`"release_date`":`"$releaseDate`",`"download_url`":`"$dlUrl`",`"notes`":`"$notesEscaped`"}"
            $firebaseUrl = "https://dynamometer-live-default-rtdb.asia-southeast1.firebasedatabase.app/update/version.json"
            try {
                [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls
                $mbytes = [System.Text.Encoding]::UTF8.GetBytes($manifestJson)
                $mreq = [System.Net.WebRequest]::Create($firebaseUrl)
                $mreq.Method = "PUT"
                $mreq.ContentType = "application/json; charset=utf-8"
                $mreq.ContentLength = $mbytes.Length
                $mstream = $mreq.GetRequestStream()
                $mstream.Write($mbytes, 0, $mbytes.Length)
                $mstream.Close()
                $mresp = $mreq.GetResponse()
                $mreader = New-Object System.IO.StreamReader($mresp.GetResponseStream())
                $mreader.ReadToEnd() | Out-Null
                $mreader.Close()
                Write-Host "✅ Firebase manifest updated: V$appVer (Target V$Version) -> $dlUrl"
            } catch {
                Write-Warning "⚠️  Firebase manifest update failed: $_  (Please run scratch\fix_manifest.ps1 manually)"
            }
        } else {
            Write-Warning "⚠️  Push failed. Please check GitHub authorization and run push_to_github.bat manually."
        }
    } else {
        Write-Warning "⚠️  Git commit failed. Please check git status manually."
    }
}
