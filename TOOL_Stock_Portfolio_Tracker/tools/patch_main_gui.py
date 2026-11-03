import os
import sys

def patch_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. Add fundamental button in bottom_frame
    target_btn = 'btn_view_kline.pack(side=tk.LEFT, padx=2)'
    new_btn = ('btn_view_kline.pack(side=tk.LEFT, padx=2)\n\n'
               '        btn_fundamental = ttk.Button(bottom_frame, text="[基本面] 價值河流圖", command=self.on_open_fundamental_analyzer, style="Action.TButton")\n'
               '        btn_fundamental.pack(side=tk.LEFT, padx=2)')

    if target_btn in content and 'btn_fundamental' not in content:
        content = content.replace(target_btn, new_btn, 1)
        print(f"Added btn_fundamental to {filepath}")

    # 2. Add on_open_fundamental_analyzer method
    target_method = 'def on_view_kline(self):'
    new_method = '''    def on_open_fundamental_analyzer(self, symbol=None, name=""):
        """開啟個股基本面深度分析與價值河流圖視窗"""
        if not symbol:
            selected = self.tree.selection()
            if selected:
                try:
                    pos_id = int(selected[0])
                    pos = next((p for p in self.positions if p["id"] == pos_id), None)
                    if pos:
                        symbol = pos.get("symbol", "")
                        name = pos.get("name", "")
                except Exception:
                    pass
            if not symbol:
                symbol = "2330.TW"
                name = "台積電"
        try:
            from fundamental_analyzer_gui import FundamentalAnalysisDialog
            FundamentalAnalysisDialog(self, symbol=symbol, name=name)
        except Exception as e:
            messagebox.showerror("錯誤", f"開啟基本面分析視窗失敗: {e}", parent=self)

    def on_view_kline(self):'''

    if target_method in content and 'def on_open_fundamental_analyzer' not in content:
        content = content.replace(target_method, new_method, 1)
        print(f"Added on_open_fundamental_analyzer to {filepath}")

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

for path in ['TOOL_Stock_Portfolio_Tracker/modules/main_gui.py', 'TOOL_Stock_Portfolio_Tracker/main_gui.py']:
    if os.path.exists(path):
        patch_file(path)

print("Patching main_gui completed successfully!")

