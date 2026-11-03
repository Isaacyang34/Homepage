import os

# 1. Patch main_gui.py
for fp in ['TOOL_Stock_Portfolio_Tracker/modules/main_gui.py', 'TOOL_Stock_Portfolio_Tracker/main_gui.py']:
    if not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        text = f.read()
    old_call = 'FundamentalAnalysisDialog(self, symbol=symbol, name=name)'
    new_call = 'FundamentalAnalysisDialog(self, symbol=symbol, name=name, get_positions_callback=lambda: self.positions)'
    if old_call in text:
        text = text.replace(old_call, new_call)
        with open(fp, 'w', encoding='utf-8') as f:
            f.write(text)
        print(f"Patched {fp}")

# 2. Patch unified_ranking_gui.py
for fp in ['TOOL_Stock_Portfolio_Tracker/modules/unified_ranking_gui.py', 'TOOL_Stock_Portfolio_Tracker/unified_ranking_gui.py']:
    if not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        text = f.read()
    old_call = 'dialog = FundamentalAnalysisDialog(self.winfo_toplevel(), default_symbol=symbol)'
    new_call = 'dialog = FundamentalAnalysisDialog(self.winfo_toplevel(), default_symbol=symbol, get_positions_callback=self.get_positions_callback)'
    if old_call in text:
        text = text.replace(old_call, new_call)
        with open(fp, 'w', encoding='utf-8') as f:
            f.write(text)
        print(f"Patched {fp}")

