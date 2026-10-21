import time
import sys
import os
from core_sync import check_iphone, check_pixel, sync_incremental, find_adb

def run_watcher(poll_interval: int = 10):
    print("=" * 60)
    print("iOS to Pixel Auto Watcher Daemon")
    print("Monitoring USB ports for iPhone and Google Pixel...")
    print(f"Poll interval: {poll_interval}s")
    print("=" * 60)

    adb_path = find_adb()
    if not adb_path:
        print("[!] Warning: adb.exe not found. Please install platform-tools.")

    armed = True  # Ready to trigger when both connected

    while True:
        try:
            iph_ok, iph_info = check_iphone()
            pix_ok, pix_info, free_gb = check_pixel(adb_path)

            both = iph_ok and pix_ok

            # Print status summary
            status_line = f"[{time.strftime('%H:%M:%S')}] iPhone: {'[ONLINE] ' + iph_info if iph_ok else '[OFFLINE]'} | Pixel: {'[ONLINE] ' + pix_info if pix_ok else '[OFFLINE]'}"
            print(status_line)

            if both and armed:
                print("\n" + "#" * 60)
                print(">>> Both iPhone and Pixel detected! Starting synchronization...")
                print("#" * 60)
                
                success, msg, stats = sync_incremental()
                
                print("\n" + "=" * 60)
                print(f"Sync finished: {msg}")
                if stats:
                    print(f"Synced: {stats.get('transferred', 0)}, Skipped: {stats.get('skipped', 0)}, Failed: {stats.get('failed', 0)}")
                print("Please disconnect iPhone or Pixel when done. Next sync will trigger on reconnect.")
                print("=" * 60 + "\n")
                
                armed = False  # Prevent looping on same connection
            elif not both and not armed:
                print("[-] Device disconnected. Auto-sync re-armed for next plug-in.")
                armed = True

        except KeyboardInterrupt:
            print("\nWatcher stopped by user.")
            break
        except Exception as e:
            print(f"[!] Watcher error: {e}")

        time.sleep(poll_interval)

if __name__ == "__main__":
    interval = 10
    if len(sys.argv) > 1 and sys.argv[1].isdigit():
        interval = int(sys.argv[1])
    run_watcher(interval)
