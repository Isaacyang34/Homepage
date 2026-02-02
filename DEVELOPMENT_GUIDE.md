# LED Studio Pro - 開發指南

## 概述

LED Studio Pro 是一個基於 React + Tailwind CSS 的 LED 顯示屏設計工具。整個應用打包在單一 HTML 文件中。

---

## 文件結構

```
homepage/
├── LED螢幕設計界面規劃V1.1.html    ← 主程式（當前版本）
├── LED螢幕設計界面規劃V1.0.html    ← 備份版本
├── LED螢幕設計界面規劃V1.1.bak.html ← 自動備份
├── MODIFY_CHECKLIST.md              ← 修改檢查清單
└── DEVELOPMENT_GUIDE.md             ← 本文件
```

---

## 快速開始

### 1. 打開應用
在瀏覽器中打開：`LED螢幕設計界面規劃V1.1.html`

### 2. 編輯代碼
使用 VS Code 打開文件：
```powershell
code "LED螢幕設計界面規劃V1.1.html"
```

### 3. 測試修改
- 修改後保存（Ctrl+S）
- 在瀏覽器中刷新（F5）
- 檢查功能是否正常

---

## 代碼架構

### 核心組件堆疊

```
App (主組件)
├── State Management（狀態管理）
├── Canvas Rendering（畫布渲染）
├── Sidebar Panel（側邊欄）
└── Modal Dialogs（彈窗）
```

### 文件大致分段

| 行號範圍 | 內容 | 用途 |
|---------|------|------|
| 1-100 | HTML/CSS 框架 | 基礎結構和全局樣式 |
| 100-120 | 外部庫載入 | React、Babel、Tailwind |
| 120-200 | Icon 組件 + LED_SPECS | 圖標系統和規格定義 |
| 200-300 | App 組件初始化 | 狀態宣告和 Refs |
| 300-400 | 計算邏輯 | Stats、Code generation |
| 400-500 | 事件處理 | Mouse/Keyboard 交互 |
| 500-550 | 主 UI 布局 | Navigation + Canvas + Sidebar |
| 550-593 | Modal 彈窗 | Code Export、Power Config、BOM |

---

## 常用修改指南

### 修改 1：添加新的 LED 模組規格

**位置**：Line ~115-125（LED_SPECS 對象）

```javascript
const LED_SPECS = {
    'P2':    { pitch: 2.0,  w: 256, h: 128, pxW: 128, pxH: 64,  avgW: 650 },
    // 添加新的模組 ⬇️
    'P3.5':  { pitch: 3.5,  w: 224, h: 112, pxW: 64,  pxH: 32,  avgW: 580 },
};
```

**檢查清單**：
- [ ] 所有數值都是正數
- [ ] `pxW` 和 `pxH` 應該是 2 的倍數
- [ ] `avgW` 是平均功率/m² (瓦特)

---

### 修改 2：更改初始顯示元件

**位置**：Line ~133-137

```javascript
const [items, setItems] = useState([
    { 
        id: 1, 
        type: 'text',          // 'text' 或 'line'
        x: 1,                  // X 坐標
        y: 4,                  // Y 坐標
        color: '#ff0000',      // 顏色（十六進制）
        content: '88',         // 文字內容（僅 type=text）
        size: 3,               // 字體大小（僅 type=text）
        animation: 'none',     // 'none'|'blink'|'breath'|'scroll'
        isCollapsed: false,    // UI 展開狀態
        isReversed: false      // 反白顯示
    },
]);
```

**檢查清單**：
- [ ] `id` 必須唯一
- [ ] `x`, `y` 為像素坐標（0-256 之間）
- [ ] `color` 格式正確 (#RRGGBB)
- [ ] 最後一個元素後面 **不要** 加逗號

---

### 修改 3：添加新的 State 變數

**位置**：Line ~120-145（所有 useState 宣告區）

```javascript
// 🔴 添加新 state
const [myNewState, setMyNewState] = useState(defaultValue);

// 🔴 在 stats useMemo 的依賴中添加
const stats = useMemo(() => {
    // ... 計算邏輯
}, [modelKey, config, voltageAC, voltageDC, myNewState]); // ← 添加這裡
```

**檢查清單**：
- [ ] State 名稱使用 camelCase
- [ ] 默認值類型正確（number、string、boolean、array、object）
- [ ] 在 useMemo 依賴中添加（如果影響計算）
- [ ] Setter 函數命名為 `setXXX`

---

### 修改 4：在 Sidebar 添加新的編輯控制

**位置**：Line ~460-485（Item 編輯面板）

```jsx
{/* 新增一個顏色選擇器 */}
<div className="flex items-center gap-2 bg-slate-900/50 p-1 rounded-lg border border-white/5">
    <div title="背景色" className="text-slate-500 pl-1">
        <Icon name="palette" className="w-3.5 h-3.5" />
    </div>
    <input 
        type="color" 
        value={item.backgroundColor || '#000000'}
        onChange={e => updateItem(item.id, 'backgroundColor', e.target.value)}
        className="w-6 h-6 cursor-pointer"
    />
</div>
```

**檢查清單**：
- [ ] 用 `updateItem(id, field, value)` 更新狀態
- [ ] 事件處理：`onChange={e => updateItem(...)}`
- [ ] 使用正確的 Icon 名稱（參考 Icon 組件定義）
- [ ] Tailwind 類名正確

---

### 修改 5：改進 Modal 彈窗

**位置**：Line ~494+ (Code Export Modal 等)

```jsx
{showMyModal && (
    <div className="fixed inset-0 bg-black/90 flex items-center justify-center z-[250] backdrop-blur-md">
        <div className="bg-slate-900 border border-white/10 w-full max-w-md p-8 rounded-2xl shadow-2xl">
            {/* 彈窗內容 */}
        </div>
    </div>
)}
```

**檢查清單**：
- [ ] `showMyModal` State 已宣告
- [ ] `&&` 條件正確
- [ ] `z-[250]` 確保在最上層
- [ ] 關閉按鈕：`onClick={()=>setShowMyModal(false)}`

---

## 調試技巧

### 1. 使用瀏覽器控制台

按 `F12` 打開開發者工具，進入 Console 標籤：

```javascript
// 查看所有元件
console.log(items);

// 快速修改狀態（實驗用，刷新後重置）
document.querySelector('input[value="88"]').value = "99";
```

### 2. VS Code 語法檢查

安裝推薦擴展：
- **ES7+ React/Redux/React-Native snippets** (dsznajder.es7-react-js-snippets)
- **Prettier - Code formatter** (esbenp.prettier-vscode)
- **Error Lens** (usernamehw.errorlens)

### 3. 在代碼中添加調試註解

```javascript
// TODO: 優化這裡的性能
// FIXME: 需要修復 Z-index 問題
// HACK: 臨時解決方案，待改進
```

---

## 性能優化建議

### 1. 批量操作時禁用動畫

```javascript
// 不要頻繁更新 items，改用批量更新
const batchUpdate = (updates) => {
    setItems(prev => prev.map(item => {
        const update = updates.find(u => u.id === item.id);
        return update ? { ...item, ...update } : item;
    }));
};
```

### 2. 避免頻繁的 useMemo 計算

```javascript
// 只在必要時依賴 stats
const stats = useMemo(() => {
    // 只添加必要的依賴
}, [modelKey, config]); // 不要加不必要的依賴
```

---

## 常見問題排查

### Q: 修改後物件不顯示
- [ ] 檢查 `items` 初始化
- [ ] 檢查 `items.map()` 迴圈是否完整
- [ ] 檢查 `spec` 是否為 undefined

### Q: 點擊按鈕沒反應
- [ ] State 是否正確宣告
- [ ] onClick 事件是否綁定
- [ ] Modal 的 `&&` 條件是否正確

### Q: CSS 樣式沒有應用
- [ ] className 中的 Tailwind 類名是否正確
- [ ] 大小寫是否正確（Tailwind 大小寫敏感）
- [ ] 是否使用了不支持的 Tailwind 版本

### Q: 代碼改了但瀏覽器沒更新
- [ ] 按 `Ctrl+Shift+R` 強制刷新
- [ ] 清除瀏覽器快取
- [ ] 關閉瀏覽器重新打開

---

## 最佳實踐

### ✅ 應該做
- 每次修改前備份
- 使用有意義的變數名
- 為複雜邏輯添加註解
- 頻繁測試修改效果
- 記錄修改內容

### ❌ 不要做
- 同時修改多個地方
- 改變 JSX 結構的基本骨架
- 刪除未確認是否使用的代碼
- 未測試就提交修改
- 忽視 VS Code 的錯誤提示

---

## 聯繫方式

遇到問題時：
1. 查看 MODIFY_CHECKLIST.md 的常見錯誤模式
2. 檢查括號配對
3. 使用 Git 回退到上一個版本
4. 從 V1.0 恢復並重新做修改

---

## 版本歷史

| 版本 | 日期 | 主要改進 |
|------|------|---------|
| V1.0 | 2026-01-30 | 初版，基本功能完成 |
| V1.1 | 2026-01-30 | 修復語法錯誤，添加防護機制 |

---

**最後更新**：2026-01-30
