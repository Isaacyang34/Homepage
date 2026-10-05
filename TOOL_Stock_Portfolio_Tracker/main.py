import sys
import os

# 確保主控台 UTF-8 編碼避免 Windows 亂碼
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")

def main():
    try:
        from main_gui import PortfolioApp
    except ImportError as e:
        print(f"[ERROR] 缺少必要的套件: {e}")
        print("請執行: pip install -r requirements.txt")
        input("按 Enter 鍵結束...")
        sys.exit(1)

    app = PortfolioApp()
    app.mainloop()

if __name__ == "__main__":
    main()
