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

# Copy unified single executable
Copy-Item $outExe "$targetDir\" -Force

# Copy WebMonitor.html (遠端網頁監看儀表板)
$webMonitorSrc = Join-Path $modernDir "WebMonitor.html"
if (Test-Path $webMonitorSrc) {
    Copy-Item $webMonitorSrc "$targetDir\" -Force
}

# 依 Clean Release Policy: 徹底刪除所有 .bat 批次檔 (Native winexe 直接雙擊即可，零批次檔殘留)
Get-ChildItem $targetDir -Filter "*.bat" | ForEach-Object {
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

Write-Host "🎉 Successfully packaged Release V$Version to:"
Write-Host "   👉 $targetDir"

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

            # Pure ASCII Unicode-escaped notes to eliminate PowerShell code-page corruption
            $notesEscaped = "1. TN \u7a69\u5b9a\u5224\u5b9a\u5408\u7406\u5316 (\u7b49\u5f85\u8f49\u901f\u9589\u8ff4\u8def\u88dc\u511f\u5230\u4f4d\u624d\u555f\u52d5\u5012\u6578)\n2. \u540c\u8f49\u901f\u63db\u9805\u76f4\u63a5\u5e73\u7a69\u8abf\u626d\uff0c\u4e0d\u964d\u8f09\u4e0d\u8b8a\u901f\n3. \u7570\u901f\u63db\u9805 3 \u6b65\u5e73\u7a69\u968e\u68af\u964d\u8f09\u81f3 25% \u518d\u8b8a\u901f\uff0c\u7b49\u901f\u5ea6\u5230\u4f4d\u518d\u905e\u589e\u52a0\u8f09\n4. \u7dda\u4e0a\u66f4\u65b0\u65e5\u8a8c Unicode \u9632\u4e82\u78bc\u6a5f\u5236"
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
