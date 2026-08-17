/**
 * ============================================================================
 * KAVAS 鼓風機 ESP32 資料接收與 LINE 告警腳本 (google_script.js)
 * 說明:
 *   此腳本部署於 Google Apps Script (Web App)，專門接收 ESP32 上傳之 JSON 資料，
 *   自動寫入 Google 試算表，並在發生故障時自動發送 LINE 告警通知！
 * 
 * 部署步驟:
 *   1. 打開全新的 Google 試算表 (Google Sheets)
 *   2. 點擊選單 [擴充功能] -> [Apps Script]
 *   3. 清空貼上此段程式碼
 *   4. (選配) 若要開啟 LINE 通知，將 LINE_NOTIFY_TOKEN 填入您的權杖
 *   5. 點擊右上角 [部署] -> [新增部署] -> 類型選擇 [Web 應用程式]
 *   6. 「誰可以存取」選擇【任何人 (Anyone)】 -> 點擊 [部署]
 *   7. 複製產生的 Web 應用程式 URL，貼回 ESP32 的 SERVER_URL 中！
 * ============================================================================
 */

// 🟢 (選配) LINE Notify 權杖，發出故障告警用。若不需要可留空 ""
const LINE_NOTIFY_TOKEN = ""; 

function doPost(e) {
  try {
    const sheet = SpreadsheetApp.getActiveSpreadsheet().getActiveSheet();
    
    // 如果是第一行，自動建立表頭
    if (sheet.getLastRow() === 0) {
      sheet.appendRow([
        "時間戳記", "鼓風機編號", "運轉狀態", "馬達轉速(RPM)", 
        "運轉頻率(Hz)", "輸出電壓(V)", "輸出電流(A)", 
        "馬達溫度(°C)", "驅動器溫度(°C)", "故障碼", 
        "環境溫度(°C)", "環境濕度(%RH)", "觸發原因"
      ]);
      sheet.getRange(1, 1, 1, 13).setFontWeight("bold").setBackground("#002b49").setFontColor("#ffffff");
    }

    // 解析 ESP32 發送過來的 JSON Payload
    const data = JSON.parse(e.postData.contents);
    
    const timestamp  = data.timestamp || new Date().toLocaleString("zh-TW", {timeZone: "Asia/Taipei"});
    const blowerId   = "鼓風機 " + (data.blower_id || 1);
    const status     = data.status || "UNKNOWN";
    const rpm        = data.rpm || 0;
    const freq       = data.freq || 0;
    const voltage    = data.voltage || 0;
    const current    = data.current || 0;
    const motorTemp  = data.motor_temp || 0;
    const driverTemp = data.driver_temp || 0;
    const faultCode  = data.fault_code || 0;
    const envTemp    = data.env_temp || 0;
    const envHumi    = data.env_humi || 0;
    const trigger    = data.trigger || "PERIODIC";

    // 寫入 Google 試算表
    sheet.appendRow([
      timestamp, blowerId, status, rpm, freq, voltage, current, 
      motorTemp, driverTemp, "0x" + faultCode.toString(16).toUpperCase(), 
      envTemp, envHumi, trigger
    ]);

    // 🔴 故障碼檢查與 LINE 警報推播
    if (faultCode !== 0 && LINE_NOTIFY_TOKEN !== "") {
      sendLineNotify(
        "\n⚠️ [KAVAS 鼓風機故障告警!]\n" +
        "• 設備: " + blowerId + "\n" +
        "• 時間: " + timestamp + "\n" +
        "• 故障碼: 0x" + faultCode.toString(16).toUpperCase() + "\n" +
        "• 馬達溫度: " + motorTemp + "°C\n" +
        "• 環境溫濕度: " + envTemp + "°C / " + envHumi + "%\n" +
        "請即刻派員檢查設備狀況！"
      );
    }

    return ContentService.createTextOutput(JSON.stringify({ status: "success" }))
                         .setMimeType(ContentService.MimeType.JSON);
                         
  } catch (err) {
    return ContentService.createTextOutput(JSON.stringify({ status: "error", message: err.toString() }))
                         .setMimeType(ContentService.MimeType.JSON);
  }
}

function doGet(e) {
  return ContentService.createTextOutput("KAVAS Blower Google Apps Script API Server is Running!");
}

// 發送 LINE Notify 簡訊通知
function sendLineNotify(message) {
  const url = "https://notify-api.line.me/api/notify";
  const options = {
    "method": "post",
    "headers": {
      "Authorization": "Bearer " + LINE_NOTIFY_TOKEN
    },
    "payload": {
      "message": message
    }
  };
  UrlFetchApp.fetch(url, options);
}
