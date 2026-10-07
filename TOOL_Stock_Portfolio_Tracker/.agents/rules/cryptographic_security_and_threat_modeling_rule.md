# 密碼學安全實踐與威脅建模鐵律 (Cryptographic Security & Threat Modeling Rule)

本規範為全專案在處理任何端到端加密 (E2EE)、雲端同步、資料庫儲存與跨平台 Web 應用時的**最高強制資安準則**。嚴禁以「理論上安全」或「零知識」為藉口忽視真實世界的攻擊面。

---

## 1. 嚴禁虛幻的「零破口」宣稱 (Zero-Fallback Threat Modeling)
- 任何加密架構在宣稱安全性前，必須主動進行嚴格的**威脅建模 (Threat Modeling)**。
- 必須誠實定義攻擊者邊界模型，明確指出何種攻擊可防、何種不可防（如已中木馬的終端設備、Web 端腳本交付信任鏈）。
- 嚴禁未經嚴謹比對即宣稱「完全等同於 Bitwarden 或 1Password」等商業成熟產品。

---

## 2. 金鑰衍生與離線暴力破解防護 (KDF & Offline Brute-Force)
- **公開可讀雲端 = 離線窮舉暴破場**：若密文可被下載，攻擊者可在本機/GPU 叢集無速率限制暴破。
- **KDF 運算標準**：
  - 若採用 `PBKDF2-HMAC-SHA256`，疊代次數**強制 $\ge 600,000$ 次**（嚴格遵循 OWASP 2023 最新指引，嚴禁使用過時的 100k）。
  - 若條件允許，優先考慮 `Argon2id`。
- **1Password 式雙因子金鑰 (Secret Key)**：
  - 嚴禁僅依賴使用者自訂的弱短密碼。
  - 本機首次生成帳號時，必須衍生一組密碼學安全隨機 128-bit 的 `Secret Key`。金鑰衍生公式強制為：
    $$\text{Key} = \text{KDF}(\text{MasterPassword} + \text{SecretKey}, \text{Salt})$$
  - 缺乏 Secret Key 時，攻擊者無法單憑字典檔破解密文。

---

## 3. AES-GCM 必須綁定 AAD 與防回滾 (AAD & Anti-Rollback)
- **認證額外資料 (AAD) 強制綁定**：
  - 嚴禁未設 AAD 之 GCM 加密。
  - 必須將 `userId`、`version`、`timestamp` 作為 AAD 納入 GCM 認證計算，防止密文被搬移至其他使用者帳號或偽造身分。
- **防範回滾攻擊 (Anti-Rollback)**：
  - AES-GCM 僅驗證內容完整性，無法驗證時效性。
  - 明文內部必須封裝遞增序號 (Monotonic Sequence Counter) 與更新時間戳。
  - 解密端解密後若序號小於本地已知最新序號，必須拒絕接受並警告使用者可能遭受回滾攻擊。

---

## 4. 後設資料與側信道防護 (Metadata & Length Padding)
- **長度側信道防護**：
  - 密文長度會直接洩漏持股檔數或資產結構。
  - 本機加密前，資料封包必須強制進行 **Padding 至固定尺寸塊**（如固定補齊至 32 KB 或 64 KB），徹底抹除長度特徵。
- **避免明文洩漏**：
  - 雲端節點上除密文、Salt、Nonce 外，嚴禁存放可推論行為模式的敏感明文時間戳或統計欄位。

---

## 5. 雲端儲存槽存取控制 (Cloud Storage Security Rules)
- **嚴禁全域公開讀寫**：
  - 嚴禁將 Firebase RTDB 等雲端設定為 `.read: true, .write: true`。
  - 必須封閉根節點遍歷列舉（`.read: false` on root），防止攻擊者爬行所有使用者代號。
  - 寫入必須配置寫入憑證（Write Token / Auth Verification），防止他人任意覆寫或抹除密文。

---

## 6. Web 端原生效能與交付防護 (Web Security & Memory)
- **嚴格 CSP (Content Security Policy)**：
  - Web 端頁面必須宣告嚴格 CSP，限制 `connect-src` 僅能連線至白名單 API，禁止未授權第三方外鏈。
- **記憶體銷毀的客觀描述**：
  - JavaScript 因 GC（垃圾回收）與不可變字串特性，無法 100% 保證實體記憶體即時覆寫。
  - 代碼應盡最大努力清空變數引用（`null` / ArrayBuffer 覆寫），但文檔不可做誇大之「100% 瞬間銷毀」保證。
