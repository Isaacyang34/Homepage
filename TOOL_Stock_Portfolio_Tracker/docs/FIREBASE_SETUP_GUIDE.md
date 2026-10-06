# Firebase Realtime Database 設定與安全維護手冊 (Firebase Setup & Security Guide)

本手冊提供「零知識加密投資庫存追蹤系統」之專屬雲端資料庫建置、安全規則（Security Rules）套用與日常運維指南。

---

## 1. 核心架構與免費額度說明

本專案採用 **零知識端到端架構 (Zero-Knowledge E2EE)**，Firebase 雲端**僅作為加密密文中繼站**：
- 雲端上存儲的資料一律為固定 32 KB、經 **600,000 次 PBKDF2** 與 **128-bit 設備金鑰** 加密後的二進位亂碼。
- 即使 Google 工程師或任何第三方取得資料庫完整備份，無主密碼與 Secret Key 下**在數學上不可能還原**任何股票代碼、股數或投資金額。

### Firebase Spark (免費方案) 規格評估
Google Firebase 提供永久免費的 Spark 方案，規格如下：
- **即時連線數**：同時 100 個連線（個人/家庭使用綽綽有餘）。
- **儲存容量**：**1 GB**。
  - 本系統每位使用者的固定加密封包約 **43 KB**（Base64 編碼後）。
  - 1 GB 空間約可容納 **23,000+** 位獨立使用者，單人使用空間佔用不到 **0.005%**。
- **每月下載流量**：**10 GB / 月**。
  - 每次在手機或網頁解密查看僅需下載 43 KB，相當於每月可重新整理超過 **240,000 次**。
- **結論**：個人或家庭日常使用，**完全不會產生任何費用**。

---

## 2. 三分鐘建立資料庫完整步驟

### 步驟 1：登入並新增專案
1. 使用 Google 帳號登入 [Firebase Console 控制台](https://console.firebase.google.com/)。
2. 點擊 **「新增專案」**（例如專案名稱輸入：`my-stock-tracker`）。
3. 詢問是否啟用 Google Analytics（分析）時，**選擇關閉**（本系統不需外部追蹤），點擊「建立專案」。

### 步驟 2：啟用 Realtime Database
1. 專案建立完成後，在左側功能表點選 **「建構 (Build)」 ➔ 「Realtime Database」**。
2. 點擊頁面中央的 **「建立資料庫」** 按鈕。
3. **資料庫位置**：建議選擇 **`asia-southeast1 (新加坡)`**（延遲最低）或 `us-central1 (美國)`。
4. **安全模式**：先選擇預設的「以鎖定模式啟動」，點擊完成。

### 步驟 3：取得專屬資料庫網址 (Database URL)
在資料庫的 **「資料 (Data)」** 頁籤頂部，會有一行資料庫專屬 URL，格式如下：
```text
https://<your-project-id>-default-rtdb.asia-southeast1.firebasedatabase.app/
```
> 請將此網址記下，稍後於軟體與網頁中套用。

---

## 3. 套用專屬安全規則 (Security Rules) — 至關重要！

預設的 Firebase 規則要不是「全鎖死（無法讀寫）」，要不就是「全開放（測試模式，任何人都能覆寫與刪除）」。
本專案為您量身打造了符合 OWASP 2023 密碼學標準的專屬規則：

### 規則設定步驟
1. 在 Realtime Database 頁面中，切換至 **「規則 (Rules)」** 分頁。
2. 清空原有文字，將以下 JSON 完整複製並貼上：

```json
{
  "rules": {
    /* 
      OWASP 2023 & 零知識密碼學安全規則 (Zero-Knowledge E2EE Rules)
      
      1. 根目錄閉鎖：嚴格禁止遍歷或列舉所有使用者帳號 (.read: false)
      2. 獨立路徑讀取：僅允許已知使用者代號者讀取其加密密文 (密文經 600K PBKDF2 + 128-bit Secret Key 保護)
      3. 寫入憑證防禦：覆寫或更新時強制比對 write_token，杜絕他人惡意清空或覆寫庫存資料
    */
    ".read": false,
    ".write": false,

    "portfolios": {
      ".read": false,
      ".write": false,

      "$user_id": {
        // 僅允許對特定使用者路徑進行讀取 (抗離線窮舉密文)
        ".read": true,
        
        // 寫入防護：
        // 1. 若資料庫原本為空 (首次同步)，允許建立並寫入隨機生成的 write_token
        // 2. 若已有資料，新上傳封包必須攜帶相同的 write_token，否則拒絕寫入 (防止外人惡意覆寫)
        // 3. 封包必須具備 ciphertext_b64, salt_hex, nonce_hex 等核心加密欄位
        ".write": "!data.exists() || (newData.child('write_token').val() === data.child('write_token').val())",
        ".validate": "newData.hasChildren(['ciphertext_b64', 'salt_hex', 'nonce_hex', 'write_token', 'sync_seq'])"
      }
    }
  }
}
```

3. 點擊右上角 **「發布 (Publish)」**。

### 規則防護機制解析
- **防列舉遍歷 (`".read": false`)**：駭客即使直接請求 `https://.../portfolios.json`，只會收到 `401 Permission Denied`，無法偷看全站有哪些帳號。
- **防惡意覆寫 (`write_token`)**：地端電腦首次同步時會隨機生成一組 32 字元的 `cloud_write_token`。他人若猜出您的使用者代號（例如 `isaac`），也無法上傳假資料洗掉您的庫存，因為他沒有您的 `write_token`。
- **防封包污染 (`.validate`)**：確保寫入的資料格式完整，缺少加密欄位的異常封包一律拒絕寫入。

---

## 4. 客戶端設定整合

### (1) 地端電腦軟體 (`Stock_Portfolio_Tracker.exe`)
- 點擊工具列的 **「雲端同步」** 按鈕。
- 系統已預設寫入您的資料庫網址；若日後需要更換，展開下方 **「⚙ 進階設定」** 即可修改。
- 複製記下畫面上專屬的 **128-bit 設備金鑰 (Secret Key)**（例如 `A8F2-99CD-31E0-74BA`）。
- 設定您的同步主密碼，按下 **「立即加密上傳至雲端」** 即可。

### (2) 跨平台網頁儀表板 (`web/index.html`)
- 前端代碼已將預設網址綁定為您的專屬資料庫。
- 開啟網頁時，**無需手動展開進階設定輸入網址**。
- 僅需輸入：
  1. **使用者代號 (User ID)**：本機同步時填寫的代號（例如 `isaac`）。
  2. **專屬加密主密碼**：本機同步時設定的密碼。
  3. **設備雙因子金鑰 (Secret Key)**：本機軟體產生的 128-bit 金鑰。
- 點擊「解密並載入資產分析」，瀏覽器本地記憶體將秒級還原所有投資數據。

---

## 5. 常見問題與故障排除 (FAQ)

### Q1: 同步時出現 `HTTP 401 Permission Denied` 怎麼辦？
- **原因**：通常是 Firebase 規則尚未發布，或是資料庫被設為全鎖死模式。
- **解法**：請檢查 Firebase 控制台的「規則 (Rules)」頁籤，確認已完整貼上上述 JSON 規則並點擊「發布」。

### Q2: 如果我更換了電腦，無法上傳覆寫怎麼辦？
- **原因**：因為新電腦沒有舊電腦生成的 `write_token`，被安全規則成功攔截（防惡意覆寫機制發揮作用）。
- **解法**：
  1. 登入 Firebase 控制台，進入「資料 (Data)」頁籤。
  2. 找到 `portfolios / <您的帳號>` 節點，點擊右側的 `X`（刪除該節點）。
  3. 在新電腦上重新點擊「立即加密上傳至雲端」，新電腦會自動建立新的憑證並寫入。

### Q3: 網頁放在 GitHub Pages 或手機開啟，會有跨域 (CORS) 限制嗎？
- **不會**。Firebase Realtime Database REST API 原生支援 CORS，任何網域（包括 `localhost`、GitHub Pages、手機本地端）皆可透過純前端原生 `fetch` 正常拉取密文。

### Q4: 如何備份或清除雲端資料？
- 在 Firebase 控制台「資料」分頁頂部，點選三個點 `...` 選單，可以隨時下載整份資料庫的 JSON 備份檔，或隨時一鍵清空資料庫。
