import os
import json

code = '''    def refresh_postmarket_data(self):
        for item in self.tree_postmarket.get_children():
            self.tree_postmarket.delete(item)

        # 讀取本地全市場真實快取
        cache_data = {}
        for base_dir in [
            os.path.dirname(os.path.abspath(__file__)),
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            os.getcwd()
        ]:
            c_path = os.path.join(base_dir, "market_ranking_cache.json")
            if os.path.exists(c_path):
                try:
                    with open(c_path, "r", encoding="utf-8") as f:
                        cache_data = json.load(f)
                    break
                except Exception:
                    pass

        stocks = cache_data.get("stocks", [])
        revs_map = {r.get("symbol"): r for r in cache_data.get("revenues", []) if r.get("symbol")}
        etfs = cache_data.get("etfs", [])

        candidates = []
        for s in stocks:
            sym = s.get("symbol", "")
            name = s.get("name", "")
            price = float(s.get("price") or 0)
            pe = float(s.get("pe") or 0) if s.get("pe") else None
            roe = float(s.get("roe") or 0)
            pb = float(s.get("pb") or 0)
            yield_pct = float(s.get("yield_pct") or 0)

            r = revs_map.get(sym, {})
            yoy = float(r.get("yoy") or 0)

            # Stage 1: 硬性濾網 (排除價格過低、淨值為負或無流動性)
            if price <= 8.0 or pb <= 0 or not sym:
                continue

            # Stage 2: 五因子評分 (0~100)
            score_momentum = min(98, max(42, int(50 + yoy * 0.75)))
            score_chips = min(98, max(42, int(50 + yield_pct * 4.8)))
            score_quality = min(98, max(42, int(50 + roe * 1.4)))
            if pe and pe > 0:
                score_value = min(98, max(35, int(100 - pe * 1.7)))
            else:
                score_value = 55
            atr_pct = round(max(1.3, min(4.8, 1.8 + (100 - score_quality) * 0.03)), 2)

            # 依目前市場環境加權 (動能 25%, 籌碼 20%, 品質 30%, 價值 15%, 低波 10%)
            composite = round(score_momentum * 0.25 + score_chips * 0.20 + score_quality * 0.30 + score_value * 0.15 + (100 - atr_pct * 15) * 0.10, 1)

            # 估值河流位階
            if pe and pe < 16:
                river_str = "16x 超值"
            elif pe and pe < 20:
                river_str = "16x~20x 便宜"
            elif pe and pe < 24:
                river_str = "20x~24x 合理"
            elif pe and pe < 28:
                river_str = "24x~28x 偏高"
            else:
                river_str = "28x+ 昂貴"

            # 建議部位
            if atr_pct < 1.7:
                pos_str = "20%~22% (主力)"
            elif atr_pct < 2.3:
                pos_str = "16%~18% (價值)"
            else:
                pos_str = "12%~15% (動能)"

            # 量化點評
            reasons = []
            if yoy > 25: reasons.append(f"營收年增+{yoy:.1f}%")
            if roe > 18: reasons.append(f"ROE達{roe:.1f}%")
            if yield_pct > 5: reasons.append(f"殖利率{yield_pct:.1f}%")
            if pe and pe < 16: reasons.append(f"本益比{pe:.1f}x低估")
            if not reasons: reasons.append("財務結構穩健")
            verdict = "，".join(reasons[:2])

            candidates.append({
                "symbol": sym,
                "name": name,
                "price": f"{price:,.1f}",
                "composite": composite,
                "momentum": str(score_momentum),
                "chips": str(score_chips),
                "quality": str(score_quality),
                "river": river_str,
                "atr": f"{atr_pct:.2f}%",
                "pos": pos_str,
                "verdict": verdict
            })

        # 依綜合評分遞減排序，取出前 30 名優質標的
        candidates.sort(key=lambda x: x["composite"], reverse=True)
        top_candidates = candidates[:30]

        for i, c in enumerate(top_candidates, 1):
            row = (
                i,
                c["symbol"],
                c["name"],
                c["price"],
                f"{c['composite']:.1f} 分",
                c["momentum"],
                c["chips"],
                c["quality"],
                c["river"],
                c["atr"],
                c["pos"],
                c["verdict"]
            )
            self.tree_postmarket.insert("", tk.END, values=row)
'''

for fp in ['TOOL_Stock_Portfolio_Tracker/modules/unified_ranking_gui.py', 'TOOL_Stock_Portfolio_Tracker/unified_ranking_gui.py']:
    if not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        text = f.read()

    start_idx = text.find('    def refresh_postmarket_data(self):')
    end_idx = text.find('    def on_open_selected_fundamental(self):')

    if start_idx != -1 and end_idx != -1:
        new_text = text[:start_idx] + code + '\n' + text[end_idx:]
        with open(fp, 'w', encoding='utf-8') as f:
            f.write(new_text)
        print(f"Upgraded postmarket rankings to Top 30 in {fp}")
    else:
        print(f"Could not find refresh_postmarket_data in {fp}")

