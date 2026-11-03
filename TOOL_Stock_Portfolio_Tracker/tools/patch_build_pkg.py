import os

p = 'TOOL_Stock_Portfolio_Tracker/build_pkg.py'
with open(p, 'rb') as f:
    b = f.read()

target = b'"stock_fundamental_service.py"'
replacement = b'"stock_fundamental_service.py",\r\n    "fundamental_analyzer_gui.py"' if b'\r\n' in b else b'"stock_fundamental_service.py",\n    "fundamental_analyzer_gui.py"'

if target in b:
    b = b.replace(target, replacement)
    with open(p, 'wb') as f:
        f.write(b)
    print("build_pkg.py patched successfully")
else:
    print("target not found")

