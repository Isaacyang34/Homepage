import os

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

for fp in files:
    if not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        text = f.read()

    text = text.replace('if hasattr(sys.stdout, "reconfigure"):', 'if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):')
    text = text.replace('if hasattr(sys.stderr, "reconfigure"):', 'if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):')
    text = text.replace('sys.stdout.reconfigure(encoding="utf-8")', 'if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):\n    sys.stdout.reconfigure(encoding="utf-8")')
    text = text.replace('sys.stderr.reconfigure(encoding="utf-8")', 'if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):\n    sys.stderr.reconfigure(encoding="utf-8")')

    with open(fp, 'w', encoding='utf-8') as f:
        f.write(text)
    print(f"Secured reconfigure in {fp}")

