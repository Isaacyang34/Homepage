# GBD 溫度資料閱讀器 & 後端 API 資安伺服器 (Server-Side Protection)

本專案採用 **「WebServer ＋ 後端 API 服務 (Server-Side Architecture)」** 設計，將核心編輯演算法與密碼驗證邏輯完全託管於 Node.js 後端伺服器，達成 100% 軍規級資安防護。

---

## 🛡️ 資安防護機制 (Security Features)

1. **演算法零洩漏 (Server-Side Execution)**：
   - 數值偏移 (`/api/edit/offset`)、曲線延伸 (`/api/edit/extend`)、日期修改 (`/api/edit/datetime`) 之二進位處理演算法全數在伺服器端執行。
   - 使用者電腦端 0% 存在修改演算法程式碼，徹底防止離線反組譯與演算法遭偷取。

2. **IP Rate-Limiter 防暴力破解 (Brute-Force Protection)**：
   - 伺服器端實作 IP 嘗試計數器。
   - 同一 IP 連續密碼錯誤 **5 次**，伺服器立即觸發 **15 分鐘 IP 封鎖 (HTTP 429 Too Many Requests)**。
   - 防護邏輯執行於伺服器記憶體，前端修改任何 HTML/JS 變數皆無法繞過封鎖。

3. **Bearer Token 授權機制**：
   - 成功驗證密碼 (`e799c2fbe2`) 後回傳隨機 48-char Session Token。
   - 所有編輯 API 均受 Bearer Auth 中間件保護。

---

## 🚀 快速啟動說明 (Quick Start)

### 1. 安裝套件
```bash
npm install
```

### 2. 啟動伺服器
```bash
npm start
```
伺服器啟動後將監聽 `http://localhost:3000`。

### 3. 開啟網頁
在瀏覽器中開啟：
👉 **http://localhost:3000**

---

## 📁 目錄結構

- `server.js` - Express 後端資安伺服器（含 IP Rate Limiting 與 GBD 演算法 API）
- `public/index.html` - 前端閱讀器 UI (含 AB 點對比 Dashboard、雙點裁切與 Server API 串接)
- `GBD_Editor.html` - 單機本機 AES-256 備用版本
- `backups/` - 歷史版本備份區
