// ═══════════════════════════════════════════════════════════
//  🎨 全互動 UI 介面自訂與滑鼠拖曳調整編輯器 (UI Layout Customizer)
//  【支援滑鼠直接拖曳擺放、微調數值滑桿、個人化 Layout 儲存至 localStorage】
// ═══════════════════════════════════════════════════════════

(function initUIEditor() {
  const LAYOUT_KEY = 'MYPIXELGAME_CUSTOM_UI_LAYOUT_V1';
  let isEditing = false;
  let activeDragTarget = null;
  let dragOffsetX = 0, dragOffsetY = 0;

  // 可調整位置的 UI 元件定義清單
  const EDITABLE_ELEMENTS = [
    { id: 'hud-bars',       name: '左下氣血/靈力條', defaultPos: { left: 18, bottom: 18, width: 200 } },
    { id: 'hud-hotbar',     name: '中央道具快捷欄', defaultPos: { left: 420, bottom: 18 } },
    { id: 'game-log-panel', name: '右下訊息紀錄',   defaultPos: { right: 64, bottom: 18, width: 250, height: 190 } },
    { id: 'hud-sys',        name: '右下系統按鈕',   defaultPos: { right: 18, bottom: 18 } },
    { id: 'hud-minimap',    name: '右上羅盤小地圖', defaultPos: { right: 18, top: 18 } },
  ];

  // 1. 讀取個人化 Layout 設定
  function loadLayout() {
    try {
      const saved = localStorage.getItem(LAYOUT_KEY);
      if (!saved) return;
      const layout = JSON.parse(saved);
      Object.keys(layout).forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;
        const pos = layout[id];
        if (pos.left !== undefined) el.style.left = pos.left + 'px';
        if (pos.right !== undefined) el.style.right = pos.right + 'px';
        if (pos.top !== undefined) el.style.top = pos.top + 'px';
        if (pos.bottom !== undefined) el.style.bottom = pos.bottom + 'px';
        if (pos.width !== undefined) el.style.width = pos.width + 'px';
        if (pos.height !== undefined) el.style.height = pos.height + 'px';
        if (pos.transform !== undefined) el.style.transform = pos.transform;
      });
    } catch(e) {}
  }

  // 2. 儲存個人化 Layout 設定
  function saveLayout() {
    try {
      const layout = {};
      EDITABLE_ELEMENTS.forEach(item => {
        const el = document.getElementById(item.id);
        if (!el) return;
        const rect = el.getBoundingClientRect();
        const vpW = window.innerWidth;
        const vpH = window.innerHeight;

        // 計算相對於視窗邊界的像素距離
        layout[item.id] = {
          left: Math.round(rect.left),
          bottom: Math.round(vpH - rect.bottom),
          width: Math.round(rect.width),
          height: Math.round(rect.height),
          transform: 'none'
        };
      });
      localStorage.setItem(LAYOUT_KEY, JSON.stringify(layout));
      if (window.notify) window.notify('💾 介面佈局已成功儲存！');
    } catch(e) {}
  }

  // 3. 一鍵重置為預設 Layout
  function resetLayout() {
    try {
      localStorage.removeItem(LAYOUT_KEY);
      EDITABLE_ELEMENTS.forEach(item => {
        const el = document.getElementById(item.id);
        if (!el) return;
        el.style.cssText = '';
      });
      if (typeof applyUIRelayout === 'function') applyUIRelayout();
      if (window.notify) window.notify('🔄 介面佈局已重置為預設！');
      refreshEditorControls();
    } catch(e) {}
  }

  // 4. 開啟 / 關閉 拖曳編輯模式
  function toggleEditMode() {
    isEditing = !isEditing;
    const panel = document.getElementById('ui-editor-panel');
    if (panel) panel.style.display = isEditing ? 'flex' : 'none';

    EDITABLE_ELEMENTS.forEach(item => {
      const el = document.getElementById(item.id);
      if (!el) return;
      if (isEditing) {
        el.classList.add('ui-editable-target');
        el.setAttribute('draggable', 'false');
      } else {
        el.classList.remove('ui-editable-target');
      }
    });

    if (isEditing && window.notify) {
      window.notify('🎨 進入 UI 編輯模式：請直接用滑鼠拖曳各元件，調至滿意後按【儲存】！');
    }
  }

  // 5. 注入 CSS 樣式與編輯器 Floating Window
  function injectEditorUI() {
    const style = document.createElement('style');
    style.textContent = `
      .ui-editable-target {
        outline: 2px dashed #ffd700 !important;
        outline-offset: 4px;
        cursor: move !important;
        box-shadow: 0 0 14px rgba(255, 215, 0, 0.5) !important;
      }
      .ui-editable-target * {
        pointer-events: none !important;
      }
      #ui-editor-panel {
        position: fixed;
        top: 20px;
        left: 50%;
        transform: translateX(-50%);
        background: rgba(6, 9, 22, 0.95);
        border: 2px solid var(--gold);
        border-radius: 10px;
        padding: 10px 18px;
        display: none;
        align-items: center;
        gap: 12px;
        z-index: 9999;
        box-shadow: 0 0 24px rgba(0, 0, 0, 0.85);
        backdrop-filter: blur(8px);
        font-family: 'Noto Serif TC', serif;
      }
      #ui-editor-panel .ed-title {
        color: var(--gold);
        font-size: 13px;
        font-weight: 700;
        letter-spacing: 1px;
      }
      #ui-editor-panel button {
        padding: 5px 14px;
        border-radius: 5px;
        font-size: 11px;
        font-weight: 700;
        cursor: pointer;
        border: 1px solid var(--border);
        transition: all 0.2s;
      }
      .btn-ed-save { background: #15803d; color: #fff; border-color: #22c55e !important; }
      .btn-ed-save:hover { background: #166534; }
      .btn-ed-reset { background: #b91c1c; color: #fff; border-color: #ef4444 !important; }
      .btn-ed-reset:hover { background: #991b1b; }
      .btn-ed-close { background: #334155; color: #d8dfe8; }
      .btn-ed-close:hover { background: #475569; }

      /* 按鈕專屬觸發入口 */
      #btn-ui-adjust {
        background: linear-gradient(135deg, #1e293b, #0f172a);
        border: 1.5px solid var(--gold);
        color: var(--gold);
        font-size: 11px;
        font-weight: 700;
        padding: 4px 10px;
        border-radius: 12px;
        cursor: pointer;
        transition: all 0.2s;
      }
      #btn-ui-adjust:hover {
        background: var(--gold);
        color: #000;
        box-shadow: 0 0 10px var(--gold-d);
      }
    `;
    document.head.appendChild(style);

    // 編輯工具面板 DOM
    const edBox = document.createElement('div');
    edBox.id = 'ui-editor-panel';
    edBox.innerHTML = `
      <span class="ed-title">🎨 UI 自由拖曳編輯模式</span>
      <button class="btn-ed-save" id="ed-btn-save">💾 儲存佈局</button>
      <button class="btn-ed-reset" id="ed-btn-reset">🔄 恢復預設</button>
      <button class="btn-ed-close" id="ed-btn-close">✕ 退出編輯</button>
    `;
    document.body.appendChild(edBox);

    document.getElementById('ed-btn-save').onclick = () => { saveLayout(); toggleEditMode(); };
    document.getElementById('ed-btn-reset').onclick = () => { resetLayout(); };
    document.getElementById('ed-btn-close').onclick = () => { toggleEditMode(); };

    // 在右下角系統按鈕列附近注入 "🎨 調整UI" 觸發按鈕
    const sysHud = document.getElementById('hud-stones');
    if (sysHud && !document.getElementById('btn-ui-adjust')) {
      const adjBtn = document.createElement('button');
      adjBtn.id = 'btn-ui-adjust';
      adjBtn.innerHTML = '🎨 調整UI';
      adjBtn.title = '開啟自由滑鼠拖曳與介面排版調整編輯器';
      adjBtn.onclick = toggleEditMode;
      sysHud.appendChild(adjBtn);
    }
  }

  // 6. 滑鼠拖曳事件處理 (Mouse Drag Event Handlers)
  function initDragListeners() {
    window.addEventListener('mousedown', e => {
      if (!isEditing) return;
      const target = e.target.closest('.ui-editable-target');
      if (!target) return;
      activeDragTarget = target;
      const rect = target.getBoundingClientRect();
      dragOffsetX = e.clientX - rect.left;
      dragOffsetY = e.clientY - rect.top;
      e.preventDefault();
    });

    window.addEventListener('mousemove', e => {
      if (!isEditing || !activeDragTarget) return;
      const x = e.clientX - dragOffsetX;
      const y = e.clientY - dragOffsetY;

      activeDragTarget.style.position = 'fixed';
      activeDragTarget.style.left = Math.max(0, Math.min(window.innerWidth - activeDragTarget.offsetWidth, x)) + 'px';
      activeDragTarget.style.top = Math.max(0, Math.min(window.innerHeight - activeDragTarget.offsetHeight, y)) + 'px';
      activeDragTarget.style.bottom = 'auto';
      activeDragTarget.style.right = 'auto';
      activeDragTarget.style.transform = 'none';
      e.preventDefault();
    });

    window.addEventListener('mouseup', () => {
      activeDragTarget = null;
    });
  }

  // 7. 初始化與自動讀取
  function bootUIEditor() {
    injectEditorUI();
    initDragListeners();
    setTimeout(loadLayout, 100);
  }

  if (document.readyState === 'loading') {
    window.addEventListener('DOMContentLoaded', bootUIEditor);
  } else {
    bootUIEditor();
  }

  // 掛載全域 API
  window.toggleUIEditMode = toggleEditMode;
  window.saveUILayout = saveLayout;
  window.resetUILayout = resetLayout;
})();
