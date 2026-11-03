import os

files = [
    'TOOL_Stock_Portfolio_Tracker/build_pkg.py',
    'TOOL_Stock_Portfolio_Tracker/main.py',
    'TOOL_Stock_Portfolio_Tracker/fundamental_analyzer_gui.py',
    'TOOL_Stock_Portfolio_Tracker/modules/fundamental_analyzer_gui.py',
    'TOOL_Stock_Portfolio_Tracker/package_release.py',
    'TOOL_Stock_Portfolio_Tracker/ui_layout_inspector.py',
    'TOOL_Stock_Portfolio_Tracker/modules/ui_layout_inspector.py',
    'TOOL_Stock_Portfolio_Tracker/tests/test_desktop_gui.py',
    'TOOL_Stock_Portfolio_Tracker/tests/capture_real_screenshots.py',
    'TOOL_Stock_Portfolio_Tracker/tests/generate_screenshots.py'
]

clean_reconfigure = """if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")"""

for fp in files:
    if not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        lines = f.readlines()
    
    # Filter out any lines that mention sys.stdout, sys.stderr, reconfigure
    new_lines = []
    reconfigure_inserted = False
    for line in lines:
        if 'reconfigure' in line or 'if sys.stdout' in line or 'if sys.stderr' in line:
            if not reconfigure_inserted:
                new_lines.append(clean_reconfigure + '\n')
                reconfigure_inserted = True
        else:
            new_lines.append(line)

    with open(fp, 'w', encoding='utf-8') as f:
        f.writelines(new_lines)
    print(f"Fixed {fp}")

