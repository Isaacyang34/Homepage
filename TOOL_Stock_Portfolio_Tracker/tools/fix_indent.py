import os

for path in ['TOOL_Stock_Portfolio_Tracker/modules/main_gui.py', 'TOOL_Stock_Portfolio_Tracker/main_gui.py']:
    with open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    fixed = []
    in_target = False
    for line in lines:
        if line.startswith('        def on_open_fundamental_analyzer(self, symbol=None, name=""):'):
            in_target = True
            fixed.append('    def on_open_fundamental_analyzer(self, symbol=None, name=""):\n')
        elif in_target and line.startswith('    def on_view_kline(self):'):
            in_target = False
            fixed.append(line)
        else:
            fixed.append(line)

    with open(path, 'w', encoding='utf-8') as f:
        f.writelines(fixed)
    print(f"Fixed {path}")

