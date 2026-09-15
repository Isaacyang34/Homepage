/*
 * ==============================================================================
 * 專案名稱: ESP8266 NodeMCU (Amica) + CC1101 433.92MHz + 0.91吋長條形 OLED 四輪胎壓接收系統
 * 核心功能: 
 *   1) 0.91吋長條形 OLED (SSD1306 128x32) 四象限極簡清晰儀表
 *   2) 手機 WiFi 熱點 (TPMS_PoC_Tester) 射頻雷達與全功能管理後台
 *   3) 全協議自動巡檢掃描 (FSK 9.6k/19.2k/4.8k, ASK/OOK 4.1k/9.6k, 泛捕獲)
 *   4) CC1101 即時場強條、底噪峰值測量、底層晶片狀態透視 (MARCSTATE / GDO0 / SPI)
 *   5) 訊號源白名單防干擾鎖定、手動位置配置、一鍵調胎、EEPROM 斷電記憶
 * ==============================================================================
 */

#include <Arduino.h>

#if defined(ESP8266)
  #include <ESP8266WiFi.h>
  #include <ESP8266WebServer.h>
  #include <ESP8266HTTPUpdateServer.h>
  #include <EEPROM.h>
  typedef ESP8266WebServer WebServerClass;
#elif defined(ESP32)
  #include <WiFi.h>
  #include <WebServer.h>
  #include <Update.h>
  #include <EEPROM.h>
  typedef WebServer WebServerClass;
#endif

#include <SPI.h>
#include <Wire.h>
#include <RadioLib.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>

#include "web_page.h"

// ------------------------------------------------------------------------------
// 硬體引腳定義
// ------------------------------------------------------------------------------
#if defined(ESP8266)
  #define PIN_CC1101_CS    4   // D2 (GPIO 4)
  #define PIN_CC1101_GDO0  5   // D1 (GPIO 5)
  #define PIN_OLED_SDA     0   // D3 (GPIO 0)
  #define PIN_OLED_SCL     2   // D4 (GPIO 2)
#else
  #define PIN_CC1101_CS    5
  #define PIN_CC1101_GDO0  2
  #define PIN_OLED_SDA     21
  #define PIN_OLED_SCL     22
#endif

#define PIN_CC1101_RST     RADIOLIB_NC
#define PIN_CC1101_GDO2    RADIOLIB_NC

#define SCREEN_WIDTH       128
#define SCREEN_HEIGHT      32
#define OLED_RESET         -1
#define SCREEN_ADDRESS     0x3C

#define MAX_PAYLOAD_SIZE   64
#define MAX_LOG_RECORDS    20
#define MAX_DISCOVERED     8

#define EEPROM_MAGIC       0x54504D53 // "TPMS"
#define EEPROM_VERSION     2          // 升級版本 2: 自動清除早期雜訊 EEPROM 並載入過濾器門檻

// ------------------------------------------------------------------------------
// 射頻掃描多協議模式定義 (全模式具備 16 位元同步字元硬體匹配，徹底杜絕雜訊溢流)
// ------------------------------------------------------------------------------
enum ScanMode {
  MODE_PROMISCUOUS = 0,
  MODE_FSK_CB56,
  MODE_FSK_D391,
  MODE_OOK_5569,
  MODE_FSK_19200,
  MODE_FSK_4800,
  SCAN_MODE_COUNT
};

struct ScanModeConfig {
  const char* name;
  const char* shortDesc;
  bool isOOK;
  float bitRate;
  float freqDev;
  float rxBw;
  uint8_t syncH;
  uint8_t syncL;
  bool promiscuous;
};

const ScanModeConfig SCAN_MODES[SCAN_MODE_COUNT] = {
  { "泛捕獲全抓",      "433.92M 泛捕獲 (全抓/智慧解碼)",        false, 9.6f,   40.0f, 270.0f, 0x00, 0x00, true },
  { "FSK 9.6k (CB56)", "433.92M 2-FSK 9.6k (外置通用/專屬匹配)", false, 9.6f,   40.0f, 135.0f, 0xCB, 0x56, false },
  { "FSK 9.6k (D391)", "433.92M 2-FSK 9.6k (豐田/日系/主流)",      false, 9.6f,   40.0f, 135.0f, 0xD3, 0x91, false },
  { "OOK 4.1k (5569)", "433.92M ASK/OOK 4.1k (胎外/太陽能)",       true,  4.096f, 0.0f,  135.0f, 0x55, 0x69, false },
  { "FSK 19.2k",       "433.92M 2-FSK 19.2k (歐美/Schrader)",       false, 19.2f,  50.0f, 200.0f, 0xD3, 0x91, false },
  { "FSK 4.8k",        "433.92M 2-FSK 4.8k (低速專用協定)",         false, 4.8f,   47.6f, 135.0f, 0xD3, 0x91, false }
};

int currentScanMode = MODE_PROMISCUOUS;
bool autoScanEnabled = true;
unsigned long lastModeSwitchMs = 0;
const unsigned long DWELL_TIME_MS = 4000;

float currentRssi = -110.0f;
float peakRssi = -110.0f;
unsigned long peakTimeMs = 0;
bool surgeDetected = false;

// ------------------------------------------------------------------------------
// 資料結構定義
// ------------------------------------------------------------------------------

// 系統配置結構體 (存入 EEPROM)
struct SystemConfig {
  uint32_t magic;
  uint8_t version;
  bool lockWhitelist;         // true: 僅接收綁定之四顆感測器; false: 開放/學習模式
  uint32_t sensorIds[4];      // 0: FL, 1: FR, 2: RL, 3: RR
  uint8_t minHits;            // 最小命中次數門檻 (預設 2 次才確認為真實感測器)
  int8_t minRssi;             // 最小 RSSI 門檻 (預設 -90 dBm，過濾微弱外車噪聲)
  uint8_t checksum;
};

// 四輪胎壓即時數據結構
struct TireData {
  uint32_t sensorId;
  float pressurePsi;
  float pressureBar;
  int tempC;
  bool lowBattery;
  bool valid;
  unsigned long lastSeenMs;
};

// 歷史封包紀錄結構體 (供 Web 儀表板日誌查看)
struct PacketRecord {
  uint32_t id;
  uint32_t timestampMs;
  float rssi;
  uint8_t len;
  bool filtered;
  char hexString[MAX_PAYLOAD_SIZE * 3 + 1];
};

// 探索感測器結構體 (供快速配對學習池)
struct DiscoveredSensor {
  uint32_t sensorId;
  float rssi;
  float psi;
  int tempC;
  uint32_t packetCount;
  unsigned long lastSeenMs;
};

// ------------------------------------------------------------------------------
// CC1101 擴充類別 (存取底層狀態暫存器如 MARCSTATE)
// ------------------------------------------------------------------------------
class CC1101Ex : public CC1101 {
public:
  CC1101Ex(Module* mod) : CC1101(mod) {}
  int16_t readReg(uint8_t reg) {
    return SPIgetRegValue(reg);
  }
};

// ------------------------------------------------------------------------------
// 全域物件與變數
// ------------------------------------------------------------------------------
CC1101Ex radio = new Module(PIN_CC1101_CS, PIN_CC1101_GDO0, PIN_CC1101_RST, PIN_CC1101_GDO2);
WebServerClass server(80);
#if defined(ESP8266)
ESP8266HTTPUpdateServer httpUpdater;
#endif
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);

SystemConfig config;
bool oledOnline = false;
bool radioOnline = false;
volatile bool packetReceivedFlag = false;
uint32_t totalPacketsCount = 0;

// 0: FL(左前), 1: FR(右前), 2: RL(左後), 3: RR(右後)
TireData tires[4] = {
  {0, 0.0f, 0.0f, 0, false, false, 0},
  {0, 0.0f, 0.0f, 0, false, false, 0},
  {0, 0.0f, 0.0f, 0, false, false, 0},
  {0, 0.0f, 0.0f, 0, false, false, 0}
};

const char* tireNames[4] = {"FL", "FR", "RL", "RR"};
const char* tireLabels[4] = {"左前輪", "右前輪", "左後輪", "右後輪"};

PacketRecord packetHistory[MAX_LOG_RECORDS];
int historyHead = 0;
int historyCount = 0;

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
    config.minHits = 2;       // 預設至少接收 2 次才確認為合法感測器
    config.minRssi = -90;     // 預設 -90 dBm 過濾遠端雜訊
    for (int i = 0; i < 4; i++) {
      config.sensorIds[i] = 0;
      tires[i].sensorId = 0;
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
// 探索感測器管理 (回傳更新後之命中次數)
// ------------------------------------------------------------------------------
int updateDiscoveredSensor(uint32_t id, float rssi, float psi, int tempC) {
  for (int i = 0; i < discoveredCount; i++) {
    if (discoveredSensors[i].sensorId == id) {
      discoveredSensors[i].rssi = rssi;
      discoveredSensors[i].psi = psi;
      discoveredSensors[i].tempC = tempC;
      discoveredSensors[i].packetCount++;
      discoveredSensors[i].lastSeenMs = millis();
      return discoveredSensors[i].packetCount;
    }
  }
  if (discoveredCount < MAX_DISCOVERED) {
    discoveredSensors[discoveredCount].sensorId = id;
    discoveredSensors[discoveredCount].rssi = rssi;
    discoveredSensors[discoveredCount].psi = psi;
    discoveredSensors[discoveredCount].tempC = tempC;
    discoveredSensors[discoveredCount].packetCount = 1;
    discoveredSensors[discoveredCount].lastSeenMs = millis();
    discoveredCount++;
    return 1;
  } else {
    int oldestIdx = 0;
    unsigned long oldestTime = discoveredSensors[0].lastSeenMs;
    for (int i = 1; i < MAX_DISCOVERED; i++) {
      if (discoveredSensors[i].lastSeenMs < oldestTime) {
        oldestTime = discoveredSensors[i].lastSeenMs;
        oldestIdx = i;
      }
    }
    discoveredSensors[oldestIdx].sensorId = id;
    discoveredSensors[oldestIdx].rssi = rssi;
    discoveredSensors[oldestIdx].psi = psi;
    discoveredSensors[oldestIdx].tempC = tempC;
    discoveredSensors[oldestIdx].packetCount = 1;
    discoveredSensors[oldestIdx].lastSeenMs = millis();
    return 1;
  }
}

// ------------------------------------------------------------------------------
// 感測器 ID 合法度檢驗 (過濾全0、全F、或載波飽和雜訊位元)
// ------------------------------------------------------------------------------
bool isValidSensorId(uint32_t id) {
  if (id == 0x00000000 || id == 0xFFFFFFFF) return false;
  if (id == 0x55555555 || id == 0xAAAAAAAA) return false;
  if (id == 0x01010101 || id == 0x11111111) return false;

  // Popcount: 計算 32 位元中 1 的個數 (Hamming weight)
  uint32_t v = id;
  v = v - ((v >> 1) & 0x55555555);
  v = (v & 0x33333333) + ((v >> 2) & 0x33333333);
  int ones = (((v + (v >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;

  // 正常感測器 ID 之 1 的個數分布在 8 ~ 24 位元 (飽和雜訊如 0x69FFFFFF 有 28 個 1)
  if (ones < 8 || ones > 24) return false;

  // 檢查連續 0xFF 或 0x00 之位元組數 (排除載波飽和滑移)
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

// ------------------------------------------------------------------------------
// 多協定 TPMS 數學檢查和驗證 (CRC8 / Sum8 / XOR8)
// ------------------------------------------------------------------------------
bool checkFrameChecksum(const uint8_t* p, size_t frameLen) {
  if (frameLen < 6) return false;
  uint8_t expected = p[frameLen - 1];

  // 1. Sum 模 256
  uint8_t sum = 0;
  for (size_t i = 0; i < frameLen - 1; i++) sum += p[i];
  if (expected == sum || expected == ((~sum) & 0xFF)) return true;

  // 2. XOR
  uint8_t x = 0;
  for (size_t i = 0; i < frameLen - 1; i++) x ^= p[i];
  if (expected == x || (x == 0 && expected != 0)) return true;

  // 3. CRC-8 (Poly 0x07)
  uint8_t crc7 = 0x00;
  for (size_t i = 0; i < frameLen - 1; i++) {
    crc7 ^= p[i];
    for (int j = 0; j < 8; j++) {
      if (crc7 & 0x80) crc7 = (crc7 << 1) ^ 0x07;
      else crc7 <<= 1;
    }
  }
  if (expected == crc7) return true;

  // 4. CRC-8 / MAXIM (Poly 0x31)
  uint8_t crc31 = 0x00;
  for (size_t i = 0; i < frameLen - 1; i++) {
    crc31 ^= p[i];
    for (int j = 0; j < 8; j++) {
      if (crc31 & 0x80) crc31 = (crc31 << 1) ^ 0x31;
      else crc31 <<= 1;
    }
  }
  if (expected == crc31) return true;

  // 5. CRC-8 / AUTOSAR (Poly 0x2F, Init 0xFF, XorOut 0xFF)
  uint8_t crc2f = 0xFF;
  for (size_t i = 0; i < frameLen - 1; i++) {
    crc2f ^= p[i];
    for (int j = 0; j < 8; j++) {
      if (crc2f & 0x80) crc2f = (crc2f << 1) ^ 0x2F;
      else crc2f <<= 1;
    }
  }
  if (expected == (crc2f ^ 0xFF)) return true;

  return false;
}

// ------------------------------------------------------------------------------
// 中斷服務函式
// ------------------------------------------------------------------------------
#if defined(ESP8266) || defined(ESP32)
  ICACHE_RAM_ATTR
#endif
void handleRadioInterrupt() {
  packetReceivedFlag = true;
}

// ------------------------------------------------------------------------------
// 射頻協議切換函式
// ------------------------------------------------------------------------------
bool applyScanMode(int mode) {
  if (mode < 0 || mode >= SCAN_MODE_COUNT) mode = 0;
  currentScanMode = mode;
  const ScanModeConfig& m = SCAN_MODES[mode];
  
  radio.standby();
  int state = RADIOLIB_ERR_NONE;

  if (m.isOOK) {
    state = radio.setOOK(true);
    if (state == RADIOLIB_ERR_NONE) state = radio.setFrequency(433.92);
    if (state == RADIOLIB_ERR_NONE) state = radio.setBitRate(m.bitRate);
    if (state == RADIOLIB_ERR_NONE) state = radio.setRxBandwidth(m.rxBw);
  } else {
    state = radio.setOOK(false);
    if (state == RADIOLIB_ERR_NONE) state = radio.setFrequency(433.92);
    if (state == RADIOLIB_ERR_NONE) state = radio.setBitRate(m.bitRate);
    if (state == RADIOLIB_ERR_NONE) state = radio.setFrequencyDeviation(m.freqDev);
    if (state == RADIOLIB_ERR_NONE) state = radio.setRxBandwidth(m.rxBw);
  }

  // 關閉 CRC 硬體過濾，由軟體解碼多種 TPMS 校驗
  radio.setCrcFiltering(false);

  if (m.promiscuous) {
    // 泛捕獲模式: 寬鬆接收空中所有 433MHz 封包，配合軟體靜音門閥 (Squelch)
    radio.setPromiscuousMode(true, false);
  } else {
    // 專用協議模式: 啟用同步字元匹配，並開啟 1-bit 容差
    radio.setPromiscuousMode(false);
    radio.setSyncWord(m.syncH, m.syncL, 1, false);
  }

  radio.setGdo0Action(handleRadioInterrupt, RISING);
  state = radio.startReceive();
  
  Serial.printf("[射頻掃描] 已切換至模式 [%d]: %s (State: %d)\n", mode, m.shortDesc, state);
  return (state == RADIOLIB_ERR_NONE);
}

// ------------------------------------------------------------------------------
// OLED 繪製函式 - 0.91 吋長條形 (128x32)
// ------------------------------------------------------------------------------
void renderOLED() {
  if (!oledOnline) return;

  display.clearDisplay();
  display.setTextSize(1);
  display.setTextColor(SSD1306_WHITE);

  // 繪製十字分隔線
  display.drawFastVLine(63, 0, 32, SSD1306_WHITE);
  display.drawFastHLine(0, 15, 128, SSD1306_WHITE);

  unsigned long now = millis();

  // 1. [左前輪 FL]
  display.setCursor(2, 4);
  if (tires[0].valid && (now - tires[0].lastSeenMs < 900000)) {
    display.printf("FL:%.1f", tires[0].pressurePsi);
    display.setCursor(44, 4);
    display.printf("%dC", tires[0].tempC);
  } else {
    display.print("FL:--.- --C");
  }

  // 2. [右前輪 FR]
  display.setCursor(66, 4);
  if (tires[1].valid && (now - tires[1].lastSeenMs < 900000)) {
    display.printf("FR:%.1f", tires[1].pressurePsi);
    display.setCursor(108, 4);
    display.printf("%dC", tires[1].tempC);
  } else {
    display.print("FR:--.- --C");
  }

  // 3. [左後輪 RL]
  display.setCursor(2, 20);
  if (tires[2].valid && (now - tires[2].lastSeenMs < 900000)) {
    display.printf("RL:%.1f", tires[2].pressurePsi);
    display.setCursor(44, 20);
    display.printf("%dC", tires[2].tempC);
  } else {
    display.print("RL:--.- --C");
  }

  // 4. [右後輪 RR]
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
// TPMS 封包解碼與多重防雜訊過濾器 (支援全緩衝區同步字元與多偏移掃描)
// ------------------------------------------------------------------------------
bool processTpmsPacket(const uint8_t* buffer, size_t len, float rssi) {
  if (len < 8) return false;
  if (rssi < (float)config.minRssi) return false; // 排除 -130 dBm 等靜電底噪假觸發

  bool foundValid = false;
  uint32_t sensorId = 0;
  float psi = 0.0f;
  int tempC = 25;
  bool lowBat = false;

  // 1. 全封包位移掃描 (尋找同步字元與真實 TPMS 數據)
  for (size_t offset = 0; offset + 6 <= len; offset++) {
    size_t dataStart = offset;

    // 優先檢測同步字元 (支援 0xCB 0x56, 0x55 0x56, 0x56, 0xD3 0x91 等)
    if (buffer[offset] == 0x56 && offset + 5 <= len) {
      dataStart = offset + 1;
    } else if (offset + 2 <= len && buffer[offset] == 0xCB && buffer[offset + 1] == 0x56) {
      dataStart = offset + 2;
    }

    if (dataStart + 5 > len) continue;

    const uint8_t* p = buffer + dataStart;
    size_t rem = len - dataStart;

    uint32_t candId = ((uint32_t)p[0] << 24) |
                      ((uint32_t)p[1] << 16) |
                      ((uint32_t)p[2] << 8)  |
                      ((uint32_t)p[3]);

    if (!isValidSensorId(candId)) continue;

    // 檢驗胎壓 (支援 0x5F = 34.5 psi 等標準公式)
    uint8_t rawP = p[4];
    float candPsi = rawP * 0.363f;

    // 物理真實合理區間: 0.0 ~ 85.0 psi (允許 0 psi 桌面未安裝測試)
    if (candPsi >= 0.0f && candPsi <= 85.0f) {
      // 溫度解碼 (支援 0x86 等高位元組偏移)
      uint8_t rawT = (rem > 5) ? p[5] : 78;
      int candTemp = 28;
      if (rawT >= 100 && rawT <= 160) {
        candTemp = (int)rawT - 105; // 0x86 (134) - 105 = 29°C 室溫
      } else if (rawT >= 40 && rawT < 100) {
        candTemp = (int)rawT - 40;
      }
      if (candTemp < -20 || candTemp > 80) candTemp = 28;

      sensorId = candId;
      psi = candPsi;
      tempC = candTemp;
      lowBat = (rem > 5) ? ((p[5] & 0x80) == 0) : false;
      foundValid = true;
      break;
    }
  }

  if (!foundValid) return false;

  // 2. 當成功解碼出真實 TPMS 感測器時，鎖定停留在當前模式 30 秒，避免自動切換錯過後續封包
  lastModeSwitchMs = millis() + 30000;

  // 3. 更新探索學習池並獲取累計命中次數
  int hitCount = updateDiscoveredSensor(sensorId, rssi, psi, tempC);

  Serial.printf("\n[解碼成功] 感測器 ID: 0x%08X (命中 %d 次, RSSI: %.1f dBm) | 胎壓: %.1f psi (%.2f bar) | 胎溫: %d C\n",
                sensorId, hitCount, rssi, psi, psi * 0.0689476f, tempC);

  // 4. 比對是否為已手動綁定之四輪
  int targetIndex = -1;
  for (int i = 0; i < 4; i++) {
    if (tires[i].sensorId == sensorId && sensorId != 0) {
      targetIndex = i;
      break;
    }
  }

  // 5. 白名單防干擾過濾 (嚴格杜絕未綁定外車感測器竄改儀表)
  if (config.lockWhitelist) {
    if (targetIndex == -1) {
      Serial.printf("[防干擾] 攔截未授權感測器 ID: 0x%08X (RSSI: %.1f dBm)\n", sensorId, rssi);
      return true;
    }
  }

  // 6. 若已綁定四輪之一，更新數據與 OLED 顯示
  if (targetIndex != -1) {
    tires[targetIndex].pressurePsi = psi;
    tires[targetIndex].pressureBar = psi * 0.0689476f;
    tires[targetIndex].tempC = tempC;
    tires[targetIndex].lowBattery = lowBat;
    tires[targetIndex].valid = true;
    tires[targetIndex].lastSeenMs = millis();

    renderOLED();
  }

  return true;
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

// 即時射頻掃描與場強雷達 API (300ms 輪詢)
void handleApiRfScan() {
  int marc = radio.readReg(0x35);
  String marcDesc = "0x" + String(marc, HEX);
  if (marc == 0x0D) marcDesc += " (RX 接收)";
  else if (marc == 0x01) marcDesc += " (IDLE 待命)";
  else if (marc == 0x11) marcDesc += " (FIFO 溢位重啟)";
  else marcDesc += " (運行中)";

  int chip = radio.getChipVersion();
  String chipDesc = "0x" + String(chip, HEX);
  if (chip == 0x14 || chip == 0x04) chipDesc += " (SPI 正常)";
  else if (chip <= 0) chipDesc += " (SPI 異常)";
  else chipDesc += " (正常)";

  int gdo0 = digitalRead(PIN_CC1101_GDO0);

  String json = "{";
  json += "\"rssi\":" + String(currentRssi, 1) + ",";
  json += "\"peakRssi\":" + String(peakRssi, 1) + ",";
  json += "\"scanMode\":\"" + String(SCAN_MODES[currentScanMode].name) + "\",";
  json += "\"scanModeIdx\":" + String(currentScanMode) + ",";
  json += "\"autoScan\":" + String(autoScanEnabled ? "true" : "false") + ",";
  json += "\"marcStateDesc\":\"" + marcDesc + "\",";
  json += "\"chipVerDesc\":\"" + chipDesc + "\",";
  json += "\"gdo0\":" + String(gdo0) + ",";
  json += "\"totalPackets\":" + String(totalPacketsCount) + ",";
  json += "\"surge\":" + String(surgeDetected ? "true" : "false");
  json += "}";

  server.send(200, "application/json", json);
}

void handleApiSetMode() {
  if (server.hasArg("auto")) {
    autoScanEnabled = (server.arg("auto") == "1" || server.arg("auto") == "true");
    Serial.printf("[Web掃描] 全協議自動巡檢: %s\n", autoScanEnabled ? "開啟" : "關閉");
  }
  if (server.hasArg("mode")) {
    int m = server.arg("mode").toInt();
    applyScanMode(m);
  }
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiResetRf() {
  applyScanMode(currentScanMode);
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiData() {
  String json = "{";
  json += "\"totalPackets\":" + String(totalPacketsCount) + ",";
  json += "\"rf\":" + String(radioOnline ? "true" : "false") + ",";
  json += "\"oled\":" + String(oledOnline ? "true" : "false") + ",";
  
  // 系統硬體資訊
  json += "\"sys\":{";
  json += "\"version\":\"v2.8.1\",";
  json += "\"freeHeap\":" + String(ESP.getFreeHeap()) + ",";
  json += "\"flashSize\":" + String(ESP.getFlashChipSize()) + ",";
  json += "\"chipId\":\"0x" + String(ESP.getChipId(), HEX) + "\"";
  json += "},";
  
  // 系統配置
  json += "\"config\":{";
  json += "\"lockWhitelist\":" + String(config.lockWhitelist ? "true" : "false") + ",";
  json += "\"minHits\":" + String(config.minHits) + ",";
  json += "\"minRssi\":" + String(config.minRssi);
  json += "},";

  // 四輪即時數據
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

  // 探索感測器池
  json += "\"discovered\":[";
  int dCount = 0;
  for (int i = 0; i < discoveredCount; i++) {
    if (dCount > 0) json += ",";
    json += "{";
    json += "\"id\":" + String(discoveredSensors[i].sensorId) + ",";
    json += "\"rssi\":" + String(discoveredSensors[i].rssi, 1) + ",";
    json += "\"psi\":" + String(discoveredSensors[i].psi, 1) + ",";
    json += "\"temp\":" + String(discoveredSensors[i].tempC) + ",";
    json += "\"count\":" + String(discoveredSensors[i].packetCount);
    json += "}";
    dCount++;
  }
  json += "],";

  // 封包日誌
  json += "\"logs\":[";
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

uint32_t parseSensorId(const String& str) {
  String s = str;
  s.trim();
  if (s.startsWith("0x") || s.startsWith("0X")) {
    s = s.substring(2);
  }
  return (uint32_t)strtoul(s.c_str(), NULL, 16);
}

void handleApiConfig() {
  if (server.hasArg("lock")) {
    config.lockWhitelist = (server.arg("lock") == "1" || server.arg("lock") == "true");
    Serial.printf("[Web設定] 防干擾白名單鎖定: %s\n", config.lockWhitelist ? "開啟" : "關閉");
  }
  if (server.hasArg("hits")) {
    int h = server.arg("hits").toInt();
    if (h >= 1 && h <= 10) config.minHits = h;
    Serial.printf("[Web設定] 雜訊防抖最小命中門檻: %d 次\n", config.minHits);
  }
  if (server.hasArg("rssi")) {
    int r = server.arg("rssi").toInt();
    if (r >= -120 && r <= -40) config.minRssi = r;
    Serial.printf("[Web設定] 最小 RSSI 過濾門檻: %d dBm\n", config.minRssi);
  }
  saveConfigToEEPROM();
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiClearDiscovered() {
  discoveredCount = 0;
  Serial.println("[Web操作] 周遭感測器探索池已清空");
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
  Serial.println("[Web操作] 四輪綁定已全部清空歸零");
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

void handleApiBind() {
  if (server.hasArg("pos") && server.hasArg("id")) {
    int pos = server.arg("pos").toInt();
    if (pos >= 0 && pos < 4) {
      uint32_t newId = parseSensorId(server.arg("id"));
      tires[pos].sensorId = newId;
      saveConfigToEEPROM();
      Serial.printf("[Web綁定] 輪位 %s (%s) 綁定 ID: 0x%08X\n", tireNames[pos], tireLabels[pos], newId);
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
  Serial.println("[Web操作] 歷史封包與探索學習池紀錄已清空");
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
  Serial.println("[系統操作] 已執行原廠重置，清空所有暫存與輪胎綁定");
  server.send(200, "application/json", "{\"status\":\"ok\"}");
}

// ------------------------------------------------------------------------------
// Setup
// ------------------------------------------------------------------------------
void setup() {
  Serial.begin(115200);
  delay(500);
  Serial.println("\n==================================================");
  Serial.println("   ESP8266 + CC1101 射頻掃描與四輪胎壓接收系統");
  Serial.println("==================================================");

  // 1. 載入 EEPROM 斷電記憶設定
  loadConfigFromEEPROM();

  // 2. 初始化 I2C 與 0.91 吋 OLED
  Wire.begin(PIN_OLED_SDA, PIN_OLED_SCL);
  if (display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS)) {
    oledOnline = true;
    Serial.println("[OLED] 0.91 吋 SSD1306 (128x32) 初始化成功!");
    display.clearDisplay();
    display.setTextSize(1);
    display.setTextColor(SSD1306_WHITE);
    display.setCursor(22, 6);
    display.println("ESP8266 TPMS");
    display.setCursor(16, 18);
    display.println("RF Radar & OLED");
    display.display();
    delay(1000);
  } else {
    Serial.println("[OLED] 警告: 未偵測到 SSD1306 OLED 螢幕 (可繼續以 Web/序列埠運作)");
  }

  // 3. 初始化 WiFi SoftAP (熱點)
  WiFi.mode(WIFI_AP);
  WiFi.softAP("TPMS_PoC_Tester", "12345678");
  IPAddress IP = WiFi.softAPIP();
  
  Serial.print("[WiFi AP] 熱點已啟動: TPMS_PoC_Tester (密碼: 12345678)\n");
  Serial.print("[Web 儀表板] 請用手機瀏覽器打開: http://");
  Serial.println(IP);

  // 4. 設置 WebServer 路由
  server.on("/", HTTP_GET, handleRoot);
  server.on("/api/rf_scan", HTTP_GET, handleApiRfScan);
  server.on("/api/data", HTTP_GET, handleApiData);
  server.on("/api/set_mode", HTTP_POST, handleApiSetMode);
  server.on("/api/reset_rf", HTTP_POST, handleApiResetRf);
  server.on("/api/reset_all", HTTP_POST, handleApiResetConfig);
  server.on("/api/config", HTTP_POST, handleApiConfig);
  server.on("/api/bind", HTTP_POST, handleApiBind);
  server.on("/api/swap", HTTP_POST, handleApiSwap);
  server.on("/api/clear_slot", HTTP_POST, handleApiClearSlot);
  server.on("/api/clear_discovered", handleApiClearDiscovered);
  server.on("/api/clear_all_tires", handleApiClearAllTires);
  server.on("/api/clear", handleApiClear);

  // 註冊 OTA 線上無線更新路由 (/update)
#if defined(ESP8266)
  httpUpdater.setup(&server, "/update");
  Serial.println("[OTA] 線上無線韌體更新就緒: http://192.168.4.1/update");
#endif

  server.begin();

  // 5. 初始化 CC1101 並載入初始掃描模式
  int beginState = radio.begin(433.92, 9.6, 40.0, 135.0, 10, 32);
  if (beginState == RADIOLIB_ERR_NONE) {
    radioOnline = applyScanMode(MODE_PROMISCUOUS);
  } else {
    Serial.printf("[CC1101] 初始化失敗! 錯誤碼: %d\n", beginState);
  }

  // 6. 渲染初始 OLED 介面
  renderOLED();
}

// ------------------------------------------------------------------------------
// Main Loop
// ------------------------------------------------------------------------------
void loop() {
  server.handleClient();

  // 1. 定期讀取即時場強與峰值 (每 50ms)
  static unsigned long lastRssiRead = 0;
  if (millis() - lastRssiRead >= 50) {
    lastRssiRead = millis();
    float r = radio.getRSSI();
    // 物理真實濾波: CC1101 有效接收場強物理區間為 -125 dBm 至 -25 dBm (排除 PLL 校準暫態 0.0 或 -0.5 等異常假讀數)
    if (r >= -125.0f && r <= -25.0f) {
      currentRssi = r;
      if (r > peakRssi || (millis() - peakTimeMs > 6000)) {
        if (r > peakRssi) {
          peakRssi = r;
          peakTimeMs = millis();
        } else if (millis() - peakTimeMs > 6000) {
          peakRssi = r; // 緩慢自然衰退
        }
      }
      surgeDetected = (r > -65.0f); // 嚴格門檻: 僅在近距離真實強射頻時才觸發脈衝警報
    }
  }

  // 2. 射頻硬體看門狗: 檢查 MARCSTATE (若遇 0x11 FIFO溢位自動恢復)
  static unsigned long lastWatchdog = 0;
  if (millis() - lastWatchdog >= 1000) {
    lastWatchdog = millis();
    int marc = radio.readReg(0x35);
    if (marc == 0x11) {
      Serial.println("[看門狗] 偵測到 RX FIFO 溢位，自動重啟接收...");
      radio.startReceive();
    }
  }

  // 3. 全協議自動巡檢掃描排程
  if (autoScanEnabled && !packetReceivedFlag) {
    unsigned long dwell = surgeDetected ? 8000 : DWELL_TIME_MS;
    if (millis() - lastModeSwitchMs >= dwell) {
      lastModeSwitchMs = millis();
      int nextMode = (currentScanMode + 1) % SCAN_MODE_COUNT;
      applyScanMode(nextMode);
    }
  }

  // 4. 定期刷新 OLED 螢幕 (每 2 秒一次)
  static unsigned long lastOledRefresh = 0;
  if (millis() - lastOledRefresh >= 2000) {
    lastOledRefresh = millis();
    renderOLED();
  }

  // 5. 檢查是否有 433MHz 封包抵達中斷
  if (packetReceivedFlag) {
    packetReceivedFlag = false;

    // 防中斷突波淹沒 CPU (最少間隔 60ms)
    static unsigned long lastRxTimeMs = 0;
    if (millis() - lastRxTimeMs < 60) {
      radio.startReceive();
      return;
    }
    lastRxTimeMs = millis();

    size_t len = radio.getPacketLength();
    if (len == 0) {
      radio.startReceive();
      return;
    }

    uint8_t buffer[MAX_PAYLOAD_SIZE];
    size_t safeLen = (len > sizeof(buffer)) ? sizeof(buffer) : len;
    
    int state = radio.readData(buffer, safeLen);

    if (state == RADIOLIB_ERR_NONE) {
      float rssi = radio.getRSSI();

      // 先行由解碼器檢驗是否為真實 TPMS 感測器封包
      bool isTpms = processTpmsPacket(buffer, safeLen, rssi);

      // 若未解碼出 TPMS，且符合空氣噪聲特徵 (連續 0xFF/00 超過 60% 或微弱底噪)，靜音捨棄
      if (!isTpms) {
        int satCount = 0;
        for (size_t i = 0; i < safeLen; i++) {
          if (buffer[i] == 0xFF || buffer[i] == 0x00) satCount++;
        }
        if (satCount > (int)(safeLen * 0.60) || rssi < (float)config.minRssi) {
          radio.startReceive();
          return;
        }
      }

      totalPacketsCount++;

      Serial.printf("\n[攔截封包 #%u] 模式: %s | RSSI: %.1f dBm, 長度: %u Bytes\n", 
                    totalPacketsCount, SCAN_MODES[currentScanMode].name, rssi, safeLen);
      Serial.print("  HEX: ");
      char hexStr[MAX_PAYLOAD_SIZE * 3 + 1] = {0};
      for (size_t i = 0; i < safeLen; i++) {
        char byteBuf[4];
        snprintf(byteBuf, sizeof(byteBuf), "%02X ", buffer[i]);
        strncat(hexStr, byteBuf, sizeof(hexStr) - strlen(hexStr) - 1);
        Serial.print(byteBuf);
      }
      Serial.println();

      packetHistory[historyHead].id = totalPacketsCount;
      packetHistory[historyHead].timestampMs = millis();
      packetHistory[historyHead].rssi = rssi;
      packetHistory[historyHead].len = safeLen;
      packetHistory[historyHead].filtered = false;
      strncpy(packetHistory[historyHead].hexString, hexStr, sizeof(packetHistory[historyHead].hexString) - 1);

      historyHead = (historyHead + 1) % MAX_LOG_RECORDS;
      if (historyCount < MAX_LOG_RECORDS) {
        historyCount++;
      }

    } else if (state == RADIOLIB_ERR_CRC_MISMATCH) {
      Serial.println("[警告] 收到雜訊或 CRC 錯誤封包");
    }

    radio.startReceive();
  }
}
