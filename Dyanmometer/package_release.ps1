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
            $notesEscaped = "1. \u66f4\u65b0\u65e5\u8a8c\u7de8\u78bc\u5168\u9762\u4fee\u5fa9 (Unicode \u9006\u907f\u4e82\u78bc)\n2. T-N \u5f85\u6e2c\u7aef\u96d9\u9589\u8ff4\u8def\u81ea\u52d5\u88dc\u8f49\u5dee (\u540c\u6b65 S1/S2/S6 \u907f\u514d 50s \u8d85\u6642)\n3. TN \u6e2c\u8a66\u53f3\u4e0a\u65b0\u589e\u5373\u6642\u6eab\u5ea6\u66f2\u7dda\u8207\u901a\u9053\u81ea\u9078\u76e3\u63a7\n4. T-N \u66f2\u7dda\u591a\u9ede\u81ea\u8a02\u6e2c\u8a66\u6a21\u5f0f (5\u79d2\u7a69\u5b9a + 30\u79d2\u6bcf\u79d2\u53d6\u6a23\u5e73\u5747)\n5. \u63db\u9805\u5148\u964d\u8f09\u81f3 25% \u6838\u5fc3\u904e\u6e21\u4fdd\u8b77\n6. GBD \u6a94\u6848 12KB \u6a19\u982d\u5373\u6642\u5beb\u5165\u8207\u667a\u80fd\u9006\u7b97\u9084\u539f"
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
