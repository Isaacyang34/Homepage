# 暫態穿透隧道生命週期守護與動態 Webhook 同步鐵律 (Ephemeral Tunnel Watchdog & Dynamic Webhook Sync Rule)

## 1. 致命缺陷現象與根因分析 (Fatal Bugs & Root Causes)

### 缺陷一：Cloudflare 官方租期回收與客戶端重試死鎖 (TTL Eviction & Zombie Hang)
* **現象描述**：使用免註冊臨時通道（`trycloudflare.com`），運行數小時後外部 Webhook 請求突然失效（回報 `Invalid webhook endpoint URL`）。
* **根因**：
  1. `trycloudflare.com` 是短期測試沙盒，Cloudflare 伺服器端會在數小時後強制回收 (Reclaim)。
  2. 回收後回傳 `ERR Register tunnel error: Unauthorized: Tunnel not found`。
  3. 原生 `cloudflared` 進程不會退出也不會申請新網址，而是以舊 ID 陷入無限重試死鎖（Zombie Tunnel）。

### 缺陷二：看門狗代碼誤判烏龍自殺機制 (Watchdog False-Positive Suicide Loop - 重大血淚教訓)
* **現象描述**：守護程式每隔 60 秒就擅自殺死隧道、重新申請一個全新隨機網址，導致網址不斷跳動且伺服器負載劇增。
* **致命根因 (Root Cause)**：
  * **嚴禁誤判正常日誌**：`cloudflared` 在**每一次正常啟動時**，都會輸出一行無害的提示：
    `Cannot determine default configuration path. No file [config.yml config.yaml] in [...]`
  * 若看門狗誤將這行「正常啟動提示」列為死亡判定條件，將導致：
    **每一次剛啟動成功，看門狗就誤判為進程已死 $\rightarrow$ 立即強制 kill $\rightarrow$ 每 60 秒無限重啟 $\rightarrow$ 觸發徹夜連環自殘！**

### 缺陷三：徹夜高頻轟炸引發 HTTP 429 官方封鎖 (Anti-Hammering & Error 1015)
* **現象描述**：嘗試建立新隧道時報錯 `quick tunnel provisioning failed with status 429`，整個 IP 被 Cloudflare 防火牆封鎖 30 ~ 36 分鐘（`error code: 1015`，`retry-after: 2165`）。
* **根因**：看門狗在通道失效或自殘時，採用了無退避的固定 60 秒盲目硬重試，徹夜向 `api.trycloudflare.com` 發送了數百次申請，直接觸發了官方的防濫用頻率限制（Rate Limit）。

---

## 2. 核心防禦與架構規範 (Mandatory Prevention Rules)

### 鐵律一：死亡判斷嚴格白名單（杜絕誤判自殺）
* 看門狗掃描日誌時，**嚴格禁止使用任何模糊匹配或包含一般 INF/WRN 提示**！
* **致命錯誤嚴格白名單（僅限以下明確死亡字串）**：
  * `Unauthorized: Tunnel not found`（官方回收租期）
  * `sendmsg: network is unreachable`（網路斷開）
  * `quic: connection closed` 或 `connection terminated`
* **絕對禁用字串（嚴格禁止列入死亡條件）**：
  * **`Cannot determine default configuration path`**（此為正常啟動日誌，絕對嚴禁作為死亡判斷依據！）

### 鐵律二：HTTP 429 智能退避冷卻機制 (Anti-Hammering & Exponential Backoff)
* 看門狗在每次重拉前，必須審查日誌與 HTTP 狀態碼：
  * 凡檢測到 **`status 429`** 或 **`error code: 1015`**：
  1. **立即停止衝撞**：嚴格禁止在下一週期（60秒）重複重啟，避免重設官方懲罰計時器！
  2. **啟動 30 分鐘退避冷卻**：設定 `rateLimitCooldownUntil = Date.now() + 30 * 60 * 1000`，在冷卻期間進入完全靜默模式。
  3. **非 429 一般錯誤採用指數退避**：重試間隔依序拉長（1分 $\rightarrow$ 3分 $\rightarrow$ 6分 $\rightarrow$ 15分），嚴禁高頻密集轟炸。

### 鐵律三：URL 正則提取純淨過濾 (Regex Sanitization)
* 提取新分配之 URL 時，正則必須為：
  `/https:\/\/[a-zA-Z0-9.-]+\.trycloudflare\.com/g`
* **必須過濾 API 自身端點**：
  * 提取結果必須強制排除 `api.trycloudflare.com`（此為官方報錯文本中出現的伺服器位址，非通道網址）。

### 鐵律四：動態 Webhook API 零介入自動同步閉鎖 (Automated Dynamic Endpoint Synchronization)
* 臨時隧道重啟獲取真正的新 URL 後：
  * **嚴格禁止依賴人工手動複製貼上**。
  * 系統必須在程式內部直接呼叫平台 API（如 LINE Messaging API `PUT /v2/bot/channel/webhook/endpoint`）完成自動同步。
  * 同步後必須發起 `POST /v2/bot/channel/webhook/test` 探測，確認 `statusCode: 200, success: true` 始宣告自癒成功。

### 鐵律五：全盤前置條件審查鐵律 (Upfront Complete Prerequisite Check - 杜絕做一步問一步)
* **嚴禁未查清前提就讓使用者操作**：
  * 當考慮引入第三方平台（如 Cloudflare 具名隧道、Ngrok、Tailscale 等）時，AI 必須在向使用者提議前，**通盤掌握該方案的所有硬性前置條件**：
    1. **網域要求**：是否強制要求自備頂級網域（Zone）？（Cloudflare 具名隧道強制要求，無網域者不可行）。
    2. **系統架構相容性**：該平台的用戶端軟體是否 100% 相容目標作業系統？（例如通用 Linux Go binary 在 Android Bionic 上因缺少 resolv.conf 會發生 DNS 解析失敗）。
  * 若前置條件不滿足，**嚴禁推播給使用者執行**，杜絕「做一步、碰壁一步、又叫使用者換一個」的惡劣體驗。
