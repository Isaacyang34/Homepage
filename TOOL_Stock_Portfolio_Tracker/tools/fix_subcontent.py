# -*- coding: utf-8 -*-
path = "TOOL_Stock_Portfolio_Tracker/web/index.html"
with open(path, "r", encoding="utf-8") as f:
    html = f.read()

sub_postmarket_html = """
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

if 'id="sub-content-postmarket"' not in html:
    target = 'id="market-ranking-table"'
    idx = html.find(target)
    if idx != -1:
        # 往後找到 section 或 div 結尾
        end_sec = html.find('</section>', idx)
        if end_sec != -1:
            end_div = html.find('</div>', end_sec)
            if end_div != -1:
                html = html[:end_div+6] + "\n" + sub_postmarket_html + html[end_div+6:]

with open(path, "w", encoding="utf-8") as f:
    f.write(html)

print("Injected sub-content-postmarket successfully!")

