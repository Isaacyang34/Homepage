import os

for path in ['TOOL_Stock_Portfolio_Tracker/modules/fundamental_analyzer_gui.py', 'TOOL_Stock_Portfolio_Tracker/fundamental_analyzer_gui.py']:
    if not os.path.exists(path):
        continue
    with open(path, 'r', encoding='utf-8') as f:
        c = f.read()
    c = c.replace('def __init__(self, parent, default_symbol: str = "2330.TW"):',
                  'def __init__(self, parent, default_symbol: str = "2330.TW", symbol: str = None, name: str = ""):\n        if symbol:\n            default_symbol = symbol')
    c = c.replace('self.geometry("1100, 780")', 'self.geometry("1100x780")')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(c)
    print(f"Patched {path}")

