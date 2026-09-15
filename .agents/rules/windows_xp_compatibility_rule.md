# Windows XP 向下相容最高鐵律 (Windows XP Legacy Compatibility Rule)

本規範為動力計（Dynamometer）專案與工作台電腦的**最高強制硬體/系統相容規範**。
**現場工作電腦與機台環境為 Windows XP (x86 32-bit)，所有開發、除錯、通訊與 UI 設計必須 100% 永久相容 Windows XP！**

---

## 1. 作業系統與執行環境約束 (OS & Runtime)
* **目標作業系統**：Windows XP SP3 (x86 32-bit)。
* **.NET Runtime 上限**：僅支援 **.NET Framework 4.0**（Client Profile / Full Profile）。
* **嚴格禁止引進高版本 API**：
  * **嚴禁**使用 .NET 4.5+ 專屬類別（如 `System.Net.Http.HttpClient`、現代 `ZipArchive`、`IReadOnlyList` 等）。
  * 嚴禁使用 C# 6.0+ 未向下相容編譯期功能若引發 .NET 4.0 CLR 缺漏類別。
  * 所有編譯必須維持使用 `C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /platform:x86`。

---

## 2. 網路安全與加密通訊協定約束 (Schannel & TLS)
* **XP 系統底層限制**：
  * Windows XP 系統原生 `Schannel.dll` **僅支援 SSL 3.0 與 TLS 1.0**。
  * Windows XP 核心**無原生 TLS 1.2 / TLS 1.3 支援**，亦缺少現代強式橢圓曲線密碼套件（ECDHE-AES-GCM）。
* **現代雲端/HTTPS 呼叫禁忌**：
  * **嚴禁假設 Windows 原生 `HttpWebRequest` 或 WebClient 能直接連通現代雲端 (如 Firebase, AWS, Google Cloud)**。
  * 若直接在 XP 以原生 API 發送 HTTPS 至要求 TLS 1.2 的現代伺服器，必會觸發：
    `The underlying connection was closed: An unexpected error occurred on a send` (伺服器因 TLS 1.0 強制斷線)。
* **合法雲端傳輸解法**：
  1. **本機獨立 OpenSSL 代理** (如免安裝 stunnel / 獨立編譯之 curl.exe)。
  2. **區域網路中繼** (由同網段現代 Win10/11 筆電進行 HTTP ➔ HTTPS 轉發)。
  3. **純 HTTP 區域網路推播** (本機內網 WebMonitor 不受 HTTPS 限制)。

---

## 3. 驅動函式庫與 32 位元原生限制 (32-bit Native DLLs)
* **架構限制**：全系統強制為 **32-bit (x86)** 程式架構。
* **原生廠商 DLL**：
  * 橫河功率計通訊庫：`tmctl.dll`, `USBTMCAPI.dll`, `YKMUSBD.dll`, `ykusbtmc.dll`。
  * KEB 變頻器驅動庫：`protKEB.dll`, `protKEB_2.dll`。
  * 所有自訂結構體與 P/Invoke 封送處理（Marshal）必須嚴格遵守 32-bit 指標長度 (4 bytes) 與 stdcall 呼叫慣例。

---

## 4. UI 渲染與字體向下相容 (GDI+ & WinForms)
* **字體安全性**：
  * Windows XP 內建預設為「新細明體 / 標楷體」，不一定預裝「微軟正黑體」或「Segoe UI」。
  * 字體選用需具備後備機制（Fallback），或指定通用等寬字體（如 `Consolas`, `Courier New`, `Arial`）。
  * 嚴禁依賴 Windows 10/11 特殊 Emoji 符號（如高版本彩色符號），避免在 XP 上出現方框亂碼。
* **雙緩衝與防閃爍**：
  * XP 圖形子系統缺少 DWM (Desktop Window Manager) 現代硬體加速合成，所有自繪控制項必須強制開啟 `DoubleBuffered = true` 與標準 GDI+ 資源處置 (`using (Brush/Pen)` 防止 GDI Handle 洩漏崩潰)。
