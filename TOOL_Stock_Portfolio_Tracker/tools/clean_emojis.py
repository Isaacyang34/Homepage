import os

replacements = {
    '🌐': '[環境]',
    '🌊': '[河流]',
    '🔍': '[分析]',
    '🛡️': '[安全]',
    '🛡': '[安全]',
    '📈': '[趨勢]',
    '⚖️': '[權重]',
    '⚖': '[權重]',
    '💡': '[提示]',
    '📊': '[統計]',
    '🏆': '[排名]',
    '🚀': '>>',
    '🛑': '■',
    '☁️': '[雲]',
    '☁': '[雲]',
    '🔄': '[->]',
    '📸': '[*]',
    '🤖': '[AI]',
    '⚠️': '[!]',
    '⚠': '[!]'
}

files_to_clean = [
    'TOOL_Stock_Portfolio_Tracker/modules/unified_ranking_gui.py',
    'TOOL_Stock_Portfolio_Tracker/unified_ranking_gui.py',
    'TOOL_Stock_Portfolio_Tracker/modules/fundamental_analyzer_gui.py',
    'TOOL_Stock_Portfolio_Tracker/fundamental_analyzer_gui.py' if os.path.exists('TOOL_Stock_Portfolio_Tracker/fundamental_analyzer_gui.py') else None
]

for fp in files_to_clean:
    if not fp or not os.path.exists(fp):
        continue
    with open(fp, 'r', encoding='utf-8') as f:
        text = f.read()

    for k, v in replacements.items():
        text = text.replace(k, v)

    # Check for any remaining characters > 0xFFFF
    cleaned = []
    purged_count = 0
    for char in text:
        if ord(char) > 0xFFFF:
            cleaned.append('')
            purged_count += 1
        else:
            cleaned.append(char)
    text = "".join(cleaned)

    with open(fp, 'w', encoding='utf-8') as f:
        f.write(text)
    print(f"Cleaned {fp}, purged {purged_count} remaining non-BMP characters.")

print("Rule 9 Emoji Cleansing completed!")

