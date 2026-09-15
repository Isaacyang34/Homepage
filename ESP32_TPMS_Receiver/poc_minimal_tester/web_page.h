#pragma once
#include <Arduino.h>

const char PAGE_HTML[] PROGMEM = R"rawliteral(
<!DOCTYPE html>
<html lang="zh-TW">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
  <title>ESP8266 TPMS 射頻掃描與胎壓監控儀</title>
  <style>
    :root {
      --bg: #0b1120;
      --card: #1e293b;
      --card-alt: #162032;
      --border: #334155;
      --accent: #38bdf8;
      --green: #22c55e;
      --red: #ef4444;
      --yellow: #f59e0b;
      --purple: #a855f7;
      --text: #f8fafc;
      --text-muted: #94a3b8;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
    body { background: var(--bg); color: var(--text); padding: 12px; min-height: 100vh; max-width: 620px; margin: 0 auto; }
    
    /* 頂部導覽與狀態列 */
    .top-bar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    .top-title { font-size: 1.15rem; font-weight: 700; color: var(--accent); }
    .badge-lock { padding: 4px 10px; border-radius: 9999px; font-size: 0.75rem; font-weight: 600; cursor: pointer; }
    .badge-locked { background: rgba(34, 197, 94, 0.2); color: var(--green); border: 1px solid var(--green); }
    .badge-open { background: rgba(245, 158, 11, 0.2); color: var(--yellow); border: 1px solid var(--yellow); }
    
    /* 射頻即時監測雷達 HUD (置頂常駐) */
    .rf-hud { background: linear-gradient(180deg, #131d31 0%, #0d1525 100%); border: 1px solid #2a3b5c; border-radius: 12px; padding: 12px; margin-bottom: 14px; position: relative; overflow: hidden; }
    .rf-hud-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
    .rf-title { font-size: 0.85rem; font-weight: 700; color: #7dd3fc; display: flex; align-items: center; gap: 6px; }
    .rf-mode-badge { background: #1e293b; border: 1px solid #475569; padding: 2px 8px; border-radius: 6px; font-size: 0.75rem; color: #cbd5e1; }
    
    /* RSSI 場強進度條 */
    .rssi-meter-box { margin-bottom: 8px; }
    .rssi-val-row { display: flex; justify-content: space-between; font-size: 0.8rem; margin-bottom: 4px; font-family: monospace; }
    .rssi-val-curr { font-weight: 700; font-size: 1.1rem; color: var(--accent); }
    .rssi-val-peak { color: var(--yellow); }
    .meter-track { height: 12px; background: #1e293b; border-radius: 6px; overflow: hidden; position: relative; border: 1px solid #334155; }
    .meter-bar { height: 100%; width: 0%; transition: width 0.15s ease-out; background: linear-gradient(90deg, #38bdf8 0%, #22c55e 60%, #eab308 85%, #ef4444 100%); border-radius: 6px; }
    .meter-ticks { display: flex; justify-content: space-between; font-size: 0.65rem; color: #64748b; margin-top: 2px; }

    /* 射頻脈衝警報 Banner */
    .surge-alert { display: none; background: rgba(239, 68, 68, 0.2); border: 1px solid var(--red); color: #fca5a5; padding: 6px 10px; border-radius: 8px; font-size: 0.75rem; text-align: center; font-weight: 700; margin-top: 6px; animation: pulse 1s infinite alternate; }
    @keyframes pulse { from { opacity: 0.8; transform: scale(0.99); } to { opacity: 1; transform: scale(1.01); } }

    /* 硬體健康狀態小標記 */
    .hw-status-row { display: flex; justify-content: space-between; font-size: 0.7rem; color: var(--text-muted); margin-top: 6px; border-top: 1px dashed #22324e; padding-top: 6px; }

    /* 分頁標籤 */
    .tabs { display: flex; gap: 6px; margin-bottom: 14px; background: var(--card-alt); padding: 4px; border-radius: 10px; }
    .tab-btn { flex: 1; padding: 8px; border: none; background: transparent; color: var(--text-muted); font-size: 0.85rem; font-weight: 600; border-radius: 8px; cursor: pointer; text-align: center; }
    .tab-btn.active { background: var(--card); color: var(--accent); box-shadow: 0 1px 3px rgba(0,0,0,0.3); }
    
    .tab-content { display: none; }
    .tab-content.active { display: block; }

    /* 四輪儀表佈局 */
    .car-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 14px; }
    .tire-card { background: var(--card); border-radius: 12px; padding: 12px; border: 1px solid var(--border); text-align: center; }
    .tire-card.active { border-color: var(--accent); }
    .tire-title { font-size: 0.8rem; font-weight: 700; color: var(--text-muted); margin-bottom: 4px; display: flex; justify-content: space-between; }
    .tire-psi { font-size: 1.8rem; font-weight: 800; color: var(--green); line-height: 1.2; }
    .tire-sub { font-size: 0.8rem; color: var(--text-muted); margin-top: 4px; display: flex; justify-content: space-around; }
    .tire-id-tag { font-size: 0.7rem; color: #64748b; font-family: monospace; margin-top: 6px; }

    /* 卡片容器 */
    .card { background: var(--card); border-radius: 12px; padding: 14px; border: 1px solid var(--border); margin-bottom: 14px; }
    .card-title { font-size: 0.95rem; font-weight: 700; margin-bottom: 10px; display: flex; justify-content: space-between; align-items: center; }
    
    /* 模式切換按鈕組 */
    .mode-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 10px; }
    .btn-mode { background: #162032; border: 1px solid #334155; color: #cbd5e1; padding: 8px; border-radius: 8px; font-size: 0.75rem; text-align: left; cursor: pointer; transition: all 0.2s; }
    .btn-mode.active { border-color: var(--accent); background: rgba(56, 189, 248, 0.15); color: var(--accent); font-weight: 700; }
    .btn-mode small { display: block; font-size: 0.65rem; color: var(--text-muted); }

    /* 開關組件 */
    .switch-row { display: flex; justify-content: space-between; align-items: center; padding: 8px 0; }
    .switch-info h4 { font-size: 0.9rem; font-weight: 600; margin-bottom: 2px; }
    .switch-info p { font-size: 0.75rem; color: var(--text-muted); }
    .switch { position: relative; display: inline-block; width: 48px; height: 26px; }
    .switch input { opacity: 0; width: 0; height: 0; }
    .slider { position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0; background-color: #475569; transition: .3s; border-radius: 26px; }
    .slider:before { position: absolute; content: ""; height: 20px; width: 20px; left: 3px; bottom: 3px; background-color: white; transition: .3s; border-radius: 50%; }
    input:checked + .slider { background-color: var(--green); }
    input:checked + .slider:before { transform: translateX(22px); }

    /* 快捷調胎按鈕群 */
    .btn-group { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 8px; margin-top: 8px; }
    .btn-action { background: #334155; color: #fff; border: 1px solid #475569; padding: 8px; border-radius: 8px; font-size: 0.75rem; font-weight: 600; cursor: pointer; text-align: center; }
    .btn-action:active { background: #475569; }

    /* 輪位配置表格 */
    .slot-row { display: flex; align-items: center; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #334155; }
    .slot-row:last-child { border-bottom: none; }
    .slot-label { font-size: 0.85rem; font-weight: 600; min-width: 65px; }
    .slot-input { flex: 1; margin: 0 8px; background: #0f172a; border: 1px solid #475569; border-radius: 6px; color: var(--text); padding: 6px 8px; font-family: monospace; font-size: 0.85rem; }
    .btn-slot-save { background: var(--accent); color: #0b1120; border: none; padding: 6px 10px; border-radius: 6px; font-size: 0.75rem; font-weight: bold; cursor: pointer; }
    .btn-slot-clear { background: transparent; color: var(--red); border: 1px solid var(--red); padding: 6px 8px; border-radius: 6px; font-size: 0.75rem; cursor: pointer; margin-left: 4px; }

    /* 感測器探索學習池 */
    .disc-item { background: #0f172a; border: 1px solid #334155; border-radius: 8px; padding: 10px; margin-bottom: 8px; }
    .disc-head { display: flex; justify-content: space-between; font-size: 0.8rem; margin-bottom: 6px; }
    .disc-id { font-weight: 700; color: #a5f3fc; font-family: monospace; }
    .disc-meta { color: var(--text-muted); font-size: 0.75rem; }
    .disc-bind-btns { display: grid; grid-template-columns: 1fr 1fr 1fr 1fr; gap: 6px; margin-top: 6px; }
    .btn-bind { background: #1e293b; color: var(--accent); border: 1px solid var(--accent); padding: 5px; border-radius: 6px; font-size: 0.75rem; font-weight: bold; cursor: pointer; text-align: center; }
    .btn-bind:active { background: var(--accent); color: #0b1120; }

    /* 感測器訊號強弱與遠近圖示 (4格訊號條) */
    .sig-meter { display: inline-flex; align-items: flex-end; gap: 2px; height: 14px; margin-right: 6px; vertical-align: middle; }
    .sig-bar { width: 3px; background: #334155; border-radius: 1px; }
    .sig-bar.b1 { height: 4px; }
    .sig-bar.b2 { height: 7px; }
    .sig-bar.b3 { height: 10px; }
    .sig-bar.b4 { height: 14px; }
    
    .sig-act-green { background: #22c55e !important; box-shadow: 0 0 4px rgba(34,197,94,0.6); }
    .sig-act-cyan { background: #38bdf8 !important; }
    .sig-act-yellow { background: #eab308 !important; }
    .sig-act-gray { background: #64748b !important; }

    /* 遠近距離標籤 */
    .prox-tag { display: inline-block; padding: 1px 6px; border-radius: 4px; font-size: 0.68rem; font-weight: 600; margin-left: 4px; vertical-align: middle; }
    .prox-immediate { background: rgba(34, 197, 94, 0.25); color: #4ade80; border: 1px solid #22c55e; }
    .prox-near { background: rgba(56, 189, 248, 0.2); color: #7dd3fc; border: 1px solid #38bdf8; }
    .prox-mid { background: rgba(234, 179, 8, 0.2); color: #fde047; border: 1px solid #eab308; }
    .prox-far { background: rgba(148, 163, 184, 0.15); color: #cbd5e1; border: 1px solid #475569; }

    /* 封包日誌 */
    .log-list { display: flex; flex-direction: column; gap: 8px; max-height: 320px; overflow-y: auto; }
    .log-item { background: #0f172a; border-radius: 6px; padding: 8px 10px; font-family: monospace; font-size: 0.75rem; border-left: 3px solid var(--accent); }
    .log-item.filtered { border-left-color: var(--yellow); opacity: 0.7; }
    .log-meta { display: flex; justify-content: space-between; color: var(--text-muted); margin-bottom: 4px; font-size: 0.7rem; }
    .log-hex { color: #a5f3fc; word-break: break-all; }
  </style>
</head>
<body>
  <!-- 頂部標題與防干擾狀態 -->
  <div class="top-bar">
    <div class="top-title">TPMS 射頻雷達 <span style="font-size:0.7rem; color:#a5f3fc; background:#0f172a; padding:2px 6px; border-radius:4px; border:1px solid #38bdf8;">v2.8 (距離圖示與GitHub自動更新版)</span></div>
    <div id="badge-lock" class="badge-lock badge-open" onclick="switchTab('setup')">[學習模式: 未鎖定]</div>
  </div>

  <!-- 置頂射頻掃描與場強雷達 HUD -->
  <div class="rf-hud">
    <div class="rf-hud-header">
      <div class="rf-title">
        <span style="display:inline-block; width:8px; height:8px; border-radius:50%; background:var(--green);" id="rf-dot"></span>
        <span>CC1101 即時場強雷達</span>
      </div>
      <div class="rf-mode-badge" id="hud-mode-name">FSK 9.6k (自動巡檢)</div>
    </div>

    <!-- 即時 RSSI 表針與數值 -->
    <div class="rssi-meter-box">
      <div class="rssi-val-row">
        <span>即時場強: <span class="rssi-val-curr" id="hud-rssi">-105.0</span> dBm</span>
        <span class="rssi-val-peak">峰值: <span id="hud-peak">-105.0</span> dBm</span>
      </div>
      <div class="meter-track">
        <div class="meter-bar" id="hud-meter-bar"></div>
      </div>
      <div class="meter-ticks">
        <span>-115 (安靜)</span>
        <span>-90 (底噪)</span>
        <span>-70 (中等)</span>
        <span>-45 (強發射)</span>
      </div>
    </div>

    <!-- 射頻突波脈衝警示 -->
    <div class="surge-alert" id="hud-surge">
      [!] 偵測到 433MHz 射頻強脈衝！感測器正在發射！
    </div>

    <!-- 底層晶片狀態列 -->
    <div class="hw-status-row">
      <span>晶片: <b id="hw-chip" style="color:#38bdf8">--</b></span>
      <span>MARCSTATE: <b id="hw-marc" style="color:var(--green)">--</b></span>
      <span>GDO0(D1): <b id="hw-gdo0">0</b></span>
      <span>封包總數: <b id="hw-pkts">0</b></span>
    </div>
  </div>

  <!-- 分頁選單 -->
  <div class="tabs">
    <button class="tab-btn active" onclick="switchTab('dashboard')">四輪儀表</button>
    <button class="tab-btn" onclick="switchTab('scanner')">射頻掃描</button>
    <button class="tab-btn" onclick="switchTab('setup')">輪位防干擾</button>
    <button class="tab-btn" onclick="switchTab('logs')">原始日誌</button>
    <button class="tab-btn" onclick="switchTab('ota')">線上更新</button>
  </div>

  <!-- 分頁 1: 即時四輪儀表板 -->
  <div id="tab-dashboard" class="tab-content active">
    <div class="car-grid">
      <!-- 左前 FL -->
      <div class="tire-card" id="card-fl">
        <div class="tire-title"><span>左前輪 (FL)</span><span id="stat-fl">待機</span></div>
        <div class="tire-psi" id="psi-fl">--.-</div>
        <div class="tire-sub"><span id="bar-fl">-- bar</span><span id="temp-fl">-- °C</span></div>
        <div class="tire-id-tag" id="id-fl">ID: 0x00000000</div>
      </div>
      <!-- 右前 FR -->
      <div class="tire-card" id="card-fr">
        <div class="tire-title"><span>右前輪 (FR)</span><span id="stat-fr">待機</span></div>
        <div class="tire-psi" id="psi-fr">--.-</div>
        <div class="tire-sub"><span id="bar-fr">-- bar</span><span id="temp-fr">-- °C</span></div>
        <div class="tire-id-tag" id="id-fr">ID: 0x00000000</div>
      </div>
      <!-- 左後 RL -->
      <div class="tire-card" id="card-rl">
        <div class="tire-title"><span>左後輪 (RL)</span><span id="stat-rl">待機</span></div>
        <div class="tire-psi" id="psi-rl">--.-</div>
        <div class="tire-sub"><span id="bar-rl">-- bar</span><span id="temp-rl">-- °C</span></div>
        <div class="tire-id-tag" id="id-rl">ID: 0x00000000</div>
      </div>
      <!-- 右後 RR -->
      <div class="tire-card" id="card-rr">
        <div class="tire-title"><span>右後輪 (RR)</span><span id="stat-rr">待機</span></div>
        <div class="tire-psi" id="psi-rr">--.-</div>
        <div class="tire-sub"><span id="bar-rr">-- bar</span><span id="temp-rr">-- °C</span></div>
        <div class="tire-id-tag" id="id-rr">ID: 0x00000000</div>
      </div>
    </div>

    <!-- 狀態卡 -->
    <div class="card" style="padding: 10px 14px;">
      <div style="display:flex; justify-content:space-between; font-size:0.8rem; color:var(--text-muted);">
        <span>0.91" OLED 螢幕: <b id="oled-stat" style="color:var(--green)">已連線</b></span>
        <span>提示: 對氣嘴洩氣 2 秒可喚醒發射</span>
      </div>
    </div>
  </div>

  <!-- 分頁 2: 射頻協議掃描器控制台 (核心新功能) -->
  <div id="tab-scanner" class="tab-content">
    <div class="card">
      <div class="switch-row">
        <div class="switch-info">
          <h4>全協議自動巡檢掃描 (Auto Scan)</h4>
          <p>每 4 秒自動切換不同調變協議 (FSK / ASK / OOK)，捕捉任意胎壓感測器</p>
        </div>
        <label class="switch">
          <input type="checkbox" id="chk-autoscan" onchange="toggleAutoScan()">
          <span class="slider"></span>
        </label>
      </div>

      <div style="margin-top:10px; font-size:0.8rem; color:var(--text-muted);">
        手動指定鎖定射頻協議 (若已知感測器型態可手動鎖定)：
      </div>

      <div class="mode-grid">
        <button class="btn-mode" id="btn-mode-0" onclick="setScanMode(0)">
          <b>1. 泛捕獲全抓</b>
          <small>433.92M 寬鬆全抓 / 智慧解碼</small>
        </button>
        <button class="btn-mode" id="btn-mode-1" onclick="setScanMode(1)">
          <b>2. FSK 9.6k (CB56)</b>
          <small>433.92M 專屬外置胎壓 (免雜訊)</small>
        </button>
        <button class="btn-mode" id="btn-mode-2" onclick="setScanMode(2)">
          <b>3. FSK 9.6k (D391)</b>
          <small>433.92M 豐田/日系/主流車系</small>
        </button>
        <button class="btn-mode" id="btn-mode-3" onclick="setScanMode(3)">
          <b>4. OOK 4.1k (5569)</b>
          <small>433.92M 太陽能外置主機</small>
        </button>
        <button class="btn-mode" id="btn-mode-4" onclick="setScanMode(4)">
          <b>5. FSK 19.2k</b>
          <small>433.92M 歐美/Schrader</small>
        </button>
        <button class="btn-mode" id="btn-mode-5" onclick="setScanMode(5)">
          <b>6. FSK 4.8k</b>
          <small>433.92M 低速長距專用</small>
        </button>
      </div>

      <div style="margin-top: 14px; text-align: center;">
        <button class="btn-action" style="padding: 8px 16px; background:#475569;" onclick="resetRadio()">
          [重置 CC1101 射頻晶片]
        </button>
      </div>
    </div>
  </div>

  <!-- 分頁 3: 輪位手動配置與防干擾白名單鎖定 -->
  <div id="tab-setup" class="tab-content">
    <!-- 防干擾白名單鎖定開關 -->
    <div class="card">
      <div class="switch-row">
        <div class="switch-info">
          <h4>防干擾白名單鎖定</h4>
          <p>開啟後僅接收綁定的 4 顆輪胎，過濾路上其他車輛雜訊</p>
        </div>
        <label class="switch">
          <input type="checkbox" id="chk-whitelist" onchange="toggleWhitelist()">
          <span class="slider"></span>
        </label>
      </div>
    </div>

    <!-- 雜訊與外車干擾過濾門檻設定 -->
    <div class="card">
      <div class="card-title">
        <span>雜訊與干擾過濾門檻</span>
        <span style="font-size:0.75rem; color:var(--accent);">杜絕跳碼與假訊號</span>
      </div>
      <div style="font-size:0.75rem; color:var(--text-muted); margin-bottom:10px;">
        空氣中存在隨機射頻底噪，透過命中防抖與場強門檻可 100% 濾除假訊號：
      </div>
      <div style="display:grid; grid-template-columns:1fr 1fr; gap:10px; margin-bottom:12px;">
        <div>
          <label style="font-size:0.75rem; color:var(--text-muted); display:block; margin-bottom:4px;">防抖命中次數：</label>
          <select id="sel-hits" style="width:100%; background:#0f172a; color:#fff; border:1px solid #475569; padding:6px; border-radius:6px; font-size:0.8rem;" onchange="updateFilterSettings()">
            <option value="1">1 次 (不防抖，易收雜訊)</option>
            <option value="2">2 次 (預設，過濾瞬態雜訊)</option>
            <option value="3">3 次 (嚴格，需確認 3 次)</option>
            <option value="5">5 次 (極嚴格過濾)</option>
          </select>
        </div>
        <div>
          <label style="font-size:0.75rem; color:var(--text-muted); display:block; margin-bottom:4px;">訊號強度門檻 (RSSI)：</label>
          <select id="sel-rssi" style="width:100%; background:#0f172a; color:#fff; border:1px solid #475569; padding:6px; border-radius:6px; font-size:0.8rem;" onchange="updateFilterSettings()">
            <option value="-105">不限 (-105 dBm)</option>
            <option value="-90">-90 dBm (預設，過濾遠端噪聲)</option>
            <option value="-80">-80 dBm (車輛周遭範圍)</option>
            <option value="-70">-70 dBm (極近氣嘴配對)</option>
          </select>
        </div>
      </div>
      <div style="display:flex; gap:8px;">
        <button class="btn-action" style="flex:1; padding:7px; font-size:0.75rem; background:#1e293b; border-color:#475569;" onclick="clearDiscovered()">
          [清空周遭探索池]
        </button>
        <button class="btn-action" style="flex:1; padding:7px; font-size:0.75rem; background:#1e293b; border-color:#ef4444; color:#fca5a5;" onclick="clearAllTires()">
          [清空四輪綁定歸零]
        </button>
      </div>
    </div>

    <!-- 一鍵快捷調胎 (Tire Rotation) -->
    <div class="card">
      <div class="card-title">一鍵輪胎對調 (Tire Rotation)</div>
      <div style="font-size:0.75rem; color:var(--text-muted); margin-bottom:8px;">進行輪胎對調保養後，點擊對應按鈕立即同步輪位：</div>
      <div class="btn-group">
        <button class="btn-action" onclick="swapTires('front_back')">前後輪對調<br><small>FL↔RL, FR↔RR</small></button>
        <button class="btn-action" onclick="swapTires('left_right')">左右輪對調<br><small>FL↔FR, RL↔RR</small></button>
        <button class="btn-action" onclick="swapTires('cross')">交叉輪對調<br><small>FL↔RR, FR↔RL</small></button>
      </div>
    </div>

    <!-- 四輪位置 ID 手動配置 -->
    <div class="card">
      <div class="card-title">四輪感測器 ID 手動配置</div>
      <div style="font-size:0.75rem; color:var(--text-muted); margin-bottom:10px;">可手動修改 8 碼 HEX ID (例如 0x1A2B3C4D)，修改後點「儲存」永久記憶：</div>
      
      <div class="slot-row">
        <span class="slot-label">左前 (FL)</span>
        <input class="slot-input" id="inp-fl" placeholder="0x00000000">
        <button class="btn-slot-save" onclick="saveManualSlot(0)">儲存</button>
        <button class="btn-slot-clear" onclick="clearSlot(0)">清空</button>
      </div>
      <div class="slot-row">
        <span class="slot-label">右前 (FR)</span>
        <input class="slot-input" id="inp-fr" placeholder="0x00000000">
        <button class="btn-slot-save" onclick="saveManualSlot(1)">儲存</button>
        <button class="btn-slot-clear" onclick="clearSlot(1)">清空</button>
      </div>
      <div class="slot-row">
        <span class="slot-label">左後 (RL)</span>
        <input class="slot-input" id="inp-rl" placeholder="0x00000000">
        <button class="btn-slot-save" onclick="saveManualSlot(2)">儲存</button>
        <button class="btn-slot-clear" onclick="clearSlot(2)">清空</button>
      </div>
      <div class="slot-row">
        <span class="slot-label">右後 (RR)</span>
        <input class="slot-input" id="inp-rr" placeholder="0x00000000">
        <button class="btn-slot-save" onclick="saveManualSlot(3)">儲存</button>
        <button class="btn-slot-clear" onclick="clearSlot(3)">清空</button>
      </div>
    </div>

    <!-- 活躍感測器探索池 (免打字一鍵綁定) -->
    <div class="card">
      <div class="card-title">
        <span>周遭感測器探索學習池</span>
        <span style="font-size:0.75rem; font-weight:normal; color:var(--text-muted);">按壓氣嘴放氣喚醒</span>
      </div>
      <div style="font-size:0.75rem; color:var(--text-muted); margin-bottom:10px;">
        在車旁按壓輪胎氣嘴 2 秒激發感測器，畫面將立即跳出新 ID，點擊下方快捷鍵即可一鍵綁定：
      </div>
      <div id="discovered-list">
        <div style="text-align:center; color:var(--text-muted); padding:16px; font-size:0.8rem;">正在掃描周遭 433MHz 感測器...</div>
      </div>
    </div>

    <!-- 原廠重置與清空記憶 -->
    <div style="text-align: center; margin: 16px 0;">
      <button class="btn-action" style="background:#7f1d1d; border-color:#ef4444; padding:8px 16px;" onclick="resetAllFactory()">
        [一鍵清空所有記憶與輪位 (恢復出廠狀態)]
      </button>
    </div>
  </div>

  <!-- 分頁 4: 原始封包日誌 -->
  <div id="tab-logs" class="tab-content">
    <div class="card">
      <div class="card-title">
        <span>原始 433MHz 封包流</span>
        <button class="btn-action" style="padding: 4px 10px;" onclick="clearLogs()">清空紀錄</button>
      </div>
      <div class="log-list" id="log-list">
        <div style="text-align:center; color:var(--text-muted); padding:16px; font-size:0.8rem;">等待射頻訊號接收...</div>
      </div>
    </div>
  </div>

  <!-- 分頁 5: 線上無線韌體更新 (OTA) -->
  <div id="tab-ota" class="tab-content">
    <div class="card">
      <div class="card-title">
        <span>無線韌體更新 (OTA)</span>
        <span style="font-size:0.75rem; color:var(--green);" id="ota-sys-ver">v2.6 在線</span>
      </div>
      <div style="font-size:0.8rem; color:var(--text-muted); margin-bottom:12px; line-height:1.5;">
        支援免插 USB 傳輸線！只要手機連上此熱點，選取編譯好的 <b>firmware.bin</b> 檔案，點擊開始升級即可無線寫入 ESP8266 Flash！
      </div>

      <!-- 系統硬體規格 -->
      <div style="background:#0f172a; border:1px solid #334155; border-radius:8px; padding:10px; margin-bottom:14px; font-size:0.75rem; display:grid; grid-template-columns:1fr 1fr; gap:6px;">
        <div>剩餘記憶體: <b id="ota-heap" style="color:var(--accent)">--</b></div>
        <div>Flash 容量: <b id="ota-flash" style="color:var(--accent)">4 MB</b></div>
        <div>晶片 MAC/ID: <b id="ota-chipid" style="color:#cbd5e1">--</b></div>
        <div>升級途徑: <b style="color:var(--green)">HTTP POST /update</b></div>
      </div>

      <!-- GitHub 雲端一鍵自動更新 -->
      <div style="background:#0f172a; border:1px solid #38bdf8; border-radius:10px; padding:12px; margin-bottom:14px;">
        <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:6px;">
          <div style="font-size:0.85rem; font-weight:700; color:#38bdf8; display:flex; align-items:center; gap:6px;">
            <span>☁️ GitHub 雲端一鍵自動更新</span>
          </div>
          <span style="font-size:0.7rem; color:#94a3b8;" id="gh-online-stat">點擊檢查雲端版本</span>
        </div>
        <div style="font-size:0.75rem; color:#cbd5e1; margin-bottom:10px; line-height:1.4;">
          免手動下載選檔！直接自 <b>Isaacyang34/Homepage</b> 獲取官方最新韌體並無線寫入：
        </div>
        <div id="gh-version-info" style="display:none; background:#1e293b; padding:8px; border-radius:6px; font-size:0.75rem; margin-bottom:10px; font-family:monospace; border-left:3px solid var(--green);">
          <div>雲端最新: <b id="gh-ver-num" style="color:var(--green)">--</b> (<span id="gh-ver-date">--</span>)</div>
          <div style="color:#cbd5e1; margin-top:2px;" id="gh-ver-notes">--</div>
        </div>
        <button class="btn-action" id="btn-gh-sync" style="width:100%; padding:10px; background:linear-gradient(90deg, #0284c7 0%, #2563eb 100%); color:#fff; font-size:0.85rem; font-weight:bold; border:none;" onclick="checkAndSyncGitHub()">
          [檢查 GitHub 最新版本並一鍵更新]
        </button>
      </div>

      <!-- 離線備用: 本機檔案選擇與上傳 -->
      <div style="font-size:0.75rem; color:var(--text-muted); margin-bottom:6px; font-weight:600;">備用方式：手動上傳本機二進制檔 (.bin)</div>
      <div style="margin-bottom:12px;">
        <input type="file" id="ota-file-input" accept=".bin,.bin.gz" style="display:none;" onchange="onOtaFileSelected()">
        <button class="btn-action" style="width:100%; padding:10px; background:#1e293b; border:1px dashed #475569; color:#cbd5e1; font-size:0.85rem;" onclick="document.getElementById('ota-file-input').click()">
          [手動選取本機韌體檔案 (.bin)]
        </button>
        <div id="ota-file-name" style="text-align:center; font-size:0.75rem; color:#cbd5e1; margin-top:6px; font-family:monospace;">尚未選取檔案</div>
      </div>

      <button class="btn-action" id="btn-start-ota" style="width:100%; padding:10px; background:#334155; color:#cbd5e1; font-size:0.85rem; font-weight:bold; border:none;" onclick="uploadOtaFirmware()">
        上傳手動選取的韌體
      </button>

      <!-- 進度條 -->
      <div id="ota-progress-box" style="display:none; margin-top:14px;">
        <div class="meter-track" style="height:16px;">
          <div class="meter-bar" id="ota-progress-bar" style="width:0%; background:var(--green);"></div>
        </div>
        <div id="ota-status-text" style="font-size:0.75rem; text-align:center; margin-top:6px; color:#cbd5e1;">準備中...</div>
      </div>

      <!-- 獨立網址備用入口 -->
      <div style="margin-top:16px; border-top:1px dashed #334155; padding-top:10px; text-align:center; font-size:0.75rem; color:var(--text-muted);">
        備用原生入口：可直接瀏覽 <a href="/update" target="_blank" style="color:var(--accent); text-decoration:underline;">http://192.168.4.1/update</a>
      </div>
    </div>
  </div>

  <script>
    let currentConfig = { lock: false, ids: [0, 0, 0, 0] };

    const switchTab = (tabId) => {
      const tabNames = ['dashboard', 'scanner', 'setup', 'logs', 'ota'];
      document.querySelectorAll('.tab-btn').forEach((btn, idx) => {
        btn.classList.toggle('active', tabNames[idx] === tabId);
      });
      document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
      document.getElementById('tab-' + tabId).classList.add('active');
    };

    // 高頻率射頻雷達輪詢 (每 250ms 一次，提供流暢場強針)
    const updateRfHud = async () => {
      try {
        const res = await fetch('/api/rf_scan');
        const data = await res.json();
        
        // 1. 場強數值與條
        const rssi = data.rssi;
        const peak = data.peakRssi;
        document.getElementById('hud-rssi').innerText = rssi.toFixed(1);
        document.getElementById('hud-peak').innerText = peak.toFixed(1);

        // 換算成百分比 (-115dBm = 0%, -40dBm = 100%)
        let pct = Math.round(((rssi + 115) / 75) * 100);
        pct = Math.max(2, Math.min(100, pct));
        document.getElementById('hud-meter-bar').style.width = pct + '%';

        // 2. 脈衝強訊號提示 (> -75 dBm)
        const surgeEl = document.getElementById('hud-surge');
        if (rssi > -75.0 || data.surge) {
          surgeEl.style.display = 'block';
          surgeEl.innerText = `[!] 偵測到 433MHz 強烈射頻脈衝 (${rssi.toFixed(1)} dBm)！感測器發射中！`;
        } else {
          surgeEl.style.display = 'none';
        }

        // 3. 模式徽章
        document.getElementById('hud-mode-name').innerText = data.scanMode + (data.autoScan ? " (自動巡檢)" : " (手動鎖定)");
        document.getElementById('chk-autoscan').checked = data.autoScan;

        // 4. 高亮當前選中按鈕
        for (let i = 0; i < 6; i++) {
          const btn = document.getElementById('btn-mode-' + i);
          if (btn) btn.classList.toggle('active', i === data.scanModeIdx);
        }

        // 5. 底層硬體狀態
        document.getElementById('hw-chip').innerText = data.chipVerDesc;
        document.getElementById('hw-marc').innerText = data.marcStateDesc;
        document.getElementById('hw-gdo0').innerText = data.gdo0;
        document.getElementById('hw-pkts').innerText = data.totalPackets;
      } catch (err) {}
    };

    // 距離與訊號強弱換算函式 (依據 433.92MHz 實測場強衰減模型)
    const getProximityInfo = (rssi) => {
      if (rssi >= -65.0) {
        return {
          bars: 4,
          colorClass: 'sig-act-green',
          tagClass: 'prox-immediate',
          text: '🔥 極近 (&lt;1m / 氣嘴放氣中)'
        };
      } else if (rssi >= -80.0) {
        return {
          bars: 3,
          colorClass: 'sig-act-cyan',
          tagClass: 'prox-near',
          text: '🚗 近距 (1~3m / 本車輪位)'
        };
      } else if (rssi >= -92.0) {
        return {
          bars: 2,
          colorClass: 'sig-act-yellow',
          tagClass: 'prox-mid',
          text: '⚠️ 中距 (3~8m / 鄰車周遭)'
        };
      } else {
        return {
          bars: 1,
          colorClass: 'sig-act-gray',
          tagClass: 'prox-far',
          text: '📡 遠距 (&gt;8m / 微弱底噪)'
        };
      }
    };

    const renderSignalBars = (prox) => {
      return `
        <div class="sig-meter" title="訊號強度: ${prox.bars}/4 格">
          <div class="sig-bar b1 ${prox.bars >= 1 ? prox.colorClass : ''}"></div>
          <div class="sig-bar b2 ${prox.bars >= 2 ? prox.colorClass : ''}"></div>
          <div class="sig-bar b3 ${prox.bars >= 3 ? prox.colorClass : ''}"></div>
          <div class="sig-bar b4 ${prox.bars >= 4 ? prox.colorClass : ''}"></div>
        </div>
      `;
    };

    // 常規儀表板更新 (每 1000ms 一次)
    const updateDashboard = async () => {
      try {
        const res = await fetch('/api/data');
        const data = await res.json();
        
        // 1. 更新頂部鎖定狀態與系統資訊
        currentConfig.lock = data.config.lockWhitelist;
        currentConfig.minHits = data.config.minHits;
        currentConfig.minRssi = data.config.minRssi;

        const selHits = document.getElementById('sel-hits');
        const selRssi = document.getElementById('sel-rssi');
        if (selHits && !selHits.matches(':focus') && data.config.minHits) {
          selHits.value = data.config.minHits;
        }
        if (selRssi && !selRssi.matches(':focus') && data.config.minRssi !== undefined) {
          selRssi.value = data.config.minRssi;
        }

        const badge = document.getElementById('badge-lock');
        const chk = document.getElementById('chk-whitelist');
        if (data.config.lockWhitelist) {
          badge.className = 'badge-lock badge-locked';
          badge.innerText = '🛡️ 防干擾: 已鎖定';
          chk.checked = true;
        } else {
          badge.className = 'badge-lock badge-open';
          badge.innerText = '學習模式: 未鎖定';
          chk.checked = false;
        }

        if (data.sys) {
          document.getElementById('ota-heap').innerText = (data.sys.freeHeap / 1024).toFixed(1) + ' KB';
          document.getElementById('ota-flash').innerText = (data.sys.flashSize / (1024 * 1024)).toFixed(0) + ' MB';
          document.getElementById('ota-chipid').innerText = data.sys.chipId;
          document.getElementById('ota-sys-ver').innerText = data.sys.version + ' 在線';
        }

        document.getElementById('oled-stat').innerText = data.oled ? "已連線" : "未偵測";

        // 2. 更新四輪數值
        const keys = ['fl', 'fr', 'rl', 'rr'];
        data.tires.forEach((t, i) => {
          const key = keys[i];
          const card = document.getElementById('card-' + key);
          const inp = document.getElementById('inp-' + key);
          const hexId = '0x' + (t.id ? t.id.toString(16).toUpperCase().padStart(8, '0') : '00000000');
          
          if (!inp.matches(':focus')) {
            inp.value = t.id ? hexId : '';
          }

          document.getElementById('id-' + key).innerText = 'ID: ' + hexId;
          
          if (t.valid) {
            card.classList.add('active');
            document.getElementById('psi-' + key).innerHTML = `${t.psi.toFixed(1)} <small style="font-size:0.9rem">psi</small>`;
            document.getElementById('bar-' + key).innerText = `${t.bar.toFixed(2)} bar`;
            document.getElementById('temp-' + key).innerText = `${t.temp} °C`;
            document.getElementById('stat-' + key).innerText = '正常';
            document.getElementById('stat-' + key).style.color = 'var(--green)';
          } else {
            card.classList.remove('active');
            document.getElementById('psi-' + key).innerHTML = '--.-';
            document.getElementById('bar-' + key).innerText = '-- bar';
            document.getElementById('temp-' + key).innerText = '-- °C';
            document.getElementById('stat-' + key).innerText = t.id ? '等待訊號' : '未綁定';
            document.getElementById('stat-' + key).style.color = 'var(--text-muted)';
          }
        });

        // 3. 更新探索池 (整合遠近訊號圖示與距離預估)
        const discList = document.getElementById('discovered-list');
        const minHits = (data.config && data.config.minHits) ? data.config.minHits : 2;
        if (data.discovered && data.discovered.length > 0) {
          discList.innerHTML = data.discovered.map(d => {
            const hexId = '0x' + d.id.toString(16).toUpperCase().padStart(8, '0');
            const isVerified = (d.count >= minHits);
            const prox = getProximityInfo(d.rssi);
            const badgeHtml = isVerified 
              ? `<span style="background:rgba(34,197,94,0.2); color:var(--green); border:1px solid var(--green); padding:1px 5px; border-radius:4px; font-size:0.68rem; font-weight:bold;">[已驗證 (命中 ${d.count} 次)]</span>`
              : `<span style="background:rgba(245,158,11,0.2); color:var(--yellow); border:1px solid var(--yellow); padding:1px 5px; border-radius:4px; font-size:0.68rem;">[候選暫態 (${d.count}/${minHits} 次)]</span>`;
            return `
              <div class="disc-item" style="${isVerified ? 'border-color:#38bdf8;' : 'opacity:0.85;'}">
                <div class="disc-head">
                  <div style="display:flex; align-items:center; flex-wrap:wrap; gap:4px;">
                    ${renderSignalBars(prox)}
                    <span class="disc-id">${hexId}</span>
                    <span class="prox-tag ${prox.tagClass}">${prox.text}</span>
                    ${badgeHtml}
                  </div>
                  <span class="disc-meta">${d.rssi.toFixed(0)} dBm | ${d.psi.toFixed(1)} psi | ${d.temp}°C</span>
                </div>
                <div class="disc-bind-btns">
                  <button class="btn-bind" onclick="bindSensor(0, '${hexId}')">設為 FL</button>
                  <button class="btn-bind" onclick="bindSensor(1, '${hexId}')">設為 FR</button>
                  <button class="btn-bind" onclick="bindSensor(2, '${hexId}')">設為 RL</button>
                  <button class="btn-bind" onclick="bindSensor(3, '${hexId}')">設為 RR</button>
                </div>
              </div>
            `;
          }).join('');
        } else {
          discList.innerHTML = '<div style="text-align:center; color:var(--text-muted); padding:16px; font-size:0.8rem;">正在掃描周遭 433MHz 感測器...</div>';
        }

        // 4. 更新日誌
        const list = document.getElementById('log-list');
        if (data.logs && data.logs.length > 0) {
          list.innerHTML = data.logs.map(log => `
            <div class="log-item ${log.filtered ? 'filtered' : ''}">
              <div class="log-meta">
                <span>#${log.id} | RSSI: ${log.rssi.toFixed(1)} dBm ${log.filtered ? '<span style="color:var(--yellow)">[已攔截外來訊號]</span>' : ''}</span>
                <span>長度: ${log.len} B</span>
              </div>
              <div class="log-hex">${log.hex}</div>
            </div>
          `).join('');
        } else {
          list.innerHTML = '<div style="text-align:center; color:var(--text-muted); padding:16px; font-size:0.8rem;">等待射頻訊號接收...</div>';
        }
      } catch (err) {}
    };

    // 射頻協議操作
    const toggleAutoScan = async () => {
      const enabled = document.getElementById('chk-autoscan').checked ? 1 : 0;
      await fetch(`/api/set_mode?auto=${enabled}`, { method: 'POST' });
      updateRfHud();
    };

    const setScanMode = async (idx) => {
      await fetch(`/api/set_mode?mode=${idx}&auto=0`, { method: 'POST' });
      updateRfHud();
    };

    const resetRadio = async () => {
      await fetch('/api/reset_rf', { method: 'POST' });
      alert('CC1101 射頻晶片已重新初始化！');
      updateRfHud();
    };

    // 防干擾與輪位
    const toggleWhitelist = async () => {
      const enabled = document.getElementById('chk-whitelist').checked ? 1 : 0;
      await fetch(`/api/config?lock=${enabled}`, { method: 'POST' });
      updateDashboard();
    };

    const saveManualSlot = async (pos) => {
      const keys = ['fl', 'fr', 'rl', 'rr'];
      const val = document.getElementById('inp-' + keys[pos]).value.trim();
      if (!val) return;
      await fetch(`/api/bind?pos=${pos}&id=${encodeURIComponent(val)}`, { method: 'POST' });
      updateDashboard();
    };

    const clearSlot = async (pos) => {
      if (confirm(`確定清空此輪胎綁定？`)) {
        await fetch(`/api/clear_slot?pos=${pos}`, { method: 'POST' });
        updateDashboard();
      }
    };

    const bindSensor = async (pos, hexId) => {
      const posNames = ['左前輪 (FL)', '右前輪 (FR)', '左後輪 (RL)', '右後輪 (RR)'];
      await fetch(`/api/bind?pos=${pos}&id=${encodeURIComponent(hexId)}`, { method: 'POST' });
      alert(`已將感測器 ${hexId} 綁定至 ${posNames[pos]}！`);
      updateDashboard();
    };

    const swapTires = async (mode) => {
      const modeDesc = {
        'front_back': '前後輪對調 (FL↔RL, FR↔RR)',
        'left_right': '左右輪對調 (FL↔FR, RL↔RR)',
        'cross': '交叉輪對調 (FL↔RR, FR↔RL)'
      };
      if (confirm(`確定執行「${modeDesc[mode]}」？`)) {
        await fetch(`/api/swap?mode=${mode}`, { method: 'POST' });
        updateDashboard();
      }
    };

    const resetAllFactory = async () => {
      if (confirm('確定清空所有輪位綁定與快閃記憶體設定，恢復出廠空白狀態？')) {
        await fetch('/api/reset_all', { method: 'POST' });
        alert('已恢復出廠狀態，所有輪位與歷史封包已清空！');
        updateDashboard();
      }
    };

    const updateFilterSettings = async () => {
      const hits = document.getElementById('sel-hits').value;
      const rssi = document.getElementById('sel-rssi').value;
      await fetch(`/api/config?hits=${hits}&rssi=${rssi}`, { method: 'POST' });
      updateDashboard();
    };

    const clearDiscovered = async () => {
      await fetch('/api/clear_discovered', { method: 'POST' });
      document.getElementById('discovered-list').innerHTML = '<div style="text-align:center; color:var(--text-muted); padding:16px; font-size:0.8rem;">探索學習池已清空</div>';
      updateDashboard();
    };

    const clearAllTires = async () => {
      if (confirm('確定清空目前四個輪位的感測器綁定（全數歸零）？')) {
        await fetch('/api/clear_all_tires', { method: 'POST' });
        updateDashboard();
      }
    };

    const onOtaFileSelected = () => {
      const fi = document.getElementById('ota-file-input');
      const nameEl = document.getElementById('ota-file-name');
      if (fi.files.length > 0) {
        const file = fi.files[0];
        nameEl.innerText = `${file.name} (${(file.size / 1024).toFixed(1)} KB)`;
      } else {
        nameEl.innerText = '尚未選取檔案';
      }
    };

    const uploadOtaFirmware = () => {
      const fi = document.getElementById('ota-file-input');
      if (!fi.files || !fi.files.length) {
        alert('請先點擊按鈕選取 .bin 韌體檔案！');
        return;
      }
      const file = fi.files[0];
      if (!confirm(`確定將「${file.name}」無線燒錄至 ESP8266？升級期間請勿斷電！`)) {
        return;
      }

      const formData = new FormData();
      formData.append('firmware', file, file.name);

      const xhr = new XMLHttpRequest();
      xhr.open('POST', '/update', true);

      const progressBox = document.getElementById('ota-progress-box');
      const progressBar = document.getElementById('ota-progress-bar');
      const statusText = document.getElementById('ota-status-text');
      const startBtn = document.getElementById('btn-start-ota');

      progressBox.style.display = 'block';
      startBtn.disabled = true;
      startBtn.style.opacity = '0.5';
      statusText.innerText = '正在傳輸固件至開發板...';

      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) {
          const pct = Math.round((e.loaded / e.total) * 100);
          progressBar.style.width = pct + '%';
          statusText.innerText = `傳輸進度: ${pct}% (${(e.loaded/1024).toFixed(0)} KB / ${(e.total/1024).toFixed(0)} KB)`;
        }
      };

      xhr.onload = () => {
        if (xhr.status === 200) {
          progressBar.style.width = '100%';
          statusText.innerHTML = '<span style="color:var(--green)">[✓] 韌體上傳成功！ESP8266 正在寫入 Flash 並重啟，請等待 8 秒後自動重整...</span>';
          setTimeout(() => { location.reload(true); }, 8000);
        } else {
          statusText.innerHTML = `<span style="color:var(--red)">升級失敗: ${xhr.responseText || xhr.statusText}</span>`;
          startBtn.disabled = false;
          startBtn.style.opacity = '1';
        }
      };

      xhr.onerror = () => {
        statusText.innerHTML = '<span style="color:var(--red)">連線異常，請確認手機仍連接在 TPMS_PoC_Tester 熱點！</span>';
        startBtn.disabled = false;
        startBtn.style.opacity = '1';
      };

      xhr.send(formData);
    };

    // GitHub 雲端一鍵自動更新函式 (自動抓取官方 Raw 二進制檔並直通寫入)
    const GITHUB_REPO = "Isaacyang34/Homepage";
    const GITHUB_BRANCH = "gh-pages";
    const GITHUB_VERSION_URL = `https://raw.githubusercontent.com/${GITHUB_REPO}/${GITHUB_BRANCH}/ESP32_TPMS_Receiver/poc_minimal_tester/version.json?t=${Date.now()}`;
    const GITHUB_BIN_URL = `https://raw.githubusercontent.com/${GITHUB_REPO}/${GITHUB_BRANCH}/ESP32_TPMS_Receiver/poc_minimal_tester/firmware.bin?t=${Date.now()}`;

    const checkAndSyncGitHub = async () => {
      const statEl = document.getElementById('gh-online-stat');
      const btn = document.getElementById('btn-gh-sync');
      const infoEl = document.getElementById('gh-version-info');
      const pBox = document.getElementById('ota-progress-box');
      const pBar = document.getElementById('ota-progress-bar');
      const pText = document.getElementById('ota-status-text');

      btn.disabled = true;
      btn.style.opacity = '0.6';
      statEl.innerText = "正在連線 GitHub 查詢...";

      try {
        const res = await fetch(GITHUB_VERSION_URL);
        if (!res.ok) throw new Error("無法連線至 GitHub (HTTP " + res.status + ")");
        const meta = await res.json();

        infoEl.style.display = "block";
        document.getElementById('gh-ver-num').innerText = meta.version;
        document.getElementById('gh-ver-date').innerText = meta.date || "";
        document.getElementById('gh-ver-notes').innerText = meta.changelog || "最新穩定版本";

        statEl.innerText = "已獲取雲端資訊";

        if (confirm(`發現 GitHub 雲端最新版本: ${meta.version}\n說明: ${meta.changelog || ''}\n\n確定立即從 GitHub 下載並直接無線燒錄至 ESP8266？`)) {
          pBox.style.display = "block";
          pBar.style.width = "10%";
          pText.innerText = "正在自 GitHub 雲端下載最新韌體二進制檔...";

          const binRes = await fetch(GITHUB_BIN_URL);
          if (!binRes.ok) throw new Error("下載韌體失敗: " + binRes.statusText);
          const blob = await binRes.blob();

          pBar.style.width = "40%";
          pText.innerText = `韌體下載完成 (${(blob.size / 1024).toFixed(1)} KB)，正在傳輸寫入 ESP8266 Flash...`;

          const formData = new FormData();
          formData.append("firmware", blob, "firmware.bin");

          const xhr = new XMLHttpRequest();
          xhr.open("POST", "/update", true);

          xhr.upload.onprogress = (e) => {
            if (e.lengthComputable) {
              const pct = 40 + Math.round((e.loaded / e.total) * 58);
              pBar.style.width = pct + "%";
              pText.innerText = `正在無線寫入 Flash... ${pct}%`;
            }
          };

          xhr.onload = () => {
            if (xhr.status === 200) {
              pBar.style.width = "100%";
              pText.innerHTML = '<b style="color:var(--green)">[✓ 更新成功] ESP8266 正在重啟... 8 秒後自動重載新韌體！</b>';
              setTimeout(() => { window.location.reload(true); }, 8000);
            } else {
              pText.innerHTML = `<b style="color:var(--red)">[寫入失敗] 伺服器回傳 ${xhr.status}</b>`;
              btn.disabled = false;
              btn.style.opacity = '1';
            }
          };

          xhr.onerror = () => {
            pText.innerHTML = '<b style="color:var(--red)">[傳輸錯誤] 連線中斷！</b>';
            btn.disabled = false;
            btn.style.opacity = '1';
          };

          xhr.send(formData);
        } else {
          btn.disabled = false;
          btn.style.opacity = '1';
        }
      } catch (err) {
        alert("GitHub 雲端更新失敗: " + err.message + "\n若目前環境無法連外網，請改用下方手動選取 .bin 上傳。");
        statEl.innerText = "連線失敗";
        btn.disabled = false;
        btn.style.opacity = '1';
      }
    };

    // 定時器
    setInterval(updateRfHud, 300);     // 300ms 刷新射頻雷達
    setInterval(updateDashboard, 1200); // 1.2s 刷新儀表
    updateRfHud();
    updateDashboard();
  </script>
</body>
</html>
)rawliteral";
