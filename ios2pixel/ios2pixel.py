import sys
import os
import argparse

# Ensure current directory is in sys.path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from core_sync import sync_incremental
from watcher import run_watcher

def main():
    parser = argparse.ArgumentParser(description="iOS to Pixel Pro Photo Sync")
    parser.add_argument("--cli", action="store_true", help="Run one-time sync in command-line mode without GUI")
    parser.add_argument("--watch", action="store_true", help="Run continuous background watcher daemon")
    parser.add_argument("--interval", type=int, default=10, help="Watcher poll interval in seconds (default: 10)")
    
    args, unknown = parser.parse_known_args()

    if args.watch:
        run_watcher(poll_interval=args.interval)
        return

    if args.cli:
        print("=== iOS to Pixel Incremental Sync (CLI Mode) ===")
        success, msg, stats = sync_incremental()
        if success:
            print(f"\n[SUCCESS] {msg}")
            if stats:
                print(f"Stats: {stats}")
        else:
            print(f"\n[FAILED] {msg}")
            sys.exit(1)
        return

    # Default to GUI mode if available, fallback to CLI
    try:
        from gui_app import main as gui_main
        gui_main()
    except Exception as e:
        print(f"[!] Unable to launch GUI ({e}), falling back to CLI sync...")
        success, msg, stats = sync_incremental()
        if not success:
            sys.exit(1)

if __name__ == "__main__":
    main()
