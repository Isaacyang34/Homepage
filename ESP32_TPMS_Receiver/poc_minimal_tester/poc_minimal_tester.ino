/*
 * ==============================================================================
 * 專案名稱: ESP32 DevKit V1 (30-pin) + CC1101 433.92MHz + 0.91吋長條形 OLED 四輪胎壓接收系統
 * 【診斷模式 - DIAGNOSTIC BUILD】
 *
 * ⚠️ 重要：這一版的目的是「找出你四顆汽車用 TPMS 傳感器各自的 model/id」，
 *          不是最終產品版本。編譯前請務必確認：
 *
 *   1) Arduino IDE 目前使用的 rtl_433_ESP 函式庫，必須是【未裁減、原始 153 個
 *      解碼器全開】的版本，而不是之前為了鎖定 Truck 協定而客製化裁減過的版本
 *      （如果你已經把 rtl_433_devices.h 改成只剩 DECL(tpms_truck)，這個診斷
 *      版本會完全看不到你的汽車傳感器 —— 因為 library 底層根本沒有載入
 *      Schrader / EezTire / Renault 等其他解碼器，訊號連 rtl433Callback()
 *      都進不來，[RAW_JSON] 永遠不會出現）。
 *   2) 找出四顆傳感器各自的 (model, id) 之後，才進入「方向二」把 library 裁
 *      減到只保留你實際用到的那幾個解碼器 + 白名單比對，那時候再裁減才有意義。
 *
 * 核心功能:
 *   1) 0.91吋長條形 OLED (SSD1306 128x32) 四象限極簡清晰儀表
 *   2) 手機 WiFi 熱點 (TPMS_PoC_Tester) 射頻雷達與全功能管理後台
 *   3) rtl_433_ESP 內建 TPMS 協議解碼器 (Schrader/EezTire/Carchet...)
 *   4) CC1101 即時場強條、底噪峰值測量
 *   5) 訊號源白名單防干擾鎖定、手動位置配置、一鍵調胎、EEPROM 斷電記憶
 *   6) 線上 OTA 無線韌體更新 (/update)
 *   7) 【新增】診斷模式：不篩選 model，全量印出每一包解碼結果供人工比對
 * 引腳: CS=5, GDO0=2, SCK=18, MOSI=23, MISO=19, OLED SDA=21, SCL=22
 * ==============================================================================
 */

#include <Arduino.h>
#include <WiFi.h>
#include <WebServer.h>
#include <Update.h>
#include <EEPROM.h>
#include <Wire.h>
#include <ArduinoJson.h>
#include <ArduinoLog.h>
#define RF_CC1101
#define RF_MODULE_GDO0 2
#define RF_MODULE_CS   5
#define RF_MODULE_RECEIVER_GPIO 2
#include <rtl_433_ESP.h>       // 內含 RadioLib，必須在 Adafruit 之前
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

typedef WebServer WebServerClass;

#include "web_page.h"

// ------------------------------------------------------------------------------
// 硬體引腳定義 (ESP32 DevKit V1 30-pin)
// ------------------------------------------------------------------------------
#define PIN_OLED_SDA     21
#define PIN_OLED_SCL     22
#ifndef RF_MODULE_RECEIVER_GPIO
#define RF_MODULE_RECEIVER_GPIO 2
#endif
#ifndef RF_MODULE_FREQUENCY
#define RF_MODULE_FREQUENCY 433.92
#endif

#define SCREEN_WIDTH       128
#define SCREEN_HEIGHT      32
#define OLED_RESET         -1
#define SCREEN_ADDRESS     0x3C

#define MAX_LOG_RECORDS    20
#define MAX_DISCOVERED     8

#define EEPROM_MAGIC       0x54504D54 // "TPMT"
#define EEPROM_VERSION     4

#define JSON_MSG_BUFFER    512
char messageBuffer[JSON_MSG_BUFFER];

#define DECODE_MODE_NAME  "rtl_433 FSK [診斷模式：全協議開放觀察]"

// ------------------------------------------------------------------------------
// 【診斷模式開關】
// 設 1：不管 model 是什麼、有沒有溫度欄位，都盡量印出來，方便你人工比對找出
//       四顆傳感器各自的 model/id。
// 之後正式鎖定四顆白名單後，把這裡改回 0，並在下面 WHITELIST 陣列填入四組
// (model, id)，程式就會恢復嚴格過濾。
// ------------------------------------------------------------------------------
#define DIAGNOSTIC_MODE 1

// ------------------------------------------------------------------------------
// 資料結構定義 (置於所有函式之前，確保 Arduino 前置處理器正確生成原型)
// ------------------------------------------------------------------------------
struct SystemConfig {
  uint32_t magic;
  uint8_t version;
  bool lockWhitelist;
  uint32_t sensorIds[4];
  uint8_t minHits;
  int8_t minRssi;
  uint8_t checksum;
};

struct TireData {
  uint32_t sensorId;
  float pressurePsi;
  float pressureBar;
  int tempC;
  bool lowBattery;
  bool valid;
  unsigned long lastSeenMs;
};

struct PacketRecord {
  uint32_t id;
  uint32_t timestampMs;
  float rssi;
  uint8_t len;
  bool filtered;
  char hexString[64 * 3 + 1];
};

struct DiscoveredSensor {
  uint32_t sensorId;
  float rssi;
  float psi;
  int tempC;
  uint32_t packetCount;
  unsigned long lastSeenMs;
  char model[32];   // 【新增】記錄是哪個協議解出來的，方便你比對四顆各自的model
};

// 白名單（診斷完成、確定四顆傳感器的 model/id 後，填入這裡，並把
// DIAGNOSTIC_MODE 改回 0）
struct WhitelistEntry {
  const char* model;
  uint32_t id;
};
WhitelistEntry WHITELIST[4] = {
  { "", 0 },  // TODO: 填入輪位1 例如 {"Schrader", 0x1A2B3C}
  { "", 0 },  // TODO: 填入輪位2
  { "", 0 },  // TODO: 填入輪位3
  { "", 0 },  // TODO: 填入輪位4
};

bool isWhitelisted(const char* model, uint32_t id) {
  for (int i = 0; i < 4; i++) {
    if (WHITELIST[i].id != 0 &&
        strcmp(model, WHITELIST[i].model) == 0 &&
        WHITELIST[i].id == id) {
      return true;
    }
  }
  return false;
}

float currentRssi   = -110.0f;
float peakRssi      = -110.0f;
unsigned long peakTimeMs = 0;
bool surgeDetected  = false;

// ------------------------------------------------------------------------------
// 全域物件與變數
// ------------------------------------------------------------------------------
PacketRecord packetHistory[MAX_LOG_RECORDS];
int historyHead = 0;
int historyCount = 0;
uint32_t totalPacketsCount = 0;

volatile unsigned long lastRawPulseMs = 0;
volatile unsigned int lastRawPulseCount = 0;
volatile int lastRawPulseRssi = -110;
volatile unsigned long lastRawDurationUs = 0;

// ------------------------------------------------------------------------------
// 原始射頻時序與 HEX 電文即時轉譯（僅供人眼參考，非真正解碼路徑，勿用來手動解碼）
// ------------------------------------------------------------------------------
void dumpPulseBitsHex(const int* pulse_us, const int* gap_us, unsigned int num_pulses, int rssi, unsigned long duration_us) {
  String timing = "[TIMING_RAW] n=" + String(num_pulses) + " dur=" + String(duration_us / 1000.0f, 1) + "ms ";
  unsigned int sampleCount = (num_pulses < 160) ? num_pulses : 160;
  for (unsigned int i = 0; i < sampleCount; i++) {
    timing += "+" + String(pulse_us[i]) + "-" + String(gap_us[i]) + " ";
  }
  Serial.println(timing);

  int bitWidth = 52;
  // 動態基頻估算：尋找最密集之短脈衝寬度，不再硬編碼 52us
  if (num_pulses > 8) {
    int shortPulseSum = 0;
    int shortPulseCount = 0;
    for (unsigned int k = 0; k < num_pulses; k++) {
      if (pulse_us[k] >= 25 && pulse_us[k] <= 160) {
        shortPulseSum += pulse_us[k];
        shortPulseCount++;
      }
    }
    if (shortPulseCount > 4) {
      int avgShort = shortPulseSum / shortPulseCount;
      if (avgShort >= 35 && avgShort <= 65) bitWidth = 52;
      else if (avgShort > 65 && avgShort <= 95) bitWidth = 80;
      else if (avgShort > 95 && avgShort <= 150) bitWidth = 104;
    }
  }

  uint8_t bitBuf[40];
  memset(bitBuf, 0, sizeof(bitBuf));
  unsigned int totalBits = 0;

  for (unsigned int i = 0; i < num_pulses && totalBits < 320; i++) {
    int pBits = (pulse_us[i] + (bitWidth / 2)) / bitWidth;
    if (pBits < 1) pBits = 1;
    if (pBits > 8) pBits = 8;
    for (int b = 0; b < pBits && totalBits < 320; b++) {
      bitBuf[totalBits / 8] |= (1 << (7 - (totalBits % 8)));
      totalBits++;
    }
    int gBits = (gap_us[i] + (bitWidth / 2)) / bitWidth;
    if (gBits < 1) gBits = 1;
    if (gBits > 8) gBits = 8;
    totalBits += gBits;
  }

  unsigned int byteCount = (totalBits + 7) / 8;
  if (byteCount > 32) byteCount = 32;
  if (byteCount < 4 && num_pulses >= 20) byteCount = 4;
  String hexStr = "[RAW_BITS_HEX] len=" + String(byteCount) + " bytes (" + String(totalBits) + " bits):";
  char bHex[8];
  for (unsigned int i = 0; i < byteCount; i++) {
    snprintf(bHex, sizeof(bHex), " %02X", bitBuf[i]);
    hexStr += bHex;
  }
  Serial.println(hexStr);
}

// ------------------------------------------------------------------------------
// 調變切換與上位機指令控制
// ------------------------------------------------------------------------------
extern rtl_433_ESP rf;
bool autoSeekModulation = false; // 預設固定單軌 2-FSK 模式，絕不自動跳回 OOK
unsigned long lastModSwitchMs = 0;
const unsigned long MOD_SWITCH_INTERVAL_MS = 6000; // 手動啟動輪詢時每 6 秒自動輪替 OOK ⇄ 2-FSK
String serialCmdBuffer = "";

int dynMinPulses = 8;
int dynMinRssi = -88;

// ------------------------------------------------------------------------------
// 原廠 KINICA TPMS 主機 UART 監聽模組 (GPIO 16=RX2, GPIO 17=TX2)
// ------------------------------------------------------------------------------
#define PIN_HOST_RX 16
#define PIN_HOST_TX 17
uint32_t hostBaudRate = 9600;
uint32_t totalHostUartBytes = 0;
uint8_t hostUartBuf[256];
size_t hostUartLen = 0;
unsigned long lastHostUartByteMs = 0;

void setupHostUart(uint32_t baud) {
  hostBaudRate = baud;
  Serial2.begin(baud, SERIAL_8N1, PIN_HOST_RX, PIN_HOST_TX);
  Serial.printf("[HOST_UART] 監聽已啟動: RX=GPIO%d, TX=GPIO%d, Baud=%u, TotalBytes=%u\n", PIN_HOST_RX, PIN_HOST_TX, baud, totalHostUartBytes);
}

void processHostUart() {
  while (Serial2.available()) {
    uint8_t b = Serial2.read();
    totalHostUartBytes++;
    if (hostUartLen < sizeof(hostUartBuf)) {
      hostUartBuf[hostUartLen++] = b;
    }
    lastHostUartByteMs = millis();
  }

  // 封包間隔超時（15ms 無新位元組即視為一包完整封包）
  if (hostUartLen > 0 && (millis() - lastHostUartByteMs >= 15)) {
    String hexStr = "";
    String asciiStr = "";
    char bHex[8];
    for (size_t i = 0; i < hostUartLen; i++) {
      snprintf(bHex, sizeof(bHex), "%02X ", hostUartBuf[i]);
      hexStr += bHex;
      if (hostUartBuf[i] >= 32 && hostUartBuf[i] <= 126) {
        asciiStr += (char)hostUartBuf[i];
      } else {
        asciiStr += '.';
      }
    }
    Serial.printf("[HOST_UART] len=%u HEX: %s| ASCII: %s\n", hostUartLen, hexStr.c_str(), asciiStr.c_str());
    hostUartLen = 0;
  }
}

void switchModulation(bool toOok, const char* reason) {
  rtl_433_ESP::ookModulation = toOok;
  rf.initReceiver(RF_MODULE_RECEIVER_GPIO, RF_MODULE_FREQUENCY);
  rf.enableReceiver();
  if (toOok) {
    Serial.printf("[CMD_ACK] 調變已切換至 OOK 模式 (%s)\n", reason);
  } else {
    Serial.printf("[CMD_ACK] 調變已切換至 2-FSK 模式 (%s)\n", reason);
  }
}

void manageAutoSeek() {
  if (!autoSeekModulation) return;
  if (millis() - lastModSwitchMs >= MOD_SWITCH_INTERVAL_MS) {
    lastModSwitchMs = millis();
    bool nextIsOok = !rtl_433_ESP::ookModulation;
    switchModulation(nextIsOok, "自動輪詢切換");
  }
}

void handleSerialCommand() {
  while (Serial.available()) {
    char c = (char)Serial.read();
    if (c == '\n' || c == '\r') {
      if (serialCmdBuffer.length() > 0) {
        serialCmdBuffer.trim();
        if (serialCmdBuffer == "CMD:LOCK_FSK" || serialCmdBuffer == "CMD:SET_MOD:FSK") {
          autoSeekModulation = false;
          switchModulation(false, "使用者指令固定 FSK");
        } else if (serialCmdBuffer == "CMD:LOCK_OOK" || serialCmdBuffer == "CMD:SET_MOD:OOK") {
          autoSeekModulation = false;
          switchModulation(true, "使用者指令固定 OOK");
        } else if (serialCmdBuffer == "CMD:AUTO_SCAN" || serialCmdBuffer == "CMD:SET_MOD:AUTO") {
          autoSeekModulation = true;
          lastModSwitchMs = millis();
          Serial.println("[CMD_ACK] 已啟動 OOK / 2-FSK 自動輪詢探索模式 (每 8 秒自動輪替)");
        } else if (serialCmdBuffer.startsWith("CMD:SET_PULSE_MIN:")) {
          dynMinPulses = serialCmdBuffer.substring(18).toInt();
          Serial.printf("[CMD_ACK] 脈衝門檻已更新為: %d\n", dynMinPulses);
        } else if (serialCmdBuffer.startsWith("CMD:SET_RSSI_MIN:")) {
          dynMinRssi = serialCmdBuffer.substring(17).toInt();
          Serial.printf("[CMD_ACK] 場強門檻已更新為: %d dBm\n", dynMinRssi);
        } else if (serialCmdBuffer.startsWith("CMD:HOST_BAUD:")) {
          uint32_t nb = serialCmdBuffer.substring(14).toInt();
          if (nb >= 1200 && nb <= 230400) {
            setupHostUart(nb);
          }
        } else if (serialCmdBuffer == "CMD:REBOOT") {
          Serial.println("[CMD_ACK] 執行硬體重啟...");
          delay(100);
          ESP.restart();
        }
        serialCmdBuffer = "";
      }
    } else {
      if (serialCmdBuffer.length() < 64) {
        serialCmdBuffer += c;
      }
    }
  }
}

void rtl_433_RawCallback(const int* pulse_us, const int* gap_us,
                         unsigned int num_pulses, unsigned long duration_us,
                         int rssi) {
  lastRawPulseMs = millis();
  lastRawPulseCount = num_pulses;
  lastRawPulseRssi = rssi;
  lastRawDurationUs = duration_us;

  if (rssi > -88) {
    currentRssi = (float)rssi;
    if (currentRssi > peakRssi) {
      peakRssi = currentRssi;
      peakTimeMs = millis();
    }
    surgeDetected = (rssi > -75);

    const char* curMod = rtl_433_ESP::ookModulation ? "OOK" : "FSK";
    if (rssi >= dynMinRssi) {
      Serial.printf("[RAW_PULSE] rssi=%d,pulses=%u,duration_ms=%.1f,mod=%s\n",
                    rssi, num_pulses, duration_us / 1000.0f, curMod);
    }
    if (rssi >= dynMinRssi && num_pulses >= dynMinPulses) {
      dumpPulseBitsHex(pulse_us, gap_us, num_pulses, rssi, duration_us);
    }
  }
}

rtl_433_ESP rf;
WebServerClass server(80);
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);

SystemConfig config;
bool oledOnline = false;
bool radioOnline = false;

TireData tires[4] = {
  {0, 0.0f, 0.0f, 0, false, false, 0},
  {0, 0.0f, 0.0f, 0, false, false, 0},
  {0, 0.0f, 0.0f, 0, false, false, 0},
  {0, 0.0f, 0.0f, 0, false, false, 0}
};

const char* tireNames[4] = {"FL", "FR", "RL", "RR"};
const char* tireLabels[4] = {"左前輪", "右前輪", "左後輪", "右後輪"};

DiscoveredSensor discoveredSensors[MAX_DISCOVERED];
int discoveredCount = 0;

// ------------------------------------------------------------------------------
// EEPROM 儲存與讀取函式
// ------------------------------------------------------------------------------
uint8_t calculateChecksum(const SystemConfig& cfg) {
  uint8_t sum = 0;
  const uint8_t* p = (const uint8_t*)&cfg;
  for (size_t i = 0; i < (sizeof(SystemConfig) - sizeof(uint8_t)); i++) {
    sum += p[i];
  }
  return sum;
}

void loadConfigFromEEPROM() {
  EEPROM.begin(128);
  EEPROM.get(0, config);
  if (config.magic != EEPROM_MAGIC || config.version != EEPROM_VERSION || config.checksum != calculateChecksum(config)) {
    Serial.println("[EEPROM] 初次建立設定檔，使用預設值 (重置乾淨狀態)...");
    config.magic = EEPROM_MAGIC;
    config.version = EEPROM_VERSION;
    config.lockWhitelist = false;
    config.minHits = 2;
    config.minRssi = -90;
    for (int i = 0; i < 4; i++) {
      config.sensorIds[i] = 0;
      tires[i].sensorId = 0;
      tires[i].pressurePsi = 0.0f;
      tires[i].pressureBar = 0.00f;
      tires[i].tempC = 0;
      tires[i].valid = false;
    }
    config.checksum = calculateChecksum(config);
    EEPROM.put(0, config);
    EEPROM.commit();
  } else {
    Serial.println("[EEPROM] 設定檔讀取成功!");
    Serial.printf("  防干擾白名單: %s | 最小命中門檻: %d 次 | 最小 RSSI: %d dBm\n",
                  config.lockWhitelist ? "已鎖定 (過濾外部訊號)" : "未鎖定 (開放學習)",
                  config.minHits, config.minRssi);
    for (int i = 0; i < 4; i++) {
      tires[i].sensorId = config.sensorIds[i];
      if (tires[i].sensorId != 0) {
        Serial.printf("  輪位 %s (%s): 0x%08X\n", tireNames[i], tireLabels[i], tires[i].sensorId);
      }
    }
  }
}

void saveConfigToEEPROM() {
  for (int i = 0; i < 4; i++) {
    config.sensorIds[i] = tires[i].sensorId;
  }
  config.checksum = calculateChecksum(config);
  EEPROM.put(0, config);
  EEPROM.commit();
  Serial.println("[EEPROM] 設定已成功寫入 Flash 儲存!");
}

// ------------------------------------------------------------------------------
// 探索感測器管理（現在會多記一個 model 字串，方便你比對哪個ID對應哪個協議）
// ------------------------------------------------------------------------------
int updateDiscoveredSensor(uint32_t id, float rssi, float psi, int tempC, const char* model) {
  for (int i = 0; i < discoveredCount; i++) {
    if (discoveredSensors[i].sensorId == id) {
      discoveredSensors[i].rssi = rssi;
      discoveredSensors[i].psi = psi;
      discoveredSensors[i].tempC = tempC;
      discoveredSensors[i].packetCount++;
      discoveredSensors[i].lastSeenMs = millis();
      strncpy(discoveredSensors[i].model, model, sizeof(discoveredSensors[i].model) - 1);
      return discoveredSensors[i].packetCount;
    }
  }
  int idx;
  if (discoveredCount < MAX_DISCOVERED) {
    idx = discoveredCount;
    discoveredCount++;
  } else {
    idx = 0;
    unsigned long oldestTime = discoveredSensors[0].lastSeenMs;
    for (int i = 1; i < MAX_DISCOVERED; i++) {
      if (discoveredSensors[i].lastSeenMs < oldestTime) {
        oldestTime = discoveredSensors[i].lastSeenMs;
        idx = i;
      }
    }
  }
  discoveredSensors[idx].sensorId = id;
  discoveredSensors[idx].rssi = rssi;
  discoveredSensors[idx].psi = psi;
  discoveredSensors[idx].tempC = tempC;
  discoveredSensors[idx].packetCount = 1;
  discoveredSensors[idx].lastSeenMs = millis();
  strncpy(discoveredSensors[idx].model, model, sizeof(discoveredSensors[idx].model) - 1);
  discoveredSensors[idx].model[sizeof(discoveredSensors[idx].model) - 1] = '\0';
  return 1;
}

// ------------------------------------------------------------------------------
// 感測器 ID 合法度檢驗
// ------------------------------------------------------------------------------
bool isValidSensorId(uint32_t id) {
  if (id == 0x00000000 || id == 0xFFFFFFFF) return false;
  if (id == 0x55555555 || id == 0xAAAAAAAA) return false;
  if (id == 0x01010101 || id == 0x11111111) return false;

  uint32_t v = id;
  v = v - ((v >> 1) & 0x55555555);
  v = (v & 0x33333333) + ((v >> 2) & 0x33333333);
  int ones = (((v + (v >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
  if (ones < 8 || ones > 24) return false;

  uint8_t b[4] = {
    (uint8_t)((id >> 24) & 0xFF),
    (uint8_t)((id >> 16) & 0xFF),
    (uint8_t)((id >> 8) & 0xFF),
    (uint8_t)(id & 0xFF)
  };
  int ffCount = 0, zeroCount = 0;
  for (int i = 0; i < 4; i++) {
    if (b[i] == 0xFF) ffCount++;
    if (b[i] == 0x00) zeroCount++;
  }
  if (ffCount >= 2 || zeroCount >= 2) return false;

  return true;
}

uint32_t parseSensorId(const String& str) {
  String s = str;
  s.trim();
  if (s.startsWith("0x") || s.startsWith("0X")) {
    s = s.substring(2);
  }
  return (uint32_t)strtoul(s.c_str(), NULL, 16);
}

// ------------------------------------------------------------------------------
// rtl_433_ESP 回調函式：由 rtl_433 內部解碼器解析完成後呼叫，傳入 JSON 字串
// 【診斷模式核心邏輯】
// ------------------------------------------------------------------------------
void rtl433Callback(char* message) {
  totalPacketsCount++;
  Serial.printf("[RAW_JSON] %s\n", message);

  JsonDocument doc;
  DeserializationError err = deserializeJson(doc, message);
  if (err) {
    Serial.printf("[rtl433] JSON 解析失敗: %s\n", err.c_str());
    return;
  }

  const char* model = doc["model"] | "unknown";

  float rssi = (float)(int)doc["rssi"];
  if (rssi >= -125.0f && rssi <= -25.0f) {
    currentRssi = rssi;
    if (rssi > peakRssi) { peakRssi = rssi; peakTimeMs = millis(); }
    surgeDetected = (rssi > -65.0f);
  }

  // 提取感測器 ID（先取出來，這樣不管後面是否被過濾，都能在 log 看到 id）
  uint32_t sensorId = 0;
  if (doc["id"].is<const char*>()) {
    sensorId = parseSensorId(String(doc["id"].as<const char*>()));
  } else if (doc["id"].is<uint32_t>()) {
    sensorId = doc["id"].as<uint32_t>();
  } else {
    sensorId = parseSensorId(doc["id"].as<String>());
  }

#if DIAGNOSTIC_MODE
  // 診斷模式：完全不管 model 是什麼，一律往下處理、一律印出，方便你比對
  // 四顆汽車傳感器各自觸發的是哪個 model + id。
  Serial.printf("[觀察] model=%s id=0x%08X rssi=%.1f\n", model, sensorId, rssi);
#else
  // 正式模式：只放行白名單裡登記過的四組 (model, id)
  if (!isWhitelisted(model, sensorId)) {
    Serial.printf("[rtl433] 忽略非白名單協議 model=%s id=0x%08X\n", model, sensorId);
    return;
  }
#endif

  // 提取胎壓數值 (自適應 kPa / PSI / bar)
  float psi = 0.0f;
  bool hasPressure = false;
  if (doc["pressure_kPa"].is<float>() || doc["pressure_kPa"].is<double>()) {
    psi = (float)(double)doc["pressure_kPa"] * 0.14503f;
    hasPressure = true;
  } else if (doc["pressure_PSI"].is<float>() || doc["pressure_PSI"].is<double>()) {
    psi = (float)(double)doc["pressure_PSI"];
    hasPressure = true;
  } else if (doc["pressure_bar"].is<float>() || doc["pressure_bar"].is<double>()) {
    psi = (float)(double)doc["pressure_bar"] * 14.5038f;
    hasPressure = true;
  }

  // 提取溫度 (自適應 C / F)
  int tempC = -999;
  bool hasTemp = false;
  if (doc["temperature_C"].is<float>() || doc["temperature_C"].is<double>() || doc["temperature_C"].is<int>()) {
    tempC = (int)(double)doc["temperature_C"];
    hasTemp = true;
  } else if (doc["temperature_F"].is<float>() || doc["temperature_F"].is<double>() || doc["temperature_F"].is<int>()) {
    tempC = (int)(((double)doc["temperature_F"] - 32.0) * 5.0 / 9.0);
    hasTemp = true;
  }

#if DIAGNOSTIC_MODE
  // 診斷模式下，即使缺少壓力或溫度欄位，也不要直接 return，
  // 印出來讓你知道「這個協議根本沒有這個欄位」也是有用的資訊。
  if (!hasPressure) {
    Serial.printf("[觀察] model=%s id=0x%08X 這包沒有壓力欄位\n", model, sensorId);
  }
  if (!hasTemp) {
    Serial.printf("[觀察] model=%s id=0x%08X 這包沒有溫度欄位\n", model, sensorId);
  }
#else
  if (!hasTemp) {
    Serial.printf("[rtl433] 格式過濾 - 封包缺少溫度欄位 ID=0x%08X\n", sensorId);
    return;
  }
#endif

  bool lowBat = !((bool)doc["battery_ok"] | false);

#if !DIAGNOSTIC_MODE
  // 正式模式才做物理範圍過濾；診斷階段先全部放行，避免漏掉真實但數值略偏的封包
  if (psi < -2.0f || psi > 120.0f) {
    Serial.printf("[rtl433] 物理過濾 - 胎壓異常 %.1f psi ID=0x%08X\n", psi, sensorId);
    return;
  }
  if (tempC < -30 || tempC > 90) {
    Serial.printf("[rtl433] 物理過濾 - 溫度異常 %d C ID=0x%08X\n", tempC, sensorId);
    return;
  }
  if (!isValidSensorId(sensorId)) {
    Serial.printf("[rtl433] ID 非法 0x%08X\n", sensorId);
    return;
  }
  if (rssi < (float)config.minRssi) return;
#endif

  // 更新探索學習池（診斷模式下這裡會累積所有出現過的 model+id，開 Web 後台
  // 的「探索感測器」清單就能看到目前為止偵測到的所有候選）
  int hitCount = updateDiscoveredSensor(sensorId, rssi, psi, tempC, model);

  Serial.printf("[TPMS_DATA] model=%s,id=0x%08X,psi=%.1f,bar=%.2f,temp=%d,rssi=%.1f,hit=%d\n",
                model, sensorId, psi, psi * 0.0689476f, tempC, rssi, hitCount);

#if DIAGNOSTIC_MODE
  // 診斷模式到此為止，不寫入四輪、不更新 OLED——因為還沒有白名單，
  // 不知道這顆該算哪一輪。等你確認四組 (model,id) 後填入 WHITELIST 並關閉
  // DIAGNOSTIC_MODE，才會進到下面的四輪綁定邏輯。
  return;
#endif

  // 記錄封包日誌
  packetHistory[historyHead].id = totalPacketsCount;
  packetHistory[historyHead].timestampMs = millis();
  packetHistory[historyHead].rssi = rssi;
  packetHistory[historyHead].len = 8;
  packetHistory[historyHead].filtered = false;
  snprintf(packetHistory[historyHead].hexString,
           sizeof(packetHistory[historyHead].hexString),
           "ID:%08X P:%.1fpsi T:%dC", sensorId, psi, tempC);
  historyHead = (historyHead + 1) % MAX_LOG_RECORDS;
  if (historyCount < MAX_LOG_RECORDS) historyCount++;

  int targetIndex = -1;
  for (int i = 0; i < 4; i++) {
    if (tires[i].sensorId == sensorId && sensorId != 0) {
      targetIndex = i;
      break;
    }
  }

  if (config.lockWhitelist && targetIndex == -1) {
    Serial.printf("[防干擾] 攔截未授權感測器 ID: 0x%08X\n", sensorId);
    return;
  }

  if (targetIndex != -1) {
    tires[targetIndex].pressurePsi = psi;
    tires[targetIndex].pressureBar = psi * 0.0689476f;
    tires[targetIndex].tempC = tempC;
    tires[targetIndex].lowBattery = lowBat;
    tires[targetIndex].valid = true;
    tires[targetIndex].lastSeenMs = millis();
    renderOLED();
  }
}

// ------------------------------------------------------------------------------
// OLED 繪製函式 - 0.91 吋長條形 (128x32)
// ------------------------------------------------------------------------------
void renderOLED() {
  if (!oledOnline) return;

  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);
  display.drawFastVLine(63, 0, 32, SSD1306_WHITE);
  display.drawFastHLine(0, 15, 128, SSD1306_WHITE);

  unsigned long now = millis();

  display.setCursor(2, 4);
  if (tires[0].valid && (now - tires[0].lastSeenMs < 900000)) {
    display.printf("FL:%.1f", tires[0].pressurePsi);
    display.setCursor(44, 4);
    display.printf("%dC", tires[0].tempC);
  } else {
    display.print("FL:--.- --C");
  }

  display.setCursor(66, 4);
  if (tires[1].valid && (now - tires[1].lastSeenMs < 900000)) {
    display.printf("FR:%.1f", tires[1].pressurePsi);
    display.setCursor(108, 4);
    display.printf("%dC", tires[1].tempC);
  } else {
    display.print("FR:--.- --C");
  }

  display.setCursor(2, 20);
  if (tires[2].valid && (now - tires[2].lastSeenMs < 900000)) {
    display.printf("RL:%.1f", tires[2].pressurePsi);
    display.setCursor(44, 20);
    display.printf("%dC", tires[2].tempC);
  } else {
    display.print("RL:--.- --C");
  }

  display.setCursor(66, 20);
  if (tires[3].valid && (now - tires[3].lastSeenMs < 900000)) {
    display.printf("RR:%.1f", tires[3].pressurePsi);
    display.setCursor(108, 20);
    display.printf("%dC", tires[3].tempC);
  } else {
    display.print("RR:--.- --C");
  }

  display.display();
}

// ------------------------------------------------------------------------------
// WebServer 路由處理
// ------------------------------------------------------------------------------
void handleRoot() {
  server.sendHeader("Cache-Control", "no-cache, no-store, must-revalidate");
  server.sendHeader("Pragma", "no-cache");
  server.sendHeader("Expires", "-1");
  server.send(200, "text/html", PAGE_HTML);
}

void handleApiData() {
  String json = "{";
  json += "\"rssi\":" + String(currentRssi, 1) + ",";
  json += "\"peakRssi\":" + String(peakRssi, 1) + ",";
  json += "\"scanMode\":\"" DECODE_MODE_NAME "\",";
  json += "\"surge\":" + String(surgeDetected ? "true" : "false") + ",";
  json += "\"totalPackets\":" + String(totalPacketsCount) + ",";
  json += "\"freeHeap\":" + String(ESP.getFreeHeap()) + ",";
  json += "\"rf\":" + String(radioOnline ? "true" : "false") + ",";
  json += "\"oled\":" + String(oledOnline ? "true" : "false") + ",";

  json += "\"sys\":{";
  json += "\"version\":\"v2.9.0-diag\",";
  json += "\"freeHeap\":" + String(ESP.getFreeHeap()) + ",";
  json += "\"flashSize\":" + String(ESP.getFlashChipSize()) + ",";
  json += "\"chipId\":\"0x" + String((uint32_t)(ESP.getEfuseMac() & 0xFFFFFFFF), HEX) + "\"";
  json += "},";

  json += "\"config\":{";
  json += "\"lockWhitelist\":" + String(config.lockWhitelist ? "true" : "false") + ",";
  json += "\"minHits\":" + String(config.minHits) + ",";
  json += "\"minRssi\":" + String(config.minRssi);
  json += "},";

  json += "\"tires\":[";
  for (int i = 0; i < 4; i++) {
    json += "{";
    json += "\"id\":" + String(tires[i].sensorId) + ",";
    json += "\"psi\":" + String(tires[i].pressurePsi, 1) + ",";
    json += "\"bar\":" + String(tires[i].pressureBar, 2) + ",";
    json += "\"temp\":" + String(tires[i].tempC) + ",";
    json += "\"valid\":" + String(tires[i].valid ? "true" : "false");
    json += (i < 3) ? "}," : "}";
  }
  json += "],";

  // 【診斷用】探索池現在多帶一個 model 欄位，方便你在網頁上直接看到
  // 目前為止偵測到哪些 (model,id) 組合、各出現了幾次
  json += "\"discovered\":[";
  int dCount = 0;
  for (int i = 0; i < discoveredCount; i++) {
    if (dCount > 0) json += ",";
    json += "{";
    json += "\"id\":" + String(discoveredSensors[i].sensorId) + ",";
    json += "\"model\":\"" + String(discoveredSensors[i].model) + "\",";
    json += "\"rssi\":" + String(discoveredSensors[i].rssi, 1) + ",";
    json += "\"psi\":" + String(discoveredSensors[i].psi, 1) + ",";
    json += "\"temp\":" + String(discoveredSensors[i].tempC) + ",";
    json += "\"count\":" + String(discoveredSensors[i].packetCount);
    json += "}";
    dCount++;
  }
  json += "]}";

  server.send(200, "application/json", json);
}

void handleApiLogs() {
  String json = "{\"logs\":[";
  int count = 0;
  for (int i = 0; i < historyCount; i++) {
    int idx = (historyHead - 1 - i + MAX_LOG_RECORDS) % MAX_LOG_RECORDS;
    if (count > 0) json += ",";
    json += "{";
    json += "\"id\":" + String(packetHistory[idx].id) + ",";
    json += "\"rssi\":" + String(packetHistory[idx].rssi, 1) + ",";
    json += "\"len\":" + String(packetHistory[idx].len) + ",";
    json += "\"filtered\":" + String(packetHistory[idx].filtered ? "true" : "false") + ",";
    json += "\"hex\":\"" + String(packetHistory[idx].hexString) + "\"";
    json += "}";
    count++;
  }
  json += "]}";
  server.send(200, "application/json", json);
}

void handleApiConfig() {
  if (server.hasArg("lock")) {
    config.lockWhitelist = (server.arg("lock") == "1" || server.arg("lock") == "true");
  }
  if (server.hasArg("hits")) {
    int h = server.arg("hits").toInt();
    if (h >= 1 && h <= 10) config.minHits = h;
  }
  if (server.hasArg("rssi")) {
    int r = server.arg("rssi").toInt();
    if (r >= -120 && r <= -40) config.minRssi = r;
  }
  saveConfigToEEPROM();
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiClearDiscovered() {
  discoveredCount = 0;
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiClearAllTires() {
  for (int i = 0; i < 4; i++) {
    tires[i].sensorId = 0;
    tires[i].valid = false;
    tires[i].pressurePsi = 0.0f;
    tires[i].pressureBar = 0.0f;
    tires[i].tempC = 0;
  }
  saveConfigToEEPROM();
  renderOLED();
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiBind() {
  if (server.hasArg("pos") && server.hasArg("id")) {
    int pos = server.arg("pos").toInt();
    if (pos >= 0 && pos < 4) {
      uint32_t newId = parseSensorId(server.arg("id"));
      tires[pos].sensorId = newId;
      bool inherited = false;
      for (int d = 0; d < discoveredCount; d++) {
        if (discoveredSensors[d].sensorId == newId) {
          tires[pos].pressurePsi = discoveredSensors[d].psi;
          tires[pos].pressureBar = discoveredSensors[d].psi * 0.0689476f;
          tires[pos].tempC = discoveredSensors[d].tempC;
          tires[pos].valid = true;
          tires[pos].lastSeenMs = millis();
          inherited = true;
          break;
        }
      }
      saveConfigToEEPROM();
      renderOLED();
      server.send(200, "application/json", "{\"status\":\"ok\"}");
      return;
    }
  }
  server.send(400, "application/json", "{\"status\":\"error\",\"msg\":\"invalid params\"}");
}

void handleApiClearSlot() {
  if (server.hasArg("pos")) {
    int pos = server.arg("pos").toInt();
    if (pos >= 0 && pos < 4) {
      tires[pos].sensorId = 0;
      tires[pos].valid = false;
      tires[pos].pressurePsi = 0.0f;
      tires[pos].pressureBar = 0.0f;
      saveConfigToEEPROM();
      renderOLED();
      server.send(200, "application/json", "{\"status\":\"ok\"}");
      return;
    }
  }
  server.send(400, "application/json", "{\"status\":\"error\"}");
}

void swapTwoTires(int a, int b) {
  TireData temp = tires[a];
  tires[a] = tires[b];
  tires[b] = temp;
}

void handleApiSwap() {
  String mode = server.arg("mode");
  if (mode == "front_back") {
    swapTwoTires(0, 2);
    swapTwoTires(1, 3);
    saveConfigToEEPROM();
  } else if (mode == "left_right") {
    swapTwoTires(0, 1);
    swapTwoTires(2, 3);
    saveConfigToEEPROM();
  } else if (mode == "cross") {
    swapTwoTires(0, 3);
    swapTwoTires(1, 2);
    saveConfigToEEPROM();
  }
  renderOLED();
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiClear() {
  historyHead = 0;
  historyCount = 0;
  discoveredCount = 0;
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiResetConfig() {
  config.magic = EEPROM_MAGIC;
  config.version = EEPROM_VERSION;
  config.lockWhitelist = false;
  for (int i = 0; i < 4; i++) {
    config.sensorIds[i] = 0;
    tires[i].sensorId = 0;
    tires[i].valid = false;
    tires[i].pressurePsi = 0.0f;
    tires[i].pressureBar = 0.0f;
    tires[i].tempC = 0;
  }
  saveConfigToEEPROM();
  discoveredCount = 0;
  historyHead = 0;
  historyCount = 0;
  totalPacketsCount = 0;
  renderOLED();
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

// ------------------------------------------------------------------------------
// Setup
// ------------------------------------------------------------------------------
void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println("\n==================================================");
  Serial.println("   ESP32 + CC1101 + rtl_433_ESP 胎壓接收系統【診斷版】");
#if DIAGNOSTIC_MODE
  Serial.println("   >>> DIAGNOSTIC_MODE = 1，全協議開放觀察中 <<<");
  Serial.println("   >>> 若你已裁減 library 只剩 tpms_truck，這個模式會失效！<<<");
#endif
  Serial.println("==================================================");

  loadConfigFromEEPROM();

  Wire.begin(PIN_OLED_SDA, PIN_OLED_SCL);
  if (display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS)) {
    oledOnline = true;
    display.clearDisplay();
    display.setTextSize(1);
    display.setTextColor(SSD1306_WHITE);
    display.setCursor(22, 6);
    display.println("ESP32 TPMS");
    display.setCursor(4, 18);
    display.println("DIAGNOSTIC MODE");
    display.display();
    delay(1000);
  } else {
    Serial.println("[OLED] 警告: 未偵測到 SSD1306 OLED 螢幕 (可繼續以 Web/序列埠運作)");
  }

  WiFi.mode(WIFI_AP);
  WiFi.softAP("TPMS_PoC_Tester", "12345678");
  IPAddress IP = WiFi.softAPIP();
  Serial.print("[WiFi AP] 熱點已啟動: TPMS_PoC_Tester (密碼: 12345678)\n");
  Serial.print("[Web 儀表板] 請用手機瀏覽器打開: http://");
  Serial.println(IP);

  server.on("/", HTTP_GET, handleRoot);
  server.on("/api/rf_scan", HTTP_GET, handleApiData);
  server.on("/api/data", HTTP_GET, handleApiData);
  server.on("/api/logs", HTTP_GET, handleApiLogs);
  server.on("/api/reset_all", HTTP_POST, handleApiResetConfig);
  server.on("/api/config", HTTP_POST, handleApiConfig);
  server.on("/api/bind", HTTP_POST, handleApiBind);
  server.on("/api/swap", HTTP_POST, handleApiSwap);
  server.on("/api/clear_slot", HTTP_POST, handleApiClearSlot);
  server.on("/api/clear_discovered", handleApiClearDiscovered);
  server.on("/api/clear_all_tires", handleApiClearAllTires);
  server.on("/api/clear", handleApiClear);

  server.on("/update", HTTP_POST,
    []() {
      server.sendHeader("Connection", "close");
      server.send(200, "text/plain", Update.hasError() ? "FAIL" : "OK");
      delay(500);
      ESP.restart();
    },
    []() {
      HTTPUpload& upload = server.upload();
      if (upload.status == UPLOAD_FILE_START) {
        if (!Update.begin(UPDATE_SIZE_UNKNOWN)) Update.printError(Serial);
      } else if (upload.status == UPLOAD_FILE_WRITE) {
        if (Update.write(upload.buf, upload.currentSize) != upload.currentSize) Update.printError(Serial);
      } else if (upload.status == UPLOAD_FILE_END) {
        if (Update.end(true)) Serial.printf("[OTA] 升級完成: %u bytes\n", upload.totalSize);
        else Update.printError(Serial);
      }
    }
  );

  server.begin();

  Log.begin(LOG_LEVEL_NOTICE, &Serial);
  rtl_433_ESP::ookModulation = false;
  rf.initReceiver(RF_MODULE_RECEIVER_GPIO, RF_MODULE_FREQUENCY);
  rf.setCallback(rtl433Callback, messageBuffer, JSON_MSG_BUFFER);
  rf.setRawPulsesCallback(rtl_433_RawCallback);
  rf.enableReceiver();
  radioOnline = true;
  Serial.println("[rtl433] CC1101 433.92 MHz 2-FSK 啟動");
  rf.getModuleStatus();
  setupHostUart(9600);

  renderOLED();
}

// ------------------------------------------------------------------------------
// Main Loop
// ------------------------------------------------------------------------------
void loop() {
  server.handleClient();
  rf.loop();
  handleSerialCommand();
  processHostUart();
  manageAutoSeek();

  static unsigned long lastOledRefresh = 0;
  if (millis() - lastOledRefresh >= 2000) {
    lastOledRefresh = millis();
    renderOLED();
  }

  static unsigned long lastStatPrint = 0;
  if (millis() - lastStatPrint >= 500) {
    lastStatPrint = millis();
    uint32_t activeLockId = 0;
    for (int i = 0; i < 4; i++) {
      if (tires[i].sensorId != 0) {
        activeLockId = tires[i].sensorId;
        break;
      }
    }
    const char* curMod = rtl_433_ESP::ookModulation ? "OOK" : "FSK";
    int remainS = autoSeekModulation ? (int)((MOD_SWITCH_INTERVAL_MS - (millis() - lastModSwitchMs)) / 1000) : 0;
    if (remainS < 0) remainS = 0;
    int isLocked = autoSeekModulation ? 0 : 1;
    Serial.printf("[RADIO_STAT] rssi=%.1f,peak=%.1f,mod=%s,remain_s=%d,locked=%d,pkts=%u,heap=%u,lock_id=0x%08X,uart_rx=%u,uart_baud=%u\n",
                  currentRssi, peakRssi, curMod, remainS, isLocked, totalPacketsCount, ESP.getFreeHeap(), activeLockId, totalHostUartBytes, hostBaudRate);
  }

  if (peakRssi > -110.0f && (millis() - peakTimeMs > 6000)) {
    peakRssi = currentRssi;
  }
}
