# 🚀 LED Studio Pro - 快速參考卡

## 版本控制命令

### 備份當前版本
```powershell
.\manage-versions.ps1 backup
```

### 列出所有備份
```powershell
.\manage-versions.ps1 list
```

### 還原到某個版本
```powershell
.\manage-versions.ps1 restore
# 選擇要還原的版本號
```

### 比較版本差異（VS Code）
```powershell
.\manage-versions.ps1 compare
```

### 清理舊備份（保留最近 5 個）
```powershell
.\manage-versions.ps1 clean
```

---

## 文件修改工作流

### ✅ 標準工作流程

1. **修改前**
   ```powershell
   # 創建備份
   .\manage-versions.ps1 backup
   ```

2. **編輯代碼**
   ```powershell
   # 用 VS Code 打開
   code "LED螢幕設計界面規劃V1.1.html"
   ```

3. **檢查語法**
   - 按 `Ctrl+Shift+M` 查看問題面板
   - 確認沒有紅色錯誤

4. **測試功能**
   - 在瀏覽器打開 HTML 文件
   - 按 `F5` 刷新測試修改

5. **如果出錯**
   ```powershell
   # 還原到之前的版本
   .\manage-versions.ps1 restore
   ```

---

## 代碼修改檢查清單

### 修改任何代碼前
- [ ] 已運行 `backup` 命令
- [ ] 已打開 MODIFY_CHECKLIST.md
- [ ] VS Code 開啟了 HTML/JSX 語法檢查

### 修改後
- [ ] 保存文件 (`Ctrl+S`)
- [ ] 問題面板無紅色錯誤
- [ ] 在瀏覽器刷新測試
- [ ] 功能正常運作

### 如果出現錯誤
1. 查看 MODIFY_CHECKLIST.md 的「常見錯誤模式」
2. 檢查括號配對（99% 的問題都在這）
3. 用 `manage-versions.ps1 compare` 查看改了什麼
4. 用 `manage-versions.ps1 restore` 還原

---

## 常見修改場景速查

### 場景 1：改預設元件內容
📍 位置: Line 133-137
✓ 檢查: 元件屬性是否完整，最後一行無逗號

### 場景 2：添加新 LED 模組規格
📍 位置: Line 120-127 (LED_SPECS)
✓ 檢查: 所有數值都是數字，物件完整

### 場景 3：修改 UI 樣式
📍 位置: 任何 className="..."
✓ 檢查: Tailwind 類名正確，引號配對

### 場景 4：添加新功能按鈕
📍 位置: Navbar 或 Sidebar
✓ 檢查: State 已宣告，Modal 已添加，onclick 正確

### 場景 5：編輯元件列表面板
📍 位置: Line 480-510
✓ 檢查: items.map() 迴圈完整，)} 配對正確

---

## 括號配對速查

### 必須配對的括號類型

| 開括號 | 閉括號 | 用途 | 檢查 |
|--------|--------|------|------|
| `{` | `}` | 代碼塊、對象 | 數量相等 |
| `(` | `)` | 函數、表達式 | 配對完整 |
| `[` | `]` | 數組、屬性 | 配對完整 |
| `` ` `` | `` ` `` | 模板字符串 | 配對完整 |

### 快速檢查方法
1. VS Code 中按 `Ctrl+H` 打開「查找和替換」
2. 查找: `{` / 替換: (留空)
3. 看「替換計數」 - 應該等於 `}` 的數量

### 或使用 Bracket Pair Colorizer
- 安裝擴展: `Bracket Pair Colorizer 2`
- 括號會自動配對著色

---

## 常見錯誤快速修復

| 錯誤信息 | 原因 | 修復方法 |
|---------|------|---------|
| `)' expected` | 括號不配對 | 檢查 `()` `[]` `{}` |
| `Unexpected token }` | 多餘的 `}` | 從後往前數 |
| 物件不顯示 | items.map() 破損 | 檢查 `)}` 是否完整 |
| 按鈕沒反應 | State 未宣告 | 檢查 useState |
| 樣式未應用 | Tailwind 類名錯 | 檢查大小寫 |

---

## VS Code 快捷鍵

| 快捷鍵 | 功能 |
|--------|------|
| `Ctrl+S` | 保存 |
| `Ctrl+Z` | 撤銷 |
| `Ctrl+Y` | 重做 |
| `Ctrl+Shift+M` | 打開問題面板 |
| `Ctrl+Shift+P` | 命令面板 |
| `Ctrl+H` | 查找和替換 |
| `Ctrl+/` | 註釋/取消註釋 |
| `Ctrl+D` | 選擇當前單詞 |
| `Ctrl+L` | 選擇當前行 |

---

## 緊急恢復

### 如果 HTML 文件完全損壞

**選項 1：還原最近備份**
```powershell
.\manage-versions.ps1 list
.\manage-versions.ps1 restore
```

**選項 2：使用 V1.0 恢復**
```powershell
Copy-Item "LED螢幕設計界面規劃V1.0.html" "LED螢幕設計界面規劃V1.1.html"
```

**選項 3：檢查備份文件夾**
```powershell
# 直接查看備份
explorer backups
```

---

## 有用的文件

- `MODIFY_CHECKLIST.md` - 詳細的修改檢查清單
- `DEVELOPMENT_GUIDE.md` - 完整的開發指南
- `LED螢幕設計界面規劃V1.1.html` - 主程式（已添加結構註解）
- `LED螢幕設計界面規劃V1.0.html` - 備份版本
- `backups/` - 自動備份目錄

---

## 提示

💡 **修改前永遠先 backup**
💡 **檢查括號配對是第一步**
💡 **經常用 VS Code 的問題面板檢查錯誤**
💡 **用 compare 命令查看具體改了什麼**
💡 **遇到問題就用 restore 還原**

---

**最後更新**: 2026-01-30
