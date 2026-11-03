# -*- coding: utf-8 -*-
import json

version_file = "TOOL_Stock_Portfolio_Tracker/version.json"
with open(version_file, "r", encoding="utf-8") as f:
    data = json.load(f)

new_version = "V1.2.0.0"
new_date = "2026-11-03"
new_changelog = """【V1.2.0.0 個股基本面深度分析 & 盤後量化五維度排名重大發布】

1. [個股基本面深度分析 & 價值河流圖 (Fundamental River Valuation)]：
   - 本益比河流圖 (P/E River Band 16x~32x) 歷史五階估值位階與即時收盤價疊加。
   - 月營收與年增率 (YoY) 雙軸圖表、獲利能力三率走勢 (毛利率、營益率、淨利率)。
   - 杜邦分析拆解卡 (ROE = 純益率 × 資產周轉率 × 權益乘數) 與財務安全性指標 (自由現金流、負債比、流動比)。
2. [盤後五維度綜合排名與市場環境層 (Market Regime)]：
   - 大盤季線 (MA60)、市場寬度、外資期貨淨留倉、選擇權 Put/Call Ratio 指針。
   - 自適應動態調權矩陣（多頭動能籌碼偏重、空頭品質價值偏重）。
   - 盤後多維量化排行榜：綜合評分榜、強勢動能榜、獲利三率榜、河流低估榜。
3. [點擊個股一鍵導入深度分析]：
   - 庫存部位明細與盤後排名表，點擊股票代碼或「🔍 深度分析」操作按鈕一鍵直達個股分析。
4. [五層漏斗篩選與 ATR 波動度部位風控]：
   - 硬性濾網淘汰賽、五因子百分位評分、ATR 波動度平價建議倉位 (5%~25%) 與可解釋決策理由。"""

# 更新頂部版本
data["version"] = new_version
data["release_date"] = new_date
data["changelog"] = new_changelog
data["download_url"] = f"https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/Stock_Portfolio_Tracker.exe"
data["patch_url"] = f"https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/dist_patches/patch_{new_version}.zip"
data["full_package_url"] = f"https://raw.githubusercontent.com/Isaacyang34/Homepage/gh-pages/TOOL_Stock_Portfolio_Tracker/dist_releases/Stock_Portfolio_Tracker_{new_version}_Portable.zip"

# 加入 recent_versions 頂部
recent = data.get("recent_versions", [])
recent.insert(0, {
    "version": new_version,
    "release_date": new_date,
    "patch_url": data["patch_url"],
    "changelog": new_changelog
})
data["recent_versions"] = recent[:5]

with open(version_file, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)

print(f"Updated version.json to {new_version} successfully!")

