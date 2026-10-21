import os
import sys
import time
import subprocess
import json
from datetime import datetime
import io

# Prevent encoding crashes on Windows console (Rule 13)
try:
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    if hasattr(sys.stderr, 'reconfigure'):
        sys.stderr.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

if sys.stdout is None:
    sys.stdout = io.StringIO()
if sys.stderr is None:
    sys.stderr = io.StringIO()

from core_sync import (
    find_adb, check_pixel, push_buffer_to_pixel,
    get_buffer_summary, check_google_photos_backup_status,
    trigger_auto_free_space
)

LOG_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "supervisor_auto_pilot.log")

def log(msg: str):
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    formatted = f"[{timestamp}] {msg}"
    print(formatted, flush=True)
    try:
        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(formatted + "\n")
    except Exception:
        pass

def ensure_device_awake(adb: str):
    try:
        subprocess.run([adb, "shell", "svc", "power", "stayon", "usb"], capture_output=True, timeout=5)
        subprocess.run([adb, "shell", "input", "keyevent", "224"], capture_output=True, timeout=5)
        subprocess.run([adb, "shell", "wm", "dismiss-keyguard"], capture_output=True, timeout=5)
    except Exception:
        pass

def main():
    adb = find_adb()
    if not adb:
        log("[FATAL] 未找到 adb.exe，監控程式終止。")
        sys.exit(1)

    log("=" * 60)
    log("[Auto-Pilot] iOS 2 Pixel Pro 全自動無人值守監控守護程式啟動")
    log("=" * 60)

    cycle = 1
    consecutive_errors = 0

    while True:
        try:
            # 1. 檢查緩衝庫剩餘數量
            summary = get_buffer_summary()
            pending_cnt = summary["pending_pixel"]
            pending_gb = summary["pending_gb"]

            if pending_cnt == 0:
                log("[SUCCESS] 【全部同步大告成】！電腦緩衝庫所有相片已全數成功推入 Pixel 並備份！")
                log("[SUCCESS] 監控守護程式圓滿完成任務，系統安全退出。")
                break

            log(f"\n[波次 #{cycle}] 緩衝庫待傳: {pending_cnt} 張 ({pending_gb:.2f} GB)")

            # 2. 檢查 Pixel 連線狀態與空間
            pix_ok, pix_info, free_gb = check_pixel(adb)
            if not pix_ok:
                log(f"[WAIT] Pixel 尚未就緒: {pix_info}。5 秒後重新探測...")
                time.sleep(5)
                continue

            ensure_device_awake(adb)
            log(f"  Pixel 狀態良好: {pix_info}，剩餘容量: {free_gb:.2f} GB")

            # 若空間低於安全線 (1.5 GB)，先進入釋出等待
            if free_gb < 1.5:
                log(f"[!] Pixel 空間不足 ({free_gb:.2f} GB < 1.5 GB)，等待 Google 相簿備份完成釋出空間...")
            else:
                # 3. 執行小波次推送 (< 1GB，約 0.8 GB)
                log(f"  開始執行第 #{cycle} 波次推送 (< 1GB)...")
                ok, msg, stats = push_buffer_to_pixel(
                    adb_path=adb,
                    log_callback=log,
                    batch_max_gb=0.8,
                    min_safety_gb=1.5
                )

                pushed = stats.get("pushed", 0)
                b_gb = stats.get("batch_gb", 0.0)
                log(f"  第 #{cycle} 波次推送結果: {pushed} 張 ({b_gb:.2f} GB) 送入 Pixel。")

                if pushed > 0:
                    consecutive_errors = 0
                else:
                    consecutive_errors += 1
                    if consecutive_errors >= 5:
                        log("[!] 連續多次推送未推進，重啟 adb server 修復連線...")
                        subprocess.run([adb, "kill-server"], capture_output=True, timeout=10)
                        time.sleep(3)
                        subprocess.run([adb, "start-server"], capture_output=True, timeout=10)
                        consecutive_errors = 0

            # 4. 再次檢查緩衝庫
            if get_buffer_summary()["pending_pixel"] == 0:
                log("[SUCCESS] 本波次推送完畢後，電腦緩衝庫已全數歸零！")
                continue

            # 5. 進入雲端備份與自動釋出空間監控
            log(f"[WAIT] 正在監控 Google 相簿雲端備份... (手機保持 Wi-Fi 連線)")
            start_wait = time.time()
            freed = False

            while not freed:
                time.sleep(15)
                ensure_device_awake(adb)
                wait_min = (time.time() - start_wait) / 60

                # 探測 A: 磁碟空間回升
                p_ok, _, cur_free = check_pixel(adb)
                if p_ok and cur_free >= 2.3:  # 1.5 + 0.8
                    log(f"[OK] 偵測到 Pixel 容量已回血 (目前剩餘: {cur_free:.2f} GB)！接續下一批...")
                    freed = True
                    break

                # 探測 B: UI 自動點擊『釋出空間』
                status = check_google_photos_backup_status(adb)
                if status.get("can_free") and status.get("btn_bounds"):
                    log("[ACTION] 偵測到 Google 相簿已確認備份完成！自動點擊『釋出空間』...")
                    trigger_auto_free_space(adb, status["btn_bounds"])
                    time.sleep(6)
                    freed = True
                    break
                else:
                    log(f"  ...[監控等待中] 已等待 {wait_min:.1f} 分鐘，Google 相簿正在雲端備份中...")

                # 若等待超過 10 分鐘，主動拉起 Google 相簿防休眠
                if int(wait_min) > 0 and int(wait_min) % 5 == 0:
                    subprocess.run([adb, "shell", "am", "start", "-n", "com.google.android.apps.photos/.home.HomeActivity"], capture_output=True)

            cycle += 1

        except Exception as e:
            log(f"[ERROR] 監控迴圈發生未預期錯誤: {e}，5 秒後自癒重試...")
            time.sleep(5)

if __name__ == "__main__":
    main()
