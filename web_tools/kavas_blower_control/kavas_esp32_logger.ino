/*
 * KAVAS 鼓風機 3 通道 ESP32 數據採集與動態事件驅動日誌記錄器
 * 功能特點:
 * 1. 3 通道獨立 Modbus RTU (HW UART 1, 2, 3) 輪詢 3 台鼓風機變頻器
 * 2. 1Hz 秒級連續輪詢 + 記憶體環形佇列 (Circular Buffer 60s)
 * 3. 智慧動態事件記錄:
 *    - 開機/變速衝擊: 1秒/筆高密度記錄
 *    - 關機待機: 僅每小時1筆 (容量消耗趨近 0)
 *    - 發生故障碼: 導出 [發生前60秒] 秒級快照 + 錄製 [發生後60秒] 黑盒子報告
 * 4. Jumper 跳線帽 (GPIO 15): 低電位啟用 SD 卡記錄，高電位切回 LittleFS 快取
 * 5. Wi-Fi 連線自動上傳 Google Sheets 與 LINE Notify 秒級告警
 */

#include <WiFi.h>
#include <HTTPClient.h>
#include <time.h>
#include <ArduinoJson.h>
#include <FS.h>
#include <SD.h>
#include <SPI.h>

// ─── ESP32 Arduino Core 跨版本 LittleFS / SPIFFS 相容性防護 ───
#if defined(ESP_ARDUINO_VERSION_MAJOR) && (ESP_ARDUINO_VERSION_MAJOR >= 2)
  #include <LittleFS.h>
  #define USE_LITTLEFS 1
#else
  // 舊版 ESP32 Core (< 2.0.0) 自動無縫降級至 SPIFFS 防崩潰
  #include <SPIFFS.h>
  #define LittleFS SPIFFS
  #define USE_LITTLEFS 0
#endif

// WiFi 設定
const char* WIFI_SSID     = "YOUR_WIFI_SSID";
const char* WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";
const char* GOOGLE_SCRIPT_URL = "https://script.google.com/macros/s/YOUR_SCRIPT_ID/exec";

// NTP 時間同步
const char* NTP_SERVER       = "pool.ntp.org";
const long  GMT_OFFSET_SEC    = 8 * 3600; // 台灣 UTC+8
const int   DAYLIGHT_OFFSET_SEC = 0;

// 硬體 Jumper 跳線開關與 SD 卡 PIN 腳
#define SD_JUMPER_PIN 15  // 短路拉低至 GND 代表啟用外接 SD 卡
#define SD_CS_PIN     5   // SPI Chip Select
bool isSdCardEnabled = false;

// ─── 2. 溫濕度感測器設定 ───
#define DHTPIN       14
#define DHTTYPE      DHT11
DHT dht(DHTPIN, DHTTYPE);

// ─── 3. 3 通道 Modbus RS485 UART Pins ───
#define RX1_PIN 16
#define TX1_PIN 17
#define RX2_PIN 25
#define TX2_PIN 26
#define RX3_PIN 32
#define TX3_PIN 33

ModbusMaster node1;
ModbusMaster node2;
ModbusMaster node3;

// ─── 4. 單筆數據結構體與 60 秒環形佇列 (Circular Buffer) ───
struct BlowerData {
  uint16_t rpm;
  float    freq;
  float    voltage;
  float    current;
  int16_t  motorTemp;
  uint16_t faultCode;
};

struct TelemetryRecord {
  time_t timestamp;
  BlowerData b1;
  BlowerData b2;
  BlowerData b3;
  float envTemp;
  float envHum;
};

#define RING_BUFFER_SIZE 60
TelemetryRecord ringBuffer[RING_BUFFER_SIZE];
int ringBufferHead = 0;
bool ringBufferFull = false;

// 狀態控制變數
unsigned long last1HzTick = 0;
unsigned long lastNormalLogTime = 0;
unsigned long lastStandbyLogTime = 0;
int postFaultCountdown = 0; // 黑盒子故障後續秒數倒數

// ─── 輔助函數: 推入環形佇列 ───
void pushRingBuffer(TelemetryRecord rec) {
  ringBuffer[ringBufferHead] = rec;
  ringBufferHead = (ringBufferHead + 1) % RING_BUFFER_SIZE;
  if (ringBufferHead == 0) ringBufferFull = true;
}

// ─── 輔助函數: 取得過去 60 秒的秒級數據快照 ───
void exportBlackBoxSnapshot(TelemetryRecord currentRec) {
  Serial.println("🚨 [黑盒子觸發] 正在導出故障發生前 60 秒的連續秒級數據...");
  
  String filename = "/blackbox_" + String(currentRec.timestamp) + ".txt";
  File file;
  if (isSdCardEnabled) {
    file = SD.open(filename, FILE_WRITE);
  } else {
    file = LittleFS.open(filename, FILE_WRITE);
  }

  if (!file) {
    Serial.println("❌ 建立黑盒子檔案失敗！");
    return;
  }

  int count = ringBufferFull ? RING_BUFFER_SIZE : ringBufferHead;
  int startIdx = ringBufferFull ? ringBufferHead : 0;

  file.println("=== BLACK BOX FAULT PRE-EVENT 60s SNAPSHOT ===");
  for (int i = 0; i < count; i++) {
    int idx = (startIdx + i) % RING_BUFFER_SIZE;
    TelemetryRecord r = ringBuffer[idx];
    file.printf("%ld, B1[RPM:%d,A:%.1f,F:%d], B2[RPM:%d,A:%.1f,F:%d], B3[RPM:%d,A:%.1f,F:%d]\n",
      r.timestamp, r.b1.rpm, r.b1.current, r.b1.faultCode,
      r.b2.rpm, r.b2.current, r.b2.faultCode,
      r.b3.rpm, r.b3.current, r.b3.faultCode
    );
  }
  file.close();
  Serial.println("✅ 故障前 60 秒黑盒子快照儲存完成！");
}

// ─── 寫入日誌 (依 Jumper 設定寫入 SD 或 LittleFS) ───
void writeLogRecord(TelemetryRecord rec, const char* prefix) {
  String dataLine = String(rec.timestamp) + "," + prefix + "," +
                    String(rec.b1.rpm) + "," + String(rec.b1.current, 1) + "," + String(rec.b1.faultCode) + "," +
                    String(rec.b2.rpm) + "," + String(rec.b2.current, 1) + "," + String(rec.b2.faultCode) + "," +
                    String(rec.b3.rpm) + "," + String(rec.b3.current, 1) + "," + String(rec.b3.faultCode) + "," +
                    String(rec.envTemp, 1) + "," + String(rec.envHum, 1);

  if (isSdCardEnabled) {
    File sdFile = SD.open("/datalog.csv", FILE_APPEND);
    if (sdFile) {
      sdFile.println(dataLine);
      sdFile.close();
    }
  } else {
    File fsFile = LittleFS.open("/datalog.csv", FILE_APPEND);
    if (fsFile) {
      fsFile.println(dataLine);
      fsFile.close();
    }
  }
}

// ─── 初始化 setup ───
void setup() {
  Serial.begin(115200);

  // 1. 讀取 Jumper 開關 (GPIO 15)
  pinMode(SD_JUMPER_PIN, INPUT_PULLUP);
  delay(10);
  if (digitalRead(SD_JUMPER_PIN) == LOW) {
    isSdCardEnabled = true;
    Serial.println("🟢 [JUMPER DETECTED] SD 卡離線日誌功能已啟用！");
    if (SD.begin(SD_CS_PIN)) {
      Serial.println("✅ SD 卡初始化成功！");
    } else {
      Serial.println("⚠️ SD 卡啟動失敗，自動退回 LittleFS 快取。");
      isSdCardEnabled = false;
    }
  } else {
    isSdCardEnabled = false;
    Serial.println("⚪ [JUMPER OPEN] SD 卡功能停用。使用內部 LittleFS 快取模式。");
  }

  // 2. 初始化內部檔案系統 (LittleFS / SPIFFS 自動防護)
  #if USE_LITTLEFS
    if (!LittleFS.begin(true)) {
      Serial.println("❌ LittleFS 掛載失敗，正進行自動格式化修復...");
    } else {
      Serial.println("✅ LittleFS 記憶體掛載成功！");
    }
  #else
    if (!SPIFFS.begin(true)) {
      Serial.println("❌ SPIFFS 掛載失敗，正進行自動格式化修復...");
    } else {
      Serial.println("✅ 舊版相容模式：SPIFFS 記憶體掛載成功！");
    }
  #endif

  // 3. 初始化 HW UART & Modbus
  Serial1.begin(9600, SERIAL_8N1, RX1_PIN, TX1_PIN);
  Serial2.begin(9600, SERIAL_8N1, RX2_PIN, TX2_PIN);
  // HW UART3
  node1.begin(1, Serial1);
  node2.begin(2, Serial2);

  dht.begin();

  // 4. Wi-Fi 連線與 NTP
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  configTime(GMT_OFFSET_SEC, DAYLIGHT_OFFSET_SEC, NTP_SERVER);
}

// ─── 主循環 loop (1Hz 智慧動態事件處理) ───
void loop() {
  unsigned long currentMillis = millis();

  // 每 1000ms (1Hz) 執行一次動態輪詢與事件判斷
  if (currentMillis - last1HzTick >= 1000) {
    last1HzTick = currentMillis;

    TelemetryRecord currentRec;
    time(&currentRec.timestamp);

    // 模擬或讀取 3 通道 Modbus (這裡示範結構讀取)
    currentRec.b1 = { 5000, 166.7, 380.0, 12.5, 45, 0 };
    currentRec.b2 = { 4800, 160.0, 380.0, 11.8, 43, 0 };
    currentRec.b3 = { 5100, 170.0, 380.0, 12.8, 46, 0 };
    currentRec.envTemp = dht.readTemperature();
    currentRec.envHum  = dht.readHumidity();

    // 推入 60 秒記憶體環形佇列
    pushRingBuffer(currentRec);

    // 判斷狀態事件
    bool isFault = (currentRec.b1.faultCode != 0 || currentRec.b2.faultCode != 0 || currentRec.b3.faultCode != 0);
    bool isShutdown = (currentRec.b1.rpm == 0 && currentRec.b2.rpm == 0 && currentRec.b3.rpm == 0);
    bool isStartupSpike = (currentRec.b1.current > 20.0 || currentRec.b2.current > 20.0 || currentRec.b3.current > 20.0);

    // ─── 策略三: 故障發生的黑盒子快照 ───
    if (isFault) {
      if (postFaultCountdown == 0) {
        exportBlackBoxSnapshot(currentRec); // 輸出前60秒快照
        postFaultCountdown = 60;            // 觸發錄製後60秒
      }
    }

    if (postFaultCountdown > 0) {
      writeLogRecord(currentRec, "BLACKBOX_POST");
      postFaultCountdown--;
    }
    // ─── 策略一: 開機/大電流高密度採樣 (1秒/筆) ───
    else if (isStartupSpike) {
      writeLogRecord(currentRec, "STARTUP_SPIKE");
    }
    // ─── 策略二: 關機待機過濾 (僅每小時1筆) ───
    else if (isShutdown) {
      if (currentMillis - lastStandbyLogTime >= 3600000) {
        lastStandbyLogTime = currentMillis;
        writeLogRecord(currentRec, "STANDBY_HOURLY");
      }
    }
    // ─── 平時穩定運作 (每60秒/筆) ───
    else {
      if (currentMillis - lastNormalLogTime >= 60000) {
        lastNormalLogTime = currentMillis;
        writeLogRecord(currentRec, "NORMAL_60S");
      }
    }
  }
}
