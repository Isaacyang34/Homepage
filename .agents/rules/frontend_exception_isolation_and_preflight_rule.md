# 前端 JavaScript/HTML 零崩潰、例外隔離與交付前靜態審查鐵律 (Frontend JS/HTML Zero-Crash, Exception Isolation & Pre-Flight Verification Gate Rule)

## 1. 核心精神與最高禁令
* **核心宗旨**：前端監控儀表板必須具備高韌性（High Resilience）。嚴禁因單一 UI 元件的字串拼接或渲染小錯誤，引發整份腳本崩潰或偽報全機斷線！
* **最高禁令（嚴禁偽報斷線與未檢交付）**：
  * **嚴禁「濫報斷線」**：嚴格禁止將「DOM 渲染邏輯例外」與「真實網路通訊斷線」混入同一個 `catch` 塊中！任何 DOM 渲染錯誤絕不允許偽報為「伺服器連線中斷」或彈出中斷紅框恐嚇使用者。
  * **嚴禁未驗交付**：凡修改任何 `.html`、`.js` 檔案，在交付使用者前，AI 必須在背景執行靜態語法與順序預檢，確保 100% 零語法中斷（SyntaxError）、零暫時性死區（ReferenceError）與零未閉合字串。

---

## 2. 兩大物理防禦機制 (Two Mandatory Architectural Defenses)

### 機制一：通訊與 DOM 渲染物理隔離 (Network-DOM Exception Decoupling)
所有的遙測輪詢與資料拉取函式（如 `fetchTelemetry()`），必須強制分為獨立的二階段物理隔離區塊：
```javascript
// ✅ 標準隔離範式：
async function fetchTelemetry() {
    let data = null;

    // 階段一：真實網路請求（僅當 HTTP 4xx/5xx 或 Socket 斷開才判定為連線中斷）
    try {
        const resp = await fetch("/api/telemetry");
        if (!resp.ok) throw new Error("HTTP " + resp.status);
        data = await resp.json();
        hideDisconnectBanner();
    } catch (netErr) {
        console.error("Telemetry Network Error (Physical Connection Lost):", netErr);
        showDisconnectBanner(); // 當且僅當真正的通訊斷線才允許顯示
        return;
    }

    // 階段二：DOM 畫面渲染（物理隔離，若有 JS 渲染邏輯異常絕不偽報為連線中斷）
    try {
        if (data) renderDashboard(data);
    } catch (renderErr) {
        console.error("Dashboard DOM Render Error (Non-Network Exception):", renderErr);
        // 僅記日誌，其餘模組不受波及，嚴禁彈出全機斷線紅框！
    }
}
```

---

### 機制二：ES6 TDZ 順序鐵律與狀態綁定 (Anti-TDZ & State Integrity)
1. **先宣告後使用（Strict Declaration Order）**：
   * 凡使用 `const` 與 `let` 宣告之變數，其位置必須**物理置於任何讀取、模板字串（`${variable}`）或分支邏輯之上**，徹底杜絕 `ReferenceError: Cannot access before initialization`。
2. **禁止開關焦點鎖死（No Focus Lock on Boolean Controls）**：
   * `document.activeElement !== target` 僅適用於文字輸入框（防止打字被打斷）；**嚴禁將此檢查套用於 Checkbox / Switch 開關**，否則使用者點擊開關後焦點鎖死，將導致開關視覺與後端真實狀態完全脫節。
3. **按鈕語意與狀態原子綁定（Semantic Action Binding）**：
   * 彈窗中凡標有「立即套用」、「儲存並生效」之確認按鈕，送出之 Payload 必須明確宣告 `enabled: true`，嚴禁依賴主畫面未同步之歷史開關狀態。
4. **即時樂觀反饋（Optimistic UI）**：
   * 使用者操作開關的瞬間（`change` 事件），畫面文字與樣式必須毫秒級先行更新，隨後發送非同步請求，杜絕無回饋引發的使用者焦慮。

---

## 3. 交付前自動化靜態預檢閉環 (Pre-Flight Verification Gate)

每次只要改動前端代碼，在結束 Turn 向使用者回報前，必須強制完成以下三道關卡：

* **關卡 1：字串與轉義掃描**：
  驗證全檔所有普通字串（`"` 與 `'`）未包含未轉義的生換行，多行通知必須採用 `\n` 或標準模板字面量（反引號 `` ` ``）。
* **關卡 2：靜態語法解析與未宣告變數檢測**：
  在背景調用 Node.js 或 Python AST 語法掃描器，確認無語法中斷與變數作用域順序錯亂。
* **關卡 3：防快取版本號同動遞增（Cache-Busting Increment）**：
  在引用處（如 `index.html` 的 `<script src="app.js?v=XX.0">`）同步遞增版本號，保證使用者重新整理時 100% 載入修復後的最新代碼。
