# -*- coding: utf-8 -*-
"""
TOOL_Stock_Portfolio_Tracker - Desktop Tkinter GUI Automated Verification Test
Tests V1.2.0.0 Desktop Features:
  1. Main GUI bottom bar button '[基本面] 價值河流圖' & dialog launch.
  2. UnifiedRankingFrame postmarket subtab: Market Regime card, 5-stage funnel quant screener, ATR sizing.
  3. FundamentalAnalysisDialog: P/E River Bands Canvas, Revenue YoY & Margin chart, DuPont card, safety indicators.
Captures real desktop window screenshots into reports/screenshots/.
"""
import os
import sys
import time
import ctypes
from ctypes import wintypes
import tkinter as tk
from tkinter import ttk
from PIL import Image

if sys.stdout is not None and hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if sys.stderr is not None and hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

# Configure path
BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODULES_DIR = os.path.join(BASE_DIR, "modules")
if MODULES_DIR not in sys.path:
    sys.path.insert(0, MODULES_DIR)
if BASE_DIR not in sys.path:
    sys.path.insert(0, BASE_DIR)

SCREENSHOT_DIR = os.path.join(BASE_DIR, "reports", "screenshots")
os.makedirs(SCREENSHOT_DIR, exist_ok=True)


def capture_window(win, filename: str) -> str:
    """Capture a Tkinter window using Win32 PrintWindow API and save to PNG."""
    win.deiconify()
    win.lift()
    win.update_idletasks()
    win.update()
    time.sleep(0.4)

    user32 = ctypes.windll.user32
    gdi32 = ctypes.windll.gdi32

    inner_hwnd = win.winfo_id()
    hwnd = user32.GetAncestor(inner_hwnd, 2)  # GA_ROOT
    if not hwnd:
        hwnd = inner_hwnd

    rect = wintypes.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    w = max(rect.right - rect.left, 600)
    h = max(rect.bottom - rect.top, 400)

    hdc_win = user32.GetWindowDC(hwnd)
    hdc_mem = gdi32.CreateCompatibleDC(hdc_win)
    hbitmap = gdi32.CreateCompatibleBitmap(hdc_win, w, h)
    gdi32.SelectObject(hdc_mem, hbitmap)

    # Use flag 0 which properly captures client + frame via GDI
    user32.PrintWindow(hwnd, hdc_mem, 0)

    class BITMAPINFOHEADER(ctypes.Structure):
        _fields_ = [
            ('biSize', wintypes.DWORD),
            ('biWidth', wintypes.LONG),
            ('biHeight', wintypes.LONG),
            ('biPlanes', wintypes.WORD),
            ('biBitCount', wintypes.WORD),
            ('biCompression', wintypes.DWORD),
            ('biSizeImage', wintypes.DWORD),
            ('biXPelsPerMeter', wintypes.LONG),
            ('biYPelsPerMeter', wintypes.LONG),
            ('biClrUsed', wintypes.DWORD),
            ('biClrImportant', wintypes.DWORD)
        ]

    bmi = BITMAPINFOHEADER()
    bmi.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.biWidth = w
    bmi.biHeight = -h
    bmi.biPlanes = 1
    bmi.biBitCount = 32
    bmi.biCompression = 0

    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(hdc_mem, hbitmap, 0, h, buf, ctypes.byref(bmi), 0)

    im = Image.frombuffer('RGBA', (w, h), buf, 'raw', 'BGRA', 0, 1)
    im = im.convert('RGB')

    out_path = os.path.join(SCREENSHOT_DIR, filename)
    im.save(out_path, "PNG")

    gdi32.DeleteObject(hbitmap)
    gdi32.DeleteDC(hdc_mem)
    user32.ReleaseDC(hwnd, hdc_win)

    file_size = os.path.getsize(out_path)
    print(f"Captured {filename} ({w}x{h}, {file_size} bytes)")
    return out_path


def test_scenario_1_main_gui():
    print("\n--- [Test Scenario 1] Desktop Main GUI Bottom Bar & Fundamental Button ---")
    from main_gui import PortfolioApp
    
    # Instantiate root
    root = tk.Tk()
    root.withdraw() # hide temporary dummy if any, but PortfolioApp is a Tk
    root.destroy()

    app = PortfolioApp()
    app.geometry("1280x800+50+50")
    app.update_idletasks()
    app.update()

    # Verify button exists
    assert hasattr(app, "on_open_fundamental_analyzer"), "PortfolioApp must have on_open_fundamental_analyzer"
    
    # Find button in bottom frame
    found_btn = False
    for child in app.bottom_frame_ref.winfo_children():
        if isinstance(child, (ttk.Button, tk.Button)):
            txt = child.cget("text")
            if "基本面" in txt or "價值河流" in txt:
                found_btn = True
                print(f"[OK] Found fundamental button: '{txt}'")
                break
    assert found_btn, "Button '[基本面] 價值河流圖' not found in bottom_frame"

    # Capture Screenshot 1
    shot1 = capture_window(app, "screenshot_desktop_1_main_gui.png")
    assert os.path.exists(shot1) and os.path.getsize(shot1) > 10000, "Screenshot 1 invalid"
    print("[PASS] Scenario 1 completed successfully.")
    
    return app


def test_scenario_2_postmarket_rankings(app):
    print("\n--- [Test Scenario 2] Desktop UnifiedRankingFrame Postmarket Tab ---")
    # Switch main tab to ranking
    app.switch_main_tab("ranking")
    app.update_idletasks()
    app.update()

    # Switch subtab to postmarket
    ranking_frame = app.unified_ranking_frame
    assert hasattr(ranking_frame, "switch_sub_tab"), "UnifiedRankingFrame must have switch_sub_tab"
    ranking_frame.switch_sub_tab("postmarket")
    app.update_idletasks()
    app.update()
    time.sleep(0.5)

    # Verify postmarket view controls
    assert hasattr(ranking_frame, "lbl_regime_badge"), "lbl_regime_badge must exist"
    assert hasattr(ranking_frame, "tree_postmarket"), "tree_postmarket must exist"
    
    item_count = len(ranking_frame.tree_postmarket.get_children())
    print(f"[OK] tree_postmarket has {item_count} ranked stocks in 5-stage funnel")
    assert item_count > 0, "tree_postmarket should display ranked candidates"

    # Capture Screenshot 2
    shot2 = capture_window(app, "screenshot_desktop_2_postmarket_rankings.png")
    assert os.path.exists(shot2) and os.path.getsize(shot2) > 10000, "Screenshot 2 invalid"
    print("[PASS] Scenario 2 completed successfully.")


def test_scenario_3_fundamental_analyzer(app):
    print("\n--- [Test Scenario 3] Desktop FundamentalAnalysisDialog (2330.TW) ---")
    from fundamental_analyzer_gui import FundamentalAnalysisDialog
    
    dlg = FundamentalAnalysisDialog(app, symbol="2330.TW", name="台積電")
    dlg.geometry("1100x780+100+50")
    dlg.update_idletasks()
    dlg.update()
    time.sleep(0.5)

    # Verify Canvas & Cards exist
    assert hasattr(dlg, "river_canvas"), "river_canvas must exist"
    assert hasattr(dlg, "rev_canvas"), "rev_canvas must exist"
    assert hasattr(dlg, "margin_canvas"), "margin_canvas must exist"
    
    # Capture Screenshot 3
    shot3 = capture_window(dlg, "screenshot_desktop_3_fundamental_river_bands.png")
    assert os.path.exists(shot3) and os.path.getsize(shot3) > 10000, "Screenshot 3 invalid"
    print("[PASS] Scenario 3 completed successfully.")

    dlg.destroy()


def run_all_desktop_tests():
    print("=" * 60)
    print("Starting Desktop Tkinter GUI Verification & Screenshot Suite")
    print("=" * 60)
    
    app = None
    try:
        app = test_scenario_1_main_gui()
        test_scenario_2_postmarket_rankings(app)
        test_scenario_3_fundamental_analyzer(app)
        print("\n" + "=" * 60)
        print("[ALL TESTS PASS] All 3 desktop GUI scenarios verified and screenshotted!")
        print("=" * 60)
    finally:
        if app:
            try:
                app.destroy()
            except Exception:
                pass


if __name__ == "__main__":
    run_all_desktop_tests()
