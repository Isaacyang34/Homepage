# Stock Portfolio Tracker - 外掛擴充腳本目錄 (Plugins Directory)

此目錄支援自訂 Python 外掛腳本。任何放置在此目錄的 `.py` 檔案，在主程式啟動時都會被自動掃描並動態載入！

## 運作特點：
1. **免重新編譯**：直接撰寫純 Python 腳本，隨存隨用。
2. **核心包隔離**：無需修改 `app_core.pkg` 或重新打包執行檔。
3. **擴充掛鉤**：可在外掛中定義 `register()` 函式，主程式會自動調用註冊。

## 範例腳本結構：
```python
def register():
    print("自訂擴充功能已成功啟用！")
```
