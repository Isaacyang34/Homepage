# -*- coding: utf-8 -*-
import sys

html_path = "TOOL_Stock_Portfolio_Tracker/web/index.html"
with open(html_path, "r", encoding="utf-8") as f:
    html = f.read()

# 1. 確保主 Tab 按鈕存在
if 'id="tab-btn-fundamental"' not in html:
    html = html.replace(
        '</button>\n      <button id="tab-btn-etf"',
        '</button>\n      <button id="tab-btn-fundamental" class="main-tab-btn"><span class="tab-icon">🌊</span> 個股基本面深度分析 & 價值河流圖</button>\n      <button id="tab-btn-etf"'
    )

# 2. 確保子 Tab 按鈕存在
if 'id="sub-tab-btn-postmarket"' not in html:
    html = html.replace(
        '<button id="sub-tab-btn-market" class="sub-tab-btn active">',
        '<button id="sub-tab-btn-postmarket" class="sub-tab-btn">[量化] 盤後五維度綜合排名與市場環境</button>\n        <button id="sub-tab-btn-market" class="sub-tab-btn active">'
    )

# 3. 確保 fundamental-tab-content 存在
if 'id="fundamental-tab-content"' not in html:
    fundamental_block = """
    <!-- 區塊 4：個股基本面深度分析 & 價值河流圖 VIEW -->
    <div id="fundamental-tab-content" class="hidden">
      <!-- 頂部切換個股與即時數據 -->
      <div class="panel glass-panel" style="margin-bottom: 1.5rem; border-radius: 12px; padding: 1.25rem 1.5rem;">
        <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 1rem;">
          <div style="display: flex; align-items: center; gap: 0.75rem;">
            <h2 style="font-size: 1.25rem; font-weight: 700; color: #fff; margin: 0;">🌊 個股基本面與價值河流圖分析</h2>
            <select id="fundamental-stock-selector" onchange="renderFundamentalAnalysis(this.value)" style="background: rgba(255,255,255,0.08); color: #fff; border: 1px solid var(--border-color); border-radius: 8px; padding: 0.4rem 0.8rem; font-size: 0.95rem; font-weight: 600;">
              <option value="2330.TW">2330 台積電 (TSMC)</option>
              <option value="2002.TW">2002 中鋼</option>
              <option value="00878.TW">00878 國泰永續高股息</option>
              <option value="00919.TW">00919 群益精選高息</option>
            </select>
          </div>
          <div style="display: flex; gap: 1.5rem; align-items: center; flex-wrap: wrap;">
            <div>參考現價: <span id="fd-price" style="font-size: 1.25rem; font-weight: 700; color: #38bdf8;">2,490 元</span></div>
            <div>近四季 EPS: <span id="fd-eps" style="font-weight: 700; color: #00f090;">102.5 元</span></div>
            <div>目前本益比: <span id="fd-pe" style="font-weight: 700;">24.3 倍</span></div>
            <div id="fd-status-badge" class="cloud-badge" style="background: rgba(56, 189, 248, 0.15); color: #38bdf8; padding: 4px 10px; border-radius: 6px; font-weight: 600;">合理常態 (24x 河流帶)</div>
          </div>
        </div>
      </div>

      <!-- 四大核心圖表網格 -->
      <div class="kpi-grid" style="grid-template-columns: repeat(auto-fit, minmax(480px, 1fr)); gap: 1.5rem;">
        <!-- 圖表 1：本益比河流圖 (P/E River Band) -->
        <div class="panel glass-panel" style="border-radius: 12px; padding: 1.25rem;">
          <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
            <h3 style="font-size: 1.05rem; font-weight: 600; color: #fff; margin: 0;">🌊 本益比河流圖 (P/E River Band)</h3>
            <span style="font-size: 0.8rem; color: #94a3b8;">16x / 20x / 24x / 28x / 32x 五階估值帶</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="peRiverChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.82rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>河流圖位階判讀</b>：股價落在 16x~20x（河流下緣）具高度安全邊際；突破 28x~32x（河流上緣）為高估超買警示區。
          </div>
        </div>

        <!-- 圖表 2：月營收與年增率 (YoY) -->
        <div class="panel glass-panel" style="border-radius: 12px; padding: 1.25rem;">
          <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
            <h3 style="font-size: 1.05rem; font-weight: 600; color: #fff; margin: 0;">📊 月營收與年增率 (YoY) 走勢</h3>
            <span style="font-size: 0.8rem; color: #94a3b8;">柱狀：營收(億元) / 折線：YoY (%)</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="revYoyChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.82rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>成長動能檢定</b>：單月營收維持高檔且年增率 (YoY) 維持雙位數正成長，代表營收動能充沛。
          </div>
        </div>

        <!-- 圖表 3：獲利能力分析 (三率走勢) -->
        <div class="panel glass-panel" style="border-radius: 12px; padding: 1.25rem;">
          <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
            <h3 style="font-size: 1.05rem; font-weight: 600; color: #fff; margin: 0;">📈 獲利能力三率走勢 (毛利率/營益率/淨利率)</h3>
            <span style="font-size: 0.8rem; color: #94a3b8;">近 5 季對比</span>
          </div>
          <div style="height: 260px; position: relative;">
            <canvas id="marginsChart"></canvas>
          </div>
          <div style="margin-top: 0.75rem; font-size: 0.82rem; color: #94a3b8; line-height: 1.5;">
            💡 <b>定價權分析</b>：營業毛利率與淨利率維持擴張趨勢，反映具備核心技術定價權與成本控制能力。
          </div>
        </div>

        <!-- 模組 4：杜邦分析拆解與安全性檢測 -->
        <div class="panel glass-panel" style="border-radius: 12px; padding: 1.25rem;">
          <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem;">
            <h3 style="font-size: 1.05rem; font-weight: 600; color: #fff; margin: 0;">🛡️ 杜邦分析拆解 (DuPont) & 安全性檢測</h3>
          </div>
          <div style="display: grid; grid-template-columns: repeat(3, 1fr); gap: 0.75rem; margin-bottom: 1rem;">
            <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 0.75rem; text-align: center;">
              <div style="font-size: 0.75rem; color: #94a3b8;">稅後淨利率</div>
              <div id="dp-net-margin" style="font-size: 1.3rem; font-weight: 700; color: #38bdf8;">43.2%</div>
            </div>
            <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 0.75rem; text-align: center;">
              <div style="font-size: 0.75rem; color: #94a3b8;">資產周轉率</div>
              <div id="dp-asset-turnover" style="font-size: 1.3rem; font-weight: 700; color: #facc15;">0.65 次</div>
            </div>
            <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 0.75rem; text-align: center;">
              <div style="font-size: 0.75rem; color: #94a3b8;">權益乘數</div>
              <div id="dp-equity-multiplier" style="font-size: 1.3rem; font-weight: 700; color: #a78bfa;">1.48 倍</div>
            </div>
          </div>
          <div style="background: rgba(0, 240, 144, 0.08); border: 1px solid rgba(0,240,144,0.3); border-radius: 8px; padding: 0.8rem 1rem; margin-bottom: 1rem;">
            <div style="font-weight: 700; color: #00f090; margin-bottom: 0.2rem;">🎯 綜合 ROE 達 <span id="dp-roe">41.5%</span></div>
            <div id="dp-verdict" style="font-size: 0.82rem; color: #cbd5e1;">頂級定價權護城河 (淨利率 > 40% 且超低槓桿保守結構)</div>
          </div>
          <div style="background: rgba(255,255,255,0.03); border: 1px solid var(--border-color); border-radius: 8px; padding: 0.8rem 1rem;">
            <div style="font-weight: 600; margin-bottom: 0.5rem; color: #fff; font-size: 0.85rem;">🔒 財務體質安全性指標</div>
            <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.5rem; font-size: 0.82rem;">
              <div>自由現金流: <b id="sec-fcf" style="color: #00f090;">+6,200 億</b></div>
              <div>營業現金流: <b id="sec-ocf" style="color: #00f090;">+1.15 兆</b></div>
              <div>負債比率: <b id="sec-debt" style="color: #38bdf8;">32.5% (穩健)</b></div>
              <div>流動比率: <b id="sec-current" style="color: #38bdf8;">215% (安全)</b></div>
            </div>
          </div>
        </div>
      </div>
    </div>
"""
    # 找到結尾的 <nav class="mobile-bottom-bar"
    target_pos = html.find('<nav class="mobile-bottom-bar')
    if target_pos != -1:
        html = html[:target_pos] + fundamental_block + "\n\n    " + html[target_pos:]

# 4. 確保 sub-content-postmarket 存在
if 'id="sub-content-postmarket"' not in html:
    postmarket_block = """
        <!-- 子區塊 3：[量化] 盤後五維度綜合排名與市場環境 -->
        <div id="sub-content-postmarket" class="hidden">
          <div class="panel glass-panel" style="margin-bottom: 1.5rem; border-radius: 12px; padding: 1.25rem;">
            <div class="panel-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem;">
              <h3 style="font-size: 1.1rem; font-weight: 700; color: #fff; margin: 0;">🌐 盤後市場環境層 (Market Regime) 狀態指針</h3>
              <span class="cloud-badge" style="background: rgba(0, 240, 144, 0.15); color: #00f090; font-weight: 700;">多頭擴張 (Bullish Expansion)</span>
            </div>
            <div class="kpi-grid" style="grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem; margin-bottom: 1rem;">
              <div class="kpi-card" style="padding: 1rem;">
                <div class="kpi-title">大盤指數 vs 季線 (MA60)</div>
                <div class="kpi-value" style="font-size: 1.3rem; color: #00f090;">23,850 <span style="font-size: 0.85rem;">(+4.06%)</span></div>
                <div class="kpi-sub">季線 22,920 多方支撐</div>
              </div>
              <div class="kpi-card" style="padding: 1rem;">
                <div class="kpi-title">市場寬度 (上漲 / 下跌比)</div>
                <div class="kpi-value" style="font-size: 1.3rem; color: #38bdf8;">1.38 倍</div>
                <div class="kpi-sub">上漲 1042 家 / 下跌 755 家</div>
              </div>
              <div class="kpi-card" style="padding: 1rem;">
                <div class="kpi-title">外資期貨淨留倉 (Net OI)</div>
                <div class="kpi-value" style="font-size: 1.3rem; color: #facc15;">-8,500 口</div>
                <div class="kpi-sub">空單水位健康 (< -15000警戒)</div>
              </div>
              <div class="kpi-card" style="padding: 1rem;">
                <div class="kpi-title">選擇權 Put/Call Ratio</div>
                <div class="kpi-value" style="font-size: 1.3rem; color: #00f090;">112.5%</div>
                <div class="kpi-sub">多方支撐力道強</div>
              </div>
            </div>
          </div>

          <div class="panel glass-panel" style="border-radius: 12px; padding: 1.25rem;">
            <div class="table-responsive">
              <table class="perf-table" id="postmarket-rank-table">
                <thead>
                  <tr>
                    <th style="width: 50px;">名次</th>
                    <th>代號 / 名稱</th>
                    <th>收盤現價</th>
                    <th>綜合評分</th>
                    <th>動能</th>
                    <th>籌碼</th>
                    <th>品質</th>
                    <th>河流圖估值位階</th>
                    <th>ATR波動</th>
                    <th>建議部位</th>
                    <th>量化評語</th>
                    <th>操作</th>
                  </tr>
                </thead>
                <tbody id="postmarket-rank-tbody">
                </tbody>
              </table>
            </div>
          </div>
        </div>
"""
    # 插入在 sub-content-market 結尾後面
    target_sub = html.find('id="sub-content-market"')
    if target_sub != -1:
        # 找到其後的關閉標籤
        close_pos = html.find('</div>\n\n    <!-- 區塊 4', target_sub)
        if close_pos != -1:
            html = html[:close_pos+6] + postmarket_block + html[close_pos+6:]
        else:
            # 備用位置：直接在 sub-content-margin 之前或後
            close_pos2 = html.find('</section>\n      </div>\n\n    <!-- 區塊 4')
            if close_pos2 != -1:
                html = html[:close_pos2] + postmarket_block + html[close_pos2:]

with open(html_path, "w", encoding="utf-8") as f:
    f.write(html)

print("Injected successfully!")

