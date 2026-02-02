# LED Studio Pro - 代碼修改檢查清單

> 在修改 V1.1.html 時使用此清單，避免語法錯誤

## 📋 修改前檢查

- [ ] 備份原文件：`LED螢幕設計界面規劃V1.1.html` → `LED螢幕設計界面規劃V1.1.bak.html`
- [ ] 確認使用適當的代碼編輯器（VS Code 推薦）
- [ ] 啟用 HTML/JSX 語法檢查擴展

---

## 🔍 核心結構區域（勿動）

### 必須保持的 JSX 結構

#### 1. 狀態聲明區 (Line ~130-145)
```javascript
const [items, setItems] = useState([...])
const [selectedIds, setSelectedIds] = useState([])
// 所有 useState 宣告必須以分號結尾
```
✅ 檢查：每個 `useState` 後都有分號

---

#### 2. Items 迴圈 (Line ~452-489)
```jsx
<div className="space-y-2 pb-6">
    {items.map((item, idx) => (
        <div key={item.id} ...>
            {/* 元件內容 */}
        </div>
    ))}  // ⚠️ 必須有這個 )}
</div>
```
✅ 檢查：
- `items.map((item, idx) => (` 開頭
- `)}` 結尾配對正確
- 不能有多餘的 `</div>`

---

#### 3. Modal 彈窗 (Line ~494+)
每個彈窗結構必須完整：
```jsx
{showCodeExport && (
    <div className="fixed inset-0 ...">
        {/* 內容 */}
    </div>
)}
```
✅ 檢查：
- `&&` 條件完整
- `(` `)` 成對
- 最後一個 `)}` 不能漏

---

## ✏️ 常見修改場景

### 場景 1：修改初始元件內容
**位置**：Line 133-137
```javascript
const [items, setItems] = useState([
    { id: 1, type: 'text', x: 1, y: 4, color: '#ff0000', content: '88', size: 3, ... },
    // 修改這裡 ⬆️
]);
```
✅ 檢查表：
- [ ] 每行以逗號結尾（最後一行除外）
- [ ] 所有字符串用單引號 `'` 或雙引號 `"` 配對
- [ ] 顏色值以 `#` 開頭
- [ ] 大括號 `{}` 成對

---

### 場景 2：添加新的 UI 按鈕或功能
**位置**：Navigation Bar 或 Sidebar
```jsx
<button onClick={()=>setShowXXX(true)} className="...">
    <Icon name="xxx" />
</button>
```
✅ 檢查表：
- [ ] 對應的 State 已在上面宣告：`const [showXXX, setShowXXX] = useState(false)`
- [ ] Modal 彈窗已添加：`{showXXX && (...)}`
- [ ] className 中的引號配對
- [ ] 所有括號 `()` 成對

---

### 場景 3：修改元件編輯器面板
**位置**：Sidebar 右側面板 (Line ~460-485)
```jsx
<div className="space-y-2">
    {items.map((item, idx) => (
        <div key={item.id} ...>
            {/* 在這裡添加新的編輯控制元件 */}
        </div>
    ))}
</div>
```
✅ 檢查表：
- [ ] 新增的 JSX 元素完整閉合
- [ ] 不能破壞 `items.map()` 的結構
- [ ] 所有事件處理器：`onChange={e=>...}`
- [ ] 三元運算符：`condition ? trueValue : falseValue` 配對正確

---

### 場景 4：修改 CSS 樣式 (Tailwind 類)
**位置**：任何 className 屬性
```jsx
className={`基礎類 ${condition ? '條件類' : '其他類'}`}
```
✅ 檢查表：
- [ ] 模板字符串使用反引號 `` ` ``
- [ ] 動態類用 `${}` 包裹
- [ ] 字符串末尾有反引號 `` ` ``

---

## 🐛 常見錯誤模式

| 錯誤 | 原因 | 修復 |
|------|------|------|
| `)' expected` | 括號不配對 | 檢查所有 `()` `[]` `{}` |
| `Unexpected token }` | 多餘的括號/大括號 | 從後往前數，確保配對 |
| `Expression expected` | 語句不完整 | 檢查分號和逗號 |
| 物件不顯示 | 迴圈結構破損 | 檢查 `items.map()` 的 `)}` |

---

## 🔧 修改工作流

### 步驟 1：備份
```powershell
# 在 PowerShell 中執行
Copy-Item "LED螢幕設計界面規劃V1.1.html" "LED螢幕設計界面規劃V1.1.bak.html"
```

### 步驟 2：修改
- 在 VS Code 中打開文件
- 進行修改
- 定期按 `Ctrl+S` 保存

### 步驟 3：驗證
- 在 VS Code 中按 `Ctrl+Shift+M` 打開問題面板
- 確認沒有錯誤（紅色下波浪線）
- 在瀏覽器中打開測試功能

### 步驟 4：如果出錯
```powershell
# 恢復備份
Remove-Item "LED螢幕設計界面規劃V1.1.html"
Rename-Item "LED螢幕設計界面規劃V1.1.bak.html" "LED螢幕設計界面規劃V1.1.html"
```

---

## 📝 修改日誌（版本控制）

記錄每次修改，方便追蹤問題

| 日期 | 修改項目 | 版本 | 狀態 |
|------|---------|------|------|
| 2026-01-30 | 修復語法錯誤 | V1.1 | ✅ 成功 |
| | | | |

---

## 🚨 緊急恢復

如果文件完全損壞：

1. **使用備份恢復**
   ```powershell
   Copy-Item "LED螢幕設計界面規劃V1.1.bak.html" "LED螢幕設計界面規劃V1.1.html"
   ```

2. **使用 V1.0 恢復**
   ```powershell
   Copy-Item "LED螢幕設計界面規劃V1.0.html" "LED螢幕設計界面規劃V1.1.html"
   ```

3. **使用 Git（如果已初始化）**
   ```powershell
   git checkout LED螢幕設計界面規劃V1.1.html
   ```

---

## 📚 快速參考

### JSX 語法速查
```javascript
// ✅ 正確
<div className="class" onClick={()=>action()}>text</div>
{condition && <Component />}
{items.map((item) => <div key={item.id}>{item.name}</div>)}

// ❌ 錯誤
<div className="class" onClick={()=>action()}>text</div   // 漏掉 >
{items.map((item) => <div key={item.id}>{item.name}</div>)} // 漏掉 )}
{condition ? <A /> <B />}  // 三元運算符格式錯誤
```

### 括號配對檢查工具
在 VS Code 中：
- 按 `Ctrl+Shift+P` → 輸入 "Bracket Pair Colorizer"
- 或安裝擴展：Bracket Pair Colorizer 2

---

💡 **提示**：遇到問題時，先檢查括號配對，99% 的問題都在那裡！
