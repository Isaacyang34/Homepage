/*
 * ----------------------------------------------------------------------------
 * 專案名稱: KAVAS 鼓風機 ESP32 3通道 Modbus Wi-Fi 資料記錄器 (kavas_esp32_logger.ino)
 * 製造商設備: 克瑪里能源科技 (GPE) KAVAS MTB-5HP 鼓風機
 * 
 * 核心功能:
 *   1. 3路獨立 UART (HardwareSerial 0, 1, 2) 獨立收發 3 台 ID=1 的鼓風機
 *   2. Wi-Fi 連線時：每 5 秒採樣並上傳一次 (高頻實時監控)
 *   3. Wi-Fi 斷線時：自動切換為 每 1 分鐘採樣一次 寫入 LittleFS Flash 暫存 (極致省空間)
 *   4. 故障碼變更：不論連線或斷線，皆「即時」觸發紀錄/上傳
 *   5. 整合 DHT22 工規環境溫濕度感測器 (GPIO 14)
 *   6. 網路恢復後：自動批次補傳歷史暫存 Log 並清空 LittleFS 暫存檔
 * ----------------------------------------------------------------------------
 */

#include <Arduino.h>
#include <WiFi.h>
#include <HTTPClient.h>
#include <time.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <SD.h>
#include <SPI.h>
#include <LittleFS.h>
#include <ModbusMaster.h>
#include <DHT.h>

// ─── 1. Wi-Fi 與 伺服器設定 ───
const char* WIFI_SSID     = "YOUR_WIFI_SSID";
const char* WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";
const char* SERVER_URL    = "https://script.google.com/macros/s/AKfycbyXYi3PKoTi25CTPQ5flG1rACBjuktIa-Hb3SLM8NzfM6hTyYUvWKL-avCu5wr0E9Hg/exec"; 

// NTP 時間校時設定
const char* NTP_SERVER       = "pool.ntp.org";
const long  GMT_OFFSET_SEC    = 8 * 3600; // 台灣時區 UTC+8
const int   DAYLIGHT_OFFSET_SEC = 0;

// 硬體 Jumper 跳線開關與 SD 卡 PIN 腳
#define SD_JUMPER_PIN 15  // 接腳拉低至 GND 代表啟用 SD 卡
#define SD_CS_PIN     5   // SPI Chip Select
bool isSdCardEnabled = false;

// ─── 2. 溫濕度感測器設定 ───
#define DHTPIN       14
#define DHTTYPE      DHT22
DHT dht(DHTPIN, DHTTYPE);

// ─── 3. 3路獨立 RS485 引腳定義 ───
// Blower 1 (HardwareSerial 1)
#define B1_RX_PIN  16
#define B1_TX_PIN  17
#define B1_DE_PIN  4

// Blower 2 (HardwareSerial 2)
#define B2_RX_PIN  25
#define B2_TX_PIN  26
#define B2_DE_PIN  27

// Blower 3 (HardwareSerial 0 映射腳位)
#define B3_RX_PIN  21
#define B3_TX_PIN  22
#define B3_DE_PIN  23

#define MODBUS_BAUD 19200
#define SLAVE_ID    1

ModbusMaster mb1, mb2, mb3;
uint16_t lastFault[3] = {0, 0, 0};
unsigned long lastLogTime = 0;

// 鼓風機資料結構
struct BlowerRecord {
  uint16_t rpm;
  uint16_t freq;
  uint16_t volt;
  uint16_t curr;
  uint16_t mTemp;
  uint16_t dTemp;
  uint16_t fault;
  bool isOnline;
};

// RS485 方向控制 Enable Helper
void deB1_HIGH() { digitalWrite(B1_DE_PIN, HIGH); }
void deB1_LOW()  { digitalWrite(B1_DE_PIN, LOW); }
void deB2_HIGH() { digitalWrite(B2_DE_PIN, HIGH); }
void deB2_LOW()  { digitalWrite(B2_DE_PIN, LOW); }
void deB3_HIGH() { digitalWrite(B3_DE_PIN, HIGH); }
void deB3_LOW()  { digitalWrite(B3_DE_PIN, LOW); }

// 取得格式化時間字串
String getFormattedTime() {
  struct tm timeinfo;
  if (!getLocalTime(&timeinfo)) return "1970-01-01 00:00:00";
  char timeBuf[25];
  strftime(timeBuf, sizeof(timeBuf), "%Y-%m-%d %H:%M:%S", &timeinfo);
  return String(timeBuf);
}

// 讀取單台鼓風機 Modbus 資料
BlowerRecord readBlowerData(ModbusMaster &node, int bIndex) {
  BlowerRecord rec = {0, 0, 0, 0, 0, 0, 0, false};
  if (node.readHoldingRegisters(19, 1) == node.ku8MBSuccess) {
    rec.rpm = node.getResponseBuffer(0);
    rec.isOnline = true;
  } else {
    return rec;
  }
  if (node.readHoldingRegisters(30, 1) == node.ku8MBSuccess)  rec.freq  = node.getResponseBuffer(0);
  if (node.readHoldingRegisters(13, 1) == node.ku8MBSuccess)  rec.volt  = node.getResponseBuffer(0);
  if (node.readHoldingRegisters(213, 1) == node.ku8MBSuccess) rec.curr  = node.getResponseBuffer(0);
  if (node.readHoldingRegisters(170, 1) == node.ku8MBSuccess) rec.mTemp = node.getResponseBuffer(0);
  if (node.readHoldingRegisters(140, 1) == node.ku8MBSuccess) rec.dTemp = node.getResponseBuffer(0);
  if (node.readHoldingRegisters(35, 1) == node.ku8MBSuccess)  rec.fault = node.getResponseBuffer(0);
  return rec;
}

// 發送 HTTP POST 上傳或於斷線時快取至 Flash
void sendDataOrCache(String timeStr, int blowerID, BlowerRecord b, float envTemp, float envHumi, String triggerReason) {
  String jsonPayload = String("{") +
    "\"timestamp\":\"" + timeStr + "\"," +
    "\"blower_id\":" + String(blowerID) + "," +
    "\"status\":\"" + (b.isOnline ? "ONLINE" : "OFFLINE") + "\"," +
    "\"rpm\":" + String(b.rpm) + "," +
    "\"freq\":" + String(b.freq) + "," +
    "\"voltage\":" + String(b.volt) + "," +
    "\"current\":" + String(b.curr) + "," +
    "\"motor_temp\":" + String(b.mTemp) + "," +
    "\"driver_temp\":" + String(b.dTemp) + "," +
    "\"fault_code\":" + String(b.fault) + "," +
    "\"env_temp\":" + String(envTemp, 1) + "," +
    "\"env_humi\":" + String(envHumi, 1) + "," +
    "\"trigger\":\"" + triggerReason + "\"" +
    "}";

  // 1. Wi-Fi 連線正常 -> 實時 HTTP 上傳 (支援 Google Apps Script 302 重導向)
  if (WiFi.status() == WL_CONNECTED) {
    HTTPClient http;
    http.begin(SERVER_URL);
    http.setFollowRedirects(HTTPC_STRICT_FOLLOW); // 自動跟隨 302 Redirect，完美相容 Google Sheets
    http.addHeader("Content-Type", "application/json");
    int httpCode = http.POST(jsonPayload);
    if (httpCode > 0 && (httpCode == HTTP_CODE_OK || httpCode == HTTP_CODE_CREATED)) {
      Serial.printf("[ONLINE UPLOAD 5s] Blower_%d -> OK (Code: %d)\n", blowerID, httpCode);
      http.end();
      return;
    }
    http.end();
  }

  // 2. Wi-Fi 斷線 / 上傳失敗 -> 快取寫入 LittleFS
  Serial.printf("[OFFLINE CACHE 1m] Blower_%d -> Flash LittleFS Saved\n", blowerID);
  File cacheFile = LittleFS.open("/offline_cache.log", FILE_APPEND);
  if (cacheFile) {
    cacheFile.println(jsonPayload);
    cacheFile.close();
  }
}

// 網路恢復後：自動補傳 LittleFS 快取紀錄並清空暫存檔
void flushCacheQueue() {
  if (WiFi.status() != WL_CONNECTED || !LittleFS.exists("/offline_cache.log")) return;

  File cacheFile = LittleFS.open("/offline_cache.log", FILE_READ);
  if (!cacheFile || cacheFile.size() == 0) {
    if (cacheFile) cacheFile.close();
    LittleFS.remove("/offline_cache.log");
    return;
  }

  Serial.printf("[NETWORK RECOVERED] Flushing LittleFS Cache (%d Bytes)...\n", cacheFile.size());
  String line;
  bool allSuccess = true;

  while (cacheFile.available()) {
    line = cacheFile.readStringUntil('\n');
    line.trim();
    if (line.length() == 0) continue;

    HTTPClient http;
    http.begin(SERVER_URL);
    http.addHeader("Content-Type", "application/json");
    int httpCode = http.POST(line);
    http.end();

    if (httpCode <= 0 || (httpCode != HTTP_CODE_OK && httpCode != HTTP_CODE_CREATED)) {
      Serial.println("[FLUSH PAUSED] Re-upload interrupted, will retry later.");
      allSuccess = false;
      break;
    }
    delay(40);
  }
  cacheFile.close();

  if (allSuccess) {
    LittleFS.remove("/offline_cache.log");
    Serial.println("[CACHE CLEARED] All cached records uploaded & LittleFS cleared successfully!");
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(B1_DE_PIN, OUTPUT); digitalWrite(B1_DE_PIN, LOW);
  pinMode(B2_DE_PIN, OUTPUT); digitalWrite(B2_DE_PIN, LOW);
  pinMode(B3_DE_PIN, OUTPUT); digitalWrite(B3_DE_PIN, LOW);

  // 初始化 DHT22
  dht.begin();

  // 初始化 3 路 UART
  Serial1.begin(MODBUS_BAUD, SERIAL_8N1, B1_RX_PIN, B1_TX_PIN);
  Serial2.begin(MODBUS_BAUD, SERIAL_8N1, B2_RX_PIN, B2_TX_PIN);
  Serial0.begin(MODBUS_BAUD, SERIAL_8N1, B3_RX_PIN, B3_TX_PIN);

  mb1.begin(SLAVE_ID, Serial1); mb1.preTransmission(deB1_HIGH); mb1.postTransmission(deB1_LOW);
  mb2.begin(SLAVE_ID, Serial2); mb2.preTransmission(deB2_HIGH); mb2.postTransmission(deB2_LOW);
  mb3.begin(SLAVE_ID, Serial0); mb3.preTransmission(deB3_HIGH); mb3.postTransmission(deB3_LOW);

  // 初始化 LittleFS
  if (!LittleFS.begin(true)) {
    Serial.println("[FS ERROR] LittleFS Mount Failed!");
  }

  // 初始化 Wi-Fi 與 NTP
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  
  int retry = 0;
  while (WiFi.status() != WL_CONNECTED && retry < 20) {
    delay(500);
    retry++;
  }
  if (WiFi.status() == WL_CONNECTED) {
    configTime(GMT_OFFSET_SEC, DAYLIGHT_OFFSET_SEC, NTP_SERVER);
  }
}

void loop() {
  bool isConnected = (WiFi.status() == WL_CONNECTED);

  // 1. 自動重連與歷史快取補傳
  if (isConnected) {
    flushCacheQueue();
  } else {
    WiFi.reconnect();
  }

  // 2. 動態採樣週期計算：連線時 5 秒紀錄一次，斷線時 1 分鐘紀錄一次
  unsigned long currentInterval = isConnected ? 5000 : 60000;
  bool isTimerDue = (millis() - lastLogTime >= currentInterval) || (lastLogTime == 0);

  String timeStr = getFormattedTime();

  // 讀取環境溫濕度
  float envTemp = dht.readTemperature();
  float envHumi = dht.readHumidity();
  if (isnan(envTemp)) envTemp = 0.0;
  if (isnan(envHumi)) envHumi = 0.0;

  // 讀取三台鼓風機 Modbus
  BlowerRecord b1 = readBlowerData(mb1, 1);
  BlowerRecord b2 = readBlowerData(mb2, 2);
  BlowerRecord b3 = readBlowerData(mb3, 3);

  // 3. 故障碼即時觸發 (不受定時限制，有故障立刻記錄/上傳)
  if (b1.fault != 0 && b1.fault != lastFault[0]) { sendDataOrCache(timeStr, 1, b1, envTemp, envHumi, "FAULT_EVENT"); lastFault[0] = b1.fault; }
  if (b2.fault != 0 && b2.fault != lastFault[1]) { sendDataOrCache(timeStr, 2, b2, envTemp, envHumi, "FAULT_EVENT"); lastFault[1] = b2.fault; }
  if (b3.fault != 0 && b3.fault != lastFault[2]) { sendDataOrCache(timeStr, 3, b3, envTemp, envHumi, "FAULT_EVENT"); lastFault[2] = b3.fault; }

  // 4. 動態定時採樣記錄
  if (isTimerDue) {
    lastLogTime = millis();
    String reason = isConnected ? "PERIODIC_5SEC" : "PERIODIC_1MIN_OFFLINE";
    sendDataOrCache(timeStr, 1, b1, envTemp, envHumi, reason);
    sendDataOrCache(timeStr, 2, b2, envTemp, envHumi, reason);
    sendDataOrCache(timeStr, 3, b3, envTemp, envHumi, reason);
  }

  delay(1000);
}
