import os
import re

files = [
    'TOOL_Stock_Portfolio_Tracker/main.py',
    'TOOL_Stock_Portfolio_Tracker/build_pkg.py',
    'TOOL_Stock_Portfolio_Tracker/package_release.py',
    'TOOL_Stock_Portfolio_Tracker/ui_layout_inspector.py',
    'TOOL_Stock_Portfolio_Tracker/modules/ui_layout_inspector.py',
    'TOOL_Stock_Portfolio_Tracker/fundamental_analyzer_gui.py',
    'TOOL_Stock_Portfolio_Tracker/modules/fundamental_analyzer_gui.py',
    'TOOL_Stock_Portfolio_Tracker/stock_fundamental_service.py',
    'TOOL_Stock_Portfolio_Tracker/modules/stock_fundamental_service.py',
    'TOOL_Stock_Portfolio_Tracker/tests/test_desktop_gui.py',
    'TOOL_Stock_Portfolio_Tracker/tests/capture_real_screenshots.py',
    'TOOL_Stock_Portfolio_Tracker/tests/generate_screenshots.py'
]

clean_block = '''if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")'''

pattern = re.compile(r'(?:if\s+sys\.stdout[^\n]+\n|\s*sys\.stdout\.reconfigure[^\n]+\n|if\s+sys\.stderr[^\n]+\n|\s*sys\.stderr\.reconfigure[^\n]+\n|if\s+hasattr\([^\n]+\n)+', re.MULTILINE)

for fp in files:
    if not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        text = f.read()

    # Replace any messy reconfigure block with clean_block
    if 'reconfigure' in text:
        text = pattern.sub(clean_block + '\n', text)
        with open(fp, 'w', encoding='utf-8') as f:
            f.write(text)
        print(f"Cleaned {fp}")

print("Syntax cleanup completed.")

