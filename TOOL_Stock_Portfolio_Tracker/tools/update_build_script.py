# -*- coding: utf-8 -*-
with open("TOOL_Stock_Portfolio_Tracker/build_pkg.py", "r", encoding="utf-8") as f:
    content = f.read()

if "statementdog_service.py" not in content:
    content = content.replace(
        '"unified_ranking_gui.py"',
        '"unified_ranking_gui.py",\n    "statementdog_service.py"'
    )
    with open("TOOL_Stock_Portfolio_Tracker/build_pkg.py", "w", encoding="utf-8") as f:
        f.write(content)
    print("build_pkg.py updated successfully!")
else:
    print("Already updated.")

