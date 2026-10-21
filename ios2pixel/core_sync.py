import os
import sys
import json
import shutil
import subprocess
import time
import asyncio
from typing import List, Dict, Optional, Tuple, Callable

import sys
import io

# Prevent GUI windowed mode NoneType stdout/stderr crashes (e.g. tqdm writing to stderr)
if sys.stdout is None:
    sys.stdout = io.StringIO()
if sys.stderr is None:
    sys.stderr = io.StringIO()

SUPPORTED_EXTENSIONS = {
    '.jpg', '.jpeg', '.png', '.heic', '.mov', '.mp4', 
    '.dng', '.gif', '.webp', '.m4v', '.avi'
}

def get_base_dir() -> str:
    if getattr(sys, 'frozen', False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))

def get_vault_dir() -> str:
    v_dir = os.path.join(get_base_dir(), "vault_buffer")
    os.makedirs(v_dir, exist_ok=True)
    return v_dir

def get_state_file() -> str:
    return os.path.join(get_base_dir(), "sync_state.json")

def load_state() -> Dict[str, dict]:
    s_file = get_state_file()
    default_state = {"ingested": {}, "pushed_to_pixel": {}}
    if not os.path.exists(s_file):
        old_record = os.path.join(get_base_dir(), "transferred.json")
        if os.path.exists(old_record):
            try:
                with open(old_record, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    for k in (data.keys() if isinstance(data, dict) else data):
                        fname = os.path.basename(k)
                        default_state["ingested"][fname] = {"remote_path": k, "size": 0}
                        default_state["pushed_to_pixel"][fname] = {"pushed_time": time.time()}
            except Exception:
                pass
        return default_state

    try:
        with open(s_file, "r", encoding="utf-8") as f:
            data = json.load(f)
            if "ingested" not in data or not isinstance(data["ingested"], dict):
                data["ingested"] = {}
            if "pushed_to_pixel" not in data:
                data["pushed_to_pixel"] = {}
            elif isinstance(data["pushed_to_pixel"], list):
                data["pushed_to_pixel"] = {k: {"pushed_time": 0} for k in data["pushed_to_pixel"] if isinstance(k, str)}
            elif not isinstance(data["pushed_to_pixel"], dict):
                data["pushed_to_pixel"] = {}
            return data
    except Exception:
        return default_state

def save_state(state: Dict[str, dict]):
    try:
        with open(get_state_file(), "w", encoding="utf-8") as f:
            json.dump(state, f, indent=2, ensure_ascii=False)
    except Exception as e:
        print(f"Error saving state: {e}")

def safe_rename(src: str, dst: str, max_retries: int = 15, delay: float = 0.25) -> bool:
    """
    Robust Windows file rename/move with retry mechanism.
    Overcomes transient [WinError 32] (sharing violation) caused by OneDrive sync,
    Windows Search Indexer, Antivirus, or Windows Shell thumbnail extraction locks.
    """
    for attempt in range(max_retries):
        try:
            if os.path.exists(dst):
                try: os.remove(dst)
                except Exception: pass
            os.replace(src, dst)
            return True
        except PermissionError:
            time.sleep(delay)
        except OSError as e:
            if getattr(e, 'winerror', None) in (32, 5):
                time.sleep(delay)
            else:
                raise
    # Fallback to copy + remove if exclusive rename lock persists
    shutil.copy2(src, dst)
    try:
        os.remove(src)
    except Exception:
        pass
    return True

def find_adb() -> Optional[str]:
    base_dir = get_base_dir()
    candidates = [
        os.path.join(base_dir, "platform-tools", "adb.exe"),
        os.path.join(base_dir, "adb.exe"),
    ]
    if hasattr(sys, "_MEIPASS"):
        candidates.append(os.path.join(sys._MEIPASS, "platform-tools", "adb.exe"))
        candidates.append(os.path.join(sys._MEIPASS, "adb.exe"))
    candidates.append(r"C:\platform-tools\adb.exe")
    
    for p in candidates:
        if os.path.isfile(p):
            return p
    
    system_adb = shutil.which("adb")
    if system_adb:
        return system_adb
    return None

def is_pixel_1_device(adb_path: str, serial: str) -> Tuple[bool, str]:
    """Check if an ADB device is a 2016 Pixel 1 (sailfish / marlin)."""
    try:
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = subprocess.SW_HIDE

        r_dev = subprocess.run(
            [adb_path, "-s", serial, "shell", "getprop", "ro.product.device"],
            capture_output=True, text=True, timeout=5, startupinfo=startupinfo
        )
        r_model = subprocess.run(
            [adb_path, "-s", serial, "shell", "getprop", "ro.product.model"],
            capture_output=True, text=True, timeout=5, startupinfo=startupinfo
        )
        dev = r_dev.stdout.strip().lower() if r_dev.returncode == 0 else ""
        model = r_model.stdout.strip() if r_model.returncode == 0 else "Pixel"

        if dev in ["sailfish", "marlin"] or model in ["Pixel", "Pixel XL"]:
            return True, model
        return False, model
    except Exception:
        return False, "Android"

def check_source_device(adb_path: Optional[str] = None) -> Tuple[bool, str, str, str]:
    """
    Check if a source phone (iPhone or non-Pixel Android) is connected.
    Returns: (is_online, display_name, device_type: 'iphone'|'android'|'none', serial_or_udid)
    """
    # 1. Check iPhone first via usbmux
    try:
        from pymobiledevice3.usbmux import list_devices
        devs = asyncio.run(list_devices())
        if devs:
            dev = devs[0]
            serial = getattr(dev, "serial", getattr(dev, "udid", "iPhone"))
            conn_type = getattr(dev, "connection_type", "USB")
            return True, f"Apple iPhone [{conn_type}] ({str(serial)[:8]}...)", "iphone", str(serial)
    except Exception:
        pass

    # 2. Check Android (non-Pixel 1)
    if not adb_path:
        adb_path = find_adb()
    if adb_path and os.path.isfile(adb_path):
        try:
            startupinfo = None
            if os.name == 'nt':
                startupinfo = subprocess.STARTUPINFO()
                startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
                startupinfo.wShowWindow = subprocess.SW_HIDE

            r = subprocess.run(
                [adb_path, "devices"],
                capture_output=True, text=True, timeout=5, startupinfo=startupinfo
            )
            if r.returncode == 0:
                lines = [line.strip() for line in r.stdout.splitlines()[1:] if line.strip()]
                for line in lines:
                    parts = line.split()
                    if len(parts) >= 2 and parts[1] == "device":
                        serial = parts[0]
                        is_pix1, model = is_pixel_1_device(adb_path, serial)
                        if not is_pix1:
                            return True, f"Android 主力機: {model} ({serial[:8]}...)", "android", serial
        except Exception:
            pass

    return False, "未連線 (等待插線 - 支援 iPhone / Android)", "none", ""

def check_iphone() -> Tuple[bool, str]:
    ok, name, _, _ = check_source_device()
    return ok, name

def get_pixel_storage_info(adb_path: str) -> Tuple[float, float, float]:
    try:
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = subprocess.SW_HIDE

        r = subprocess.run(
            [adb_path, "shell", "df", "/sdcard"],
            capture_output=True, text=True, timeout=5,
            startupinfo=startupinfo
        )
        for line in r.stdout.splitlines()[1:]:
            parts = line.split()
            if len(parts) >= 4:
                total_gb = float(parts[1]) / (1024 * 1024)
                used_gb = float(parts[2]) / (1024 * 1024)
                free_gb = float(parts[3]) / (1024 * 1024)
                return (total_gb, used_gb, free_gb)
    except Exception:
        pass
    return (0.0, 0.0, 0.0)

def check_pixel(adb_path: Optional[str] = None) -> Tuple[bool, str, float]:
    if not adb_path:
        adb_path = find_adb()
    if not adb_path or not os.path.isfile(adb_path):
        return False, "adb.exe 遺失", 0.0

    try:
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = subprocess.SW_HIDE

        r = subprocess.run(
            [adb_path, "devices"],
            capture_output=True, text=True, timeout=5,
            startupinfo=startupinfo
        )
        if r.returncode != 0:
            return False, "adb 錯誤", 0.0
        
        lines = [line.strip() for line in r.stdout.splitlines()[1:] if line.strip()]
        for line in lines:
            parts = line.split()
            if len(parts) >= 2:
                serial, state = parts[0], parts[1]
                if state == "device":
                    is_pix1, model = is_pixel_1_device(adb_path, serial)
                    if is_pix1:
                        _, _, free_gb = get_pixel_storage_info(adb_path)
                        return True, f"{model} ({serial})", free_gb
                elif state == "unauthorized":
                    return False, "未授權 (請在 Pixel 螢幕勾選允許 USB 偵錯)", 0.0
        return False, "未連線", 0.0
    except Exception:
        return False, "未連線", 0.0

# ==========================================
# 階段 1：iPhone -> 電腦緩衝庫 (超高速非同步原生傳輸)
# ==========================================
async def _async_ingest_iphone(
    progress_callback: Optional[Callable[[int, int, str, str], None]],
    log_callback: Optional[Callable[[str], None]],
    stop_event: Optional[Callable[[], bool]]
) -> Tuple[bool, str, Dict[str, int]]:
    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    from pymobiledevice3.lockdown import create_using_usbmux
    from pymobiledevice3.services.afc import AfcService

    log("[1/3] 正在建立 iPhone 加密傳輸通道...")
    try:
        lockdown = await create_using_usbmux()
    except Exception as e:
        return False, f"iPhone 連線失敗 (請解鎖並確認已信任此電腦): {e}", {}

    vault_dir = get_vault_dir()
    state = load_state()
    ingested = state["ingested"]

    log("[1/3] 開始秒級檢索 iPhone 相片目錄 (/DCIM)...")
    remote_files = []
    
    try:
        async with AfcService(lockdown=lockdown) as afc:
            scan_count = 0
            # afc.dirlist with depth -1 recursively lists all files lightning fast
            async for path in afc.dirlist("DCIM", -1):
                clean = path.lstrip("/").replace("\\", "/")
                ext = os.path.splitext(clean)[1].lower()
                if ext in SUPPORTED_EXTENSIONS:
                    remote_files.append(clean)
                    scan_count += 1
                    if scan_count % 100 == 0:
                        log(f"  ...已掃描到 {scan_count} 個媒體檔案")
    except Exception as e:
        return False, f"檢索 iPhone 相簿目錄失敗: {e}", {}

    log(f" -> 掃描完成！iPhone 共有 {len(remote_files)} 個照片/影片檔案。")

    # Filter pending files
    pending = []
    for remote_path in remote_files:
        fname = os.path.basename(remote_path)
        local_path = os.path.join(vault_dir, fname)
        if fname in ingested and os.path.isfile(local_path) and os.path.getsize(local_path) > 0:
            continue
        pending.append((remote_path, fname))

    total_pending = len(pending)
    log(f" -> 電腦緩衝庫已有: {len(remote_files) - total_pending} 個")
    log(f" -> 本次待下載至電腦新檔案: {total_pending} 個")

    if total_pending == 0:
        log("[OK] 電腦緩衝庫已與 iPhone 保持一致！所有相片皆已收錄。")
        return True, "電腦緩衝庫已是最新狀態。", {"total": len(remote_files), "downloaded": 0, "skipped": len(remote_files)}

    log(f"[2/3] 開始高效下載 {total_pending} 個檔案至電腦緩衝庫 (斷點續傳保護)...")
    success_cnt = 0
    fail_cnt = 0

    try:
        async with AfcService(lockdown=lockdown) as afc:
            for idx, (remote_path, fname) in enumerate(pending, 1):
                if stop_event and stop_event():
                    log("[!] 使用者手動暫停下載。下次接上會自動續傳！")
                    break

                final_dest = os.path.join(vault_dir, fname)
                part_dest = final_dest + ".part"

                if progress_callback:
                    progress_callback(idx, total_pending, fname, "下載中 (電腦緩衝庫)...")

                try:
                    if os.path.exists(part_dest):
                        try: os.remove(part_dest)
                        except Exception: pass

                    # Dynamic timeout based on file size (min 45s, plus 1.5s per MB)
                    # Prevents premature timeout on large videos (e.g. 400MB+ MOV)
                    try:
                        fstat = await afc.stat(remote_path)
                        fsize = fstat.get("st_size", 0)
                    except Exception:
                        fsize = 0
                    calc_timeout = max(45.0, (fsize / (1024 * 1024)) * 1.5 + 30.0)

                    await asyncio.wait_for(
                        afc.pull(remote_path, part_dest, progress_bar=False),
                        timeout=calc_timeout
                    )

                    if os.path.exists(part_dest) and os.path.getsize(part_dest) > 0:
                        safe_rename(part_dest, final_dest)
                        
                        actual_size = os.path.getsize(final_dest)
                        ingested[fname] = {
                            "remote_path": remote_path,
                            "size": actual_size,
                            "ingested_time": time.time()
                        }
                        save_state(state)
                        success_cnt += 1
                        size_mb = actual_size / (1024 * 1024)
                        log(f"  [{idx}/{total_pending}] 成功收錄: {fname} ({size_mb:.1f} MB)")
                    else:
                        log(f"  [X] 下載檢驗不符: {fname}")
                        fail_cnt += 1
                except asyncio.TimeoutError:
                    log(f"  [X] 下載超時跳過 (檔案可能存於 iCloud 雲端未下載至手機): {fname}")
                    fail_cnt += 1
                    if os.path.exists(part_dest):
                        try: os.remove(part_dest)
                        except Exception: pass
                except Exception as e:
                    log(f"  [X] 下載出錯跳過: {fname} ({e})")
                    fail_cnt += 1
                    if os.path.exists(part_dest):
                        try: os.remove(part_dest)
                        except Exception: pass
    except Exception as e:
        return False, f"傳輸過程中斷: {e}", {"downloaded": success_cnt, "failed": fail_cnt}

    summary = {
        "total": len(remote_files),
        "downloaded": success_cnt,
        "failed": fail_cnt,
        "remaining": total_pending - success_cnt
    }
    log(f"[3/3] 電腦緩衝庫下載完成！本次收錄: {success_cnt} 個，待傳: {summary['remaining']} 個。")
    return True, "iPhone 照片已安全備份至電腦緩衝庫。", summary

def ingest_iphone_to_buffer(
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, int]]:
    """Synchronous entry point that runs the async ingest engine cleanly."""
    try:
        return asyncio.run(_async_ingest_iphone(progress_callback, log_callback, stop_event))
    except Exception as e:
        if log_callback: log_callback(f"[!] 採集核心異常: {e}")
        return False, str(e), {}

# ==========================================
# 階段 1.5：iPhone 重複相片分析與安全空間釋放
# ==========================================
async def _async_analyze_iphone_redundancy(log_callback=None):
    from pymobiledevice3.lockdown import create_using_usbmux
    from pymobiledevice3.services.afc import AfcService

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    log("[1/2] 正在分析 iPhone 與電腦緩衝庫/Google 相簿之重複相片...")
    try:
        lockdown = await create_using_usbmux()
    except Exception as e:
        return False, f"iPhone 連線失敗: {e}", {}

    state = load_state()
    pushed = state.get("pushed_to_pixel", {})
    vault_dir = get_vault_dir()

    iphone_files = []
    async with AfcService(lockdown=lockdown) as afc:
        async for path in afc.dirlist("DCIM", -1):
            clean = path.lstrip("/").replace("\\", "/")
            ext = os.path.splitext(clean)[1].lower()
            if ext in SUPPORTED_EXTENSIONS:
                iphone_files.append(clean)

    safe_to_delete = []
    not_backed_up = []
    total_safe_bytes = 0

    for rpath in iphone_files:
        fname = os.path.basename(rpath)
        local_path = os.path.join(vault_dir, fname)
        if os.path.isfile(local_path) and os.path.getsize(local_path) > 0:
            sz = os.path.getsize(local_path)
            in_cloud = fname in pushed
            safe_to_delete.append({
                "remote_path": rpath,
                "fname": fname,
                "size": sz,
                "in_cloud": in_cloud
            })
            total_safe_bytes += sz
        else:
            not_backed_up.append(rpath)

    log(f"[2/2] 分析完成！")
    log(f"  -> iPhone 相片總數: {len(iphone_files)} 張")
    log(f"  -> 電腦已備份原檔: {len(safe_to_delete)} 張 ({total_safe_bytes / (1024**3):.2f} GB)")
    log(f"  -> 尚未備份 (將嚴格保護不刪): {len(not_backed_up)} 張")

    return True, "分析完成", {
        "total_iphone": len(iphone_files),
        "safe_count": len(safe_to_delete),
        "safe_gb": total_safe_bytes / (1024**3),
        "safe_files": safe_to_delete,
        "protected_count": len(not_backed_up)
    }

def analyze_iphone_redundancy(log_callback=None) -> Tuple[bool, str, Dict[str, any]]:
    try:
        return asyncio.run(_async_analyze_iphone_redundancy(log_callback))
    except Exception as e:
        return False, str(e), {}

async def _async_delete_iphone_photos(targets, progress_callback=None, log_callback=None, stop_event=None):
    from pymobiledevice3.lockdown import create_using_usbmux
    from pymobiledevice3.services.afc import AfcService

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    try:
        lockdown = await create_using_usbmux()
    except Exception as e:
        return False, f"iPhone 連線失敗: {e}", {}

    success_cnt = 0
    fail_cnt = 0
    total = len(targets)
    log(f"開始從 iPhone 刪除 {total} 個已備份檔案...")

    async with AfcService(lockdown=lockdown) as afc:
        for idx, item in enumerate(targets, 1):
            if stop_event and stop_event():
                log("[!] 使用者中止刪除。")
                break
            rpath = item["remote_path"] if isinstance(item, dict) else item
            fname = os.path.basename(rpath)
            if progress_callback:
                progress_callback(idx, total, fname, "從 iPhone 刪除中...")

            try:
                await afc.rm(rpath)
                success_cnt += 1
                if idx % 50 == 0 or idx == total or total <= 10:
                    log(f"  [{idx}/{total}] 已從 iPhone 移除: {fname}")
            except Exception as e:
                fail_cnt += 1
                log(f"  [X] 刪除失敗: {fname} ({e})")

    log(f"刪除完成！成功移除 {success_cnt} 個項目，失敗 {fail_cnt} 個。")
    return True, f"成功從 iPhone 釋放 {success_cnt} 個項目", {"deleted": success_cnt, "failed": fail_cnt}

def delete_iphone_photos(targets, progress_callback=None, log_callback=None, stop_event=None) -> Tuple[bool, str, Dict[str, any]]:
    try:
        return asyncio.run(_async_delete_iphone_photos(targets, progress_callback, log_callback, stop_event))
    except Exception as e:
        return False, str(e), {}

def ingest_android_to_buffer(
    serial: str,
    adb_path: Optional[str] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, int]]:
    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    if not adb_path: adb_path = find_adb()
    if not adb_path: return False, "未找到 adb.exe", {}

    log(f"[1/3] 正在連接 Android 主力機 ({serial[:8]}...)...")
    vault_dir = get_vault_dir()
    state = load_state()
    ingested = state["ingested"]

    startupinfo = None
    if os.name == 'nt':
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startupinfo.wShowWindow = subprocess.SW_HIDE

    log("[1/3] 開始檢索 Android 主力機相片目錄 (/sdcard/DCIM/Camera)...")
    r = subprocess.run(
        [adb_path, "-s", serial, "shell", "ls", "-1", "/sdcard/DCIM/Camera/"],
        capture_output=True, text=True, timeout=15, startupinfo=startupinfo
    )
    if r.returncode != 0:
        r = subprocess.run(
            [adb_path, "-s", serial, "shell", "ls", "-1", "/sdcard/DCIM/"],
            capture_output=True, text=True, timeout=15, startupinfo=startupinfo
        )
        remote_base = "/sdcard/DCIM/"
    else:
        remote_base = "/sdcard/DCIM/Camera/"

    all_names = [line.strip() for line in r.stdout.splitlines() if line.strip()]
    remote_files = []
    for name in all_names:
        ext = os.path.splitext(name)[1].lower()
        if ext in SUPPORTED_EXTENSIONS:
            remote_files.append(name)

    log(f" -> 掃描完成！Android 手機共有 {len(remote_files)} 個照片/影片檔案。")

    pending = []
    for fname in remote_files:
        local_path = os.path.join(vault_dir, fname)
        if fname in ingested and os.path.isfile(local_path) and os.path.getsize(local_path) > 0:
            continue
        pending.append(fname)

    total_pending = len(pending)
    log(f" -> 電腦緩衝庫已有: {len(remote_files) - total_pending} 個")
    log(f" -> 本次待下載至電腦新檔案: {total_pending} 個")

    if total_pending == 0:
        log("[OK] 電腦緩衝庫已與 Android 手機保持一致！所有相片皆已收錄。")
        return True, "電腦緩衝庫已是最新狀態。", {"total": len(remote_files), "downloaded": 0, "skipped": len(remote_files)}

    log(f"[2/3] 開始自 Android 手機高效下載 {total_pending} 個檔案至電腦緩衝庫...")
    success_cnt = 0
    fail_cnt = 0

    for idx, fname in enumerate(pending, 1):
        if stop_event and stop_event():
            log("[!] 使用者手動暫停下載。下次接上會自動續傳！")
            break

        final_dest = os.path.join(vault_dir, fname)
        part_dest = final_dest + ".part"

        if progress_callback:
            progress_callback(idx, total_pending, fname, "下載中 (電腦緩衝庫)...")

        remote_path = remote_base + fname
        try:
            if os.path.exists(part_dest):
                try: os.remove(part_dest)
                except Exception: pass

            pull_r = subprocess.run(
                [adb_path, "-s", serial, "pull", remote_path, part_dest],
                capture_output=True, text=True, timeout=180, startupinfo=startupinfo
            )
            if pull_r.returncode == 0 and os.path.isfile(part_dest) and os.path.getsize(part_dest) > 0:
                sz = os.path.getsize(part_dest)
                if safe_rename(part_dest, final_dest):
                    ingested[fname] = {"remote_path": remote_path, "size": sz, "ingested_time": time.time()}
                    save_state(state)
                    success_cnt += 1
                else:
                    fail_cnt += 1
            else:
                fail_cnt += 1
        except Exception:
            fail_cnt += 1

    log(f"[3/3] 採集結束！成功收錄: {success_cnt} 個檔案 (失敗: {fail_cnt})。")
    return True, f"成功下載 {success_cnt} 個新檔案至電腦緩衝庫。", {"total": len(remote_files), "downloaded": success_cnt, "failed": fail_cnt}

def ingest_source_to_buffer(
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, int]]:
    ok, name, dev_type, dev_id = check_source_device()
    if not ok:
        return False, "未連線來源主力手機 (支援 iPhone 或 Android 主力機)", {}
    if dev_type == "iphone":
        return ingest_iphone_to_buffer(progress_callback, log_callback, stop_event)
    elif dev_type == "android":
        return ingest_android_to_buffer(dev_id, find_adb(), progress_callback, log_callback, stop_event)
    return False, "未知設備類型", {}

def analyze_source_redundancy(log_callback=None) -> Tuple[bool, str, Dict[str, any]]:
    ok, name, dev_type, dev_id = check_source_device()
    if not ok:
        return False, "未連線來源主力手機", {}
    if dev_type == "iphone":
        return analyze_iphone_redundancy(log_callback)
    elif dev_type == "android":
        adb_path = find_adb()
        vault_dir = get_vault_dir()
        state = load_state()
        pushed = state.get("pushed_to_pixel", {})

        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = subprocess.SW_HIDE

        r = subprocess.run(
            [adb_path, "-s", dev_id, "shell", "ls", "-1", "/sdcard/DCIM/Camera/"],
            capture_output=True, text=True, timeout=15, startupinfo=startupinfo
        )
        remote_base = "/sdcard/DCIM/Camera/" if r.returncode == 0 else "/sdcard/DCIM/"
        lines = [line.strip() for line in r.stdout.splitlines() if line.strip()]

        safe_to_delete = []
        unbacked_up = []
        total_safe_bytes = 0

        for fname in lines:
            ext = os.path.splitext(fname)[1].lower()
            if ext not in SUPPORTED_EXTENSIONS:
                continue
            local_path = os.path.join(vault_dir, fname)
            if os.path.isfile(local_path) and os.path.getsize(local_path) > 0 and fname in pushed:
                sz = os.path.getsize(local_path)
                safe_to_delete.append({
                    "remote_path": remote_base + fname,
                    "fname": fname,
                    "size": sz,
                    "dev_type": "android",
                    "serial": dev_id
                })
                total_safe_bytes += sz
            else:
                unbacked_up.append(fname)

        safe_gb = total_safe_bytes / (1024 * 1024 * 1024)
        return True, "分析完成", {
            "total_iphone_cnt": len(safe_to_delete) + len(unbacked_up),
            "safe_to_delete": safe_to_delete,
            "safe_cnt": len(safe_to_delete),
            "safe_gb": safe_gb,
            "unbacked_up": unbacked_up,
            "unbacked_cnt": len(unbacked_up)
        }
    return False, "未知設備", {}

def delete_source_photos(targets, progress_callback=None, log_callback=None, stop_event=None) -> Tuple[bool, str, Dict[str, any]]:
    if not targets:
        return True, "無刪除項目", {}
    first_item = targets[0]
    dev_type = first_item.get("dev_type", "iphone") if isinstance(first_item, dict) else "iphone"
    if dev_type == "iphone":
        return delete_iphone_photos(targets, progress_callback, log_callback, stop_event)
    elif dev_type == "android":
        adb_path = find_adb()
        startupinfo = None
        if os.name == 'nt':
            startupinfo = subprocess.STARTUPINFO()
            startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
            startupinfo.wShowWindow = subprocess.SW_HIDE

        succ = 0
        fail = 0
        total = len(targets)
        for idx, item in enumerate(targets, 1):
            if stop_event and stop_event():
                break
            rpath = item["remote_path"]
            serial = item.get("serial")
            if progress_callback:
                progress_callback(idx, total, os.path.basename(rpath), "從 Android 刪除中...")
            cmd = [adb_path]
            if serial:
                cmd.extend(["-s", serial])
            cmd.extend(["shell", "rm", f'"{rpath}"'])
            r = subprocess.run(cmd, capture_output=True, text=True, timeout=10, startupinfo=startupinfo)
            if r.returncode == 0:
                succ += 1
            else:
                fail += 1
        return True, f"成功從 Android 釋放 {succ} 個項目", {"deleted": succ, "failed": fail, "success": succ, "freed_gb": 0.0}
    return False, "未知設備", {}

# ==========================================
# 階段 2：電腦緩衝庫 -> Pixel (容量精算波次推送)
# ==========================================
def push_buffer_to_pixel(
    adb_path: Optional[str] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None,
    batch_max_gb: float = 0.8,
    min_safety_gb: float = 1.5
) -> Tuple[bool, str, Dict[str, float]]:
    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    if not adb_path:
        adb_path = find_adb()
    if not adb_path:
        return False, "未找到 adb.exe，請確認 platform-tools。", {}

    pix_ok, pix_info, free_gb = check_pixel(adb_path)
    if not pix_ok:
        return False, f"Pixel 尚未連線: {pix_info}", {}

    log(f"[1/4] 檢查 Pixel 磁碟空間: 目前剩餘可用 {free_gb:.2f} GB")

    if free_gb < min_safety_gb:
        msg = f"Pixel 剩餘空間不足安全線 ({free_gb:.2f} GB < {min_safety_gb} GB)！請在 Pixel 的 Google 相簿點擊『釋出裝置空間』。"
        log(f"[!] 警告: {msg}")
        return False, msg, {"free_gb": free_gb}

    budget_gb = max(0.0, free_gb - min_safety_gb)
    batch_target_gb = min(budget_gb, batch_max_gb)
    log(f" -> Pixel 本次安全承載預算: {batch_target_gb:.2f} GB (預留 {min_safety_gb} GB 緩衝)")

    vault_dir = get_vault_dir()
    state = load_state()
    pushed = state["pushed_to_pixel"]

    # Load cloud quota washer manifest: isolate untrashed quota items!
    manifest_path = os.path.join(get_base_dir(), "cloud_quota_manifest.json")
    untrashed_quota_items = set()
    if os.path.exists(manifest_path):
        try:
            with open(manifest_path, "r", encoding="utf-8") as mf:
                m_data = json.load(mf)
                for it in m_data.get("items", []):
                    # Only allow pushing to Pixel AFTER the cloud copy has been trashed!
                    if not it.get("trashed"):
                        untrashed_quota_items.add(it.get("fileName"))
        except Exception:
            pass

    candidates = []
    for fname in os.listdir(vault_dir):
        if fname.endswith(".part"):
            continue
        if fname in pushed:
            continue
        # Strict isolation: Do NOT push cloud quota items until trashed from cloud!
        if fname in untrashed_quota_items:
            continue
        fpath = os.path.join(vault_dir, fname)
        if os.path.isfile(fpath):
            candidates.append((fname, fpath, os.path.getsize(fpath)))

    log(f"[2/4] 掃描電腦緩衝庫: 待送往 Pixel 之照片總數: {len(candidates)} 個")

    if len(candidates) == 0:
        log("[OK] 電腦緩衝庫內所有照片均已傳入 Pixel，無待傳檔案！")
        return True, "緩衝庫所有相片皆已上傳完畢。", {"pushed": 0, "total_candidates": 0}

    selected_batch = []
    accumulated_bytes = 0
    max_bytes = batch_target_gb * 1024 * 1024 * 1024

    for fname, fpath, fsize in candidates:
        if accumulated_bytes + fsize > max_bytes and len(selected_batch) > 0:
            break
        selected_batch.append((fname, fpath, fsize))
        accumulated_bytes += fsize

    total_batch_cnt = len(selected_batch)
    batch_gb = accumulated_bytes / (1024 * 1024 * 1024)
    log(f"[3/4] 容量規劃完成: 本次將推送 {total_batch_cnt} 個檔案 (合計 {batch_gb:.2f} GB) 至 Pixel 1...")

    startupinfo = None
    if os.name == 'nt':
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startupinfo.wShowWindow = subprocess.SW_HIDE

    success_cnt = 0
    fail_cnt = 0
    remote_dir = "/sdcard/DCIM/Camera/"

    for idx, (fname, fpath, fsize) in enumerate(selected_batch, 1):
        if stop_event and stop_event():
            log("[!] 使用者手動暫停傳輸。")
            break

        file_mb = fsize / (1024 * 1024)
        if progress_callback:
            progress_callback(idx, total_batch_cnt, fname, f"推送至 Pixel ({file_mb:.1f} MB)...")

        remote_dest = remote_dir + fname
        try:
            r = subprocess.run(
                [adb_path, "push", fpath, remote_dest],
                capture_output=True, text=True, timeout=120,
                startupinfo=startupinfo
            )

            if r.returncode == 0:
                if not isinstance(pushed, dict):
                    pushed = state["pushed_to_pixel"] = {}
                pushed[fname] = {"pushed_time": time.time(), "size": fsize}
                save_state(state)
                success_cnt += 1
                log(f"  [{idx}/{total_batch_cnt}] 成功送入 Pixel: {fname} ({file_mb:.1f} MB)")
            else:
                log(f"  [X] 推送至 Pixel 失敗: {fname} ({r.stderr.strip()[:60] if r.stderr else '未知錯誤'})")
                fail_cnt += 1
        except Exception as e:
            log(f"  [X] 推送異常跳過: {fname} ({e})")
            fail_cnt += 1

    # Batch completed: trigger a single media scan notification
    try:
        subprocess.run(
            [adb_path, "shell", "am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
             "-d", f"file://{remote_dir}"],
            capture_output=True, text=True, timeout=10,
            startupinfo=startupinfo
        )
    except Exception:
        pass

    remaining_in_buffer = len(candidates) - success_cnt
    summary = {
        "pushed": success_cnt,
        "failed": fail_cnt,
        "remaining_buffer": remaining_in_buffer,
        "batch_gb": batch_gb
    }

    log(f"[4/4] 本波次推送完畢！成功送達 Pixel: {success_cnt} 張 ({batch_gb:.2f} GB)。")
    if remaining_in_buffer > 0:
        log(f"💡 提醒: 電腦緩衝庫尚有 {remaining_in_buffer} 張待傳。")
        log("請等待 Pixel Google 相簿備份完成，在手機內點選『釋出裝置空間』清空實體空間後，再次接上 Pixel 繼續推送下一批！")
    else:
        log("🎉 電腦緩衝庫所有照片已全部送入 Pixel！Pixel 正在背景享有原始畫質終生無限雲端備份。")

    return True, f"本批次推送完成 ({success_cnt} 張 / {batch_gb:.2f} GB)。", summary

def sync_incremental(
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    log("=== 啟動一鍵分時串接同步 ===")
    ok1, msg1, stats1 = ingest_iphone_to_buffer(progress_callback, log_callback, stop_event)
    if not ok1 and "未連線" not in msg1:
        return False, msg1, {"ingest": stats1}

    ok2, msg2, stats2 = push_buffer_to_pixel(
        progress_callback=progress_callback,
        log_callback=log_callback,
        stop_event=stop_event
    )
    return ok2, f"{msg1} ➔ {msg2}", {"ingest": stats1, "push": stats2}

def get_buffer_summary() -> Dict[str, any]:
    vault_dir = get_vault_dir()
    state = load_state()
    pushed = state.get("pushed_to_pixel", {})

    manifest_path = os.path.join(get_base_dir(), "cloud_quota_manifest.json")
    untrashed_quota_items = set()
    if os.path.exists(manifest_path):
        try:
            with open(manifest_path, "r", encoding="utf-8") as mf:
                m_data = json.load(mf)
                for it in m_data.get("items", []):
                    if not it.get("trashed"):
                        untrashed_quota_items.add(it.get("fileName"))
        except Exception:
            pass
    
    total_files = 0
    total_bytes = 0
    pending_pixel = 0
    pending_bytes = 0

    if os.path.exists(vault_dir):
        for fname in os.listdir(vault_dir):
            if fname.endswith(".part"): continue
            fpath = os.path.join(vault_dir, fname)
            if os.path.isfile(fpath):
                sz = os.path.getsize(fpath)
                total_files += 1
                total_bytes += sz
                if fname not in pushed and fname not in untrashed_quota_items:
                    pending_pixel += 1
                    pending_bytes += sz

    return {
        "total_files": total_files,
        "total_gb": total_bytes / (1024 * 1024 * 1024),
        "pending_pixel": pending_pixel,
        "pending_gb": pending_bytes / (1024 * 1024 * 1024)
    }

# ==========================================
# 階段 3：Auto-Pilot 全自動閉環模式
# ==========================================
def check_google_photos_backup_status(adb_path: str) -> Dict[str, any]:
    """
    探測 Pixel Google 相簿備份與釋出空間按鈕狀態。
    """
    try:
        # 1. 喚醒螢幕並保持 USB 恆亮，嘗試解鎖
        subprocess.run([adb_path, "shell", "svc", "power", "stayon", "usb"], capture_output=True, timeout=5)
        subprocess.run([adb_path, "shell", "input", "keyevent", "224"], capture_output=True, timeout=5)
        subprocess.run([adb_path, "shell", "wm", "dismiss-keyguard"], capture_output=True, timeout=5)
        
        # 2. 檢測 Google 相簿通知
        notif_res = subprocess.run([adb_path, "shell", "dumpsys", "notification", "--noredact"], capture_output=True, text=True, timeout=10)
        in_backup = False
        if notif_res.stdout:
            for line in notif_res.stdout.splitlines():
                if "backup_1_progress" in line or ("com.google.android.apps.photos" in line and any(k in line for k in ["正在備份", "Backing up", "Uploading", "正在上傳"])):
                    in_backup = True
                    break

        # 3. 直連 Google 相簿釋出空間頁面
        subprocess.run([
            adb_path, "shell", "am", "start", "-a", "android.intent.action.VIEW",
            "-d", "https://photos.google.com/link/freeupspace", "-p", "com.google.android.apps.photos"
        ], capture_output=True, timeout=10)
        time.sleep(2.0)

        # 4. 分析 UI 樹狀結構
        subprocess.run([adb_path, "shell", "uiautomator", "dump", "/sdcard/freeup_dump.xml"], capture_output=True, timeout=15)
        xml_res = subprocess.run([adb_path, "shell", "cat", "/sdcard/freeup_dump.xml"], capture_output=True, text=True, encoding="utf-8", errors="ignore", timeout=10)
        
        can_free = False
        btn_bounds = None
        if xml_res.stdout and "<hierarchy" in xml_res.stdout:
            import xml.etree.ElementTree as ET
            root = ET.fromstring(xml_res.stdout)
            for elem in root.iter():
                text = elem.attrib.get("text", "")
                desc = elem.attrib.get("content-desc", "")
                combined = f"{text} {desc}"
                if "無項目可清除" in combined or "No items" in combined:
                    can_free = False
                    break
                if any(k in combined for k in ["釋出", "Free up"]):
                    bounds = elem.attrib.get("bounds", "")
                    if bounds and elem.attrib.get("clickable") == "true":
                        can_free = True
                        btn_bounds = bounds
                        break

        return {
            "in_backup": in_backup,
            "can_free": can_free,
            "btn_bounds": btn_bounds
        }
    except Exception as e:
        return {"in_backup": False, "can_free": False, "btn_bounds": None, "error": str(e)}

def trigger_auto_free_space(adb_path: str, bounds_str: str) -> bool:
    """自動點擊 Google 相簿『釋出空間』按鈕與確認對話框"""
    try:
        import re
        m = re.findall(r"\d+", bounds_str)
        if len(m) >= 4:
            x1, y1, x2, y2 = map(int, m[:4])
            cx = (x1 + x2) // 2
            cy = (y1 + y2) // 2
            subprocess.run([adb_path, "shell", "input", "tap", str(cx), str(cy)], capture_output=True, timeout=5)
            time.sleep(1.5)
            # 送出確認 (Enter 鍵 / 確定)
            subprocess.run([adb_path, "shell", "input", "keyevent", "66"], capture_output=True, timeout=5)
            return True
    except Exception:
        pass
    return False

def run_auto_pilot_loop(
    adb_path: Optional[str] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None,
    batch_max_gb: float = 0.8,
    min_safety_gb: float = 1.5
) -> Tuple[bool, str, Dict[str, any]]:
    """
    Auto-Pilot 全自動閉環同步：
    分多次推送小波次 (<1GB) ➔ 等待 Google 相簿備份 ➔ 自動釋出空間 ➔ 輪番推送，直到完全結束。
    """
    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    if not adb_path: adb_path = find_adb()
    if not adb_path: return False, "未找到 adb.exe", {}

    cycle = 1
    total_pushed_all = 0
    total_gb_all = 0.0

    log("🚀 [Auto-Pilot] 啟動全自動閉環同步模式 (每波次 < 1GB，自動確認備份 ➔ 釋出空間 ➔ 輪番續傳)...")

    while True:
        if stop_event and stop_event():
            log("[!] 使用者手動暫停 Auto-Pilot。下次啟動將自動續傳！")
            break

        summary = get_buffer_summary()
        rem_files = summary["pending_pixel"]
        if rem_files == 0:
            log("🎉 [Auto-Pilot] 恭喜！電腦緩衝庫所有相片均已全部送入 Pixel 並完成同步！")
            break

        log(f"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")
        log(f"▶ [Auto-Pilot 循環波次 #{cycle}] 緩衝庫尚有 {rem_files} 個檔案待傳 (規劃 < 1GB 小波次)...")
        log(f"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━")

        ok, msg, stats = push_buffer_to_pixel(
            adb_path=adb_path,
            progress_callback=progress_callback,
            log_callback=log_callback,
            stop_event=stop_event,
            batch_max_gb=batch_max_gb,
            min_safety_gb=min_safety_gb
        )

        batch_pushed = stats.get("pushed", 0)
        batch_gb = stats.get("batch_gb", 0.0)
        total_pushed_all += batch_pushed
        total_gb_all += batch_gb

        if not ok and batch_pushed == 0:
            if "空間不足" not in msg:
                log(f"[Auto-Pilot] 推送中止: {msg}")
                return False, msg, {"cycles": cycle, "total_pushed": total_pushed_all}

        if get_buffer_summary()["pending_pixel"] == 0:
            log("🎉 [Auto-Pilot] 本波次推送完畢，緩衝庫已全數歸零！")
            break

        # 進入等待 Google 相簿備份與自動釋出空間階段
        log(f"⏳ [Auto-Pilot] 第 #{cycle} 波次已安全送入 ({batch_pushed} 張 / {batch_gb:.2f} GB)。")
        log("   正在監控 Google 相簿雲端備份進度... (手機請保持解鎖連 Wi-Fi，系統已啟用螢幕恆亮)")

        freed = False
        start_wait = time.time()
        poll_count = 0

        while not freed:
            if stop_event and stop_event():
                log("[!] 使用者在等待備份期間暫停 Auto-Pilot。")
                return True, "已手動暫停", {"cycles": cycle, "total_pushed": total_pushed_all}

            time.sleep(15)
            poll_count += 1
            wait_min = (time.time() - start_wait) / 60

            # 方案 1：直接檢查 Pixel 剩餘磁碟空間（若使用者已在手機點擊或系統已釋放）
            pix_ok, pix_info, cur_free_gb = check_pixel(adb_path)
            if pix_ok and cur_free_gb >= (min_safety_gb + 0.8):
                log(f"✨ [Auto-Pilot] 偵測到 Pixel 磁碟空間已釋放 (可用空間: {cur_free_gb:.2f} GB)！雲端備份已就緒。")
                freed = True
                break

            # 方案 2：探測 Google 相簿 UI 是否出現『釋出空間』按鈕
            status = check_google_photos_backup_status(adb_path)
            if status.get("can_free") and status.get("btn_bounds"):
                log(f"⚡ [Auto-Pilot] 偵測到 Google 相簿已確認備份完成！自動點擊『釋出空間』按鈕...")
                trigger_auto_free_space(adb_path, status["btn_bounds"])
                time.sleep(6)
                freed = True
                break
            else:
                log(f"  ...[Auto-Pilot 等待中] 已等待 {wait_min:.1f} 分鐘，Google 相簿正在雲端上傳中 (Pixel 剩餘: {cur_free_gb:.2f} GB)...")

        cycle += 1
        time.sleep(3)

    return True, f"Auto-Pilot 完成 (共執行 {cycle} 個波次，累計傳送 {total_pushed_all} 張 / {total_gb_all:.2f} GB)", {"cycles": cycle, "total_pushed": total_pushed_all}


# ==========================================
# 階段 3：iPhone 重複相片分析與安全釋放
# ==========================================
async def _async_analyze_iphone_redundancy(log_callback=None) -> Tuple[bool, str, Dict[str, any]]:
    from pymobiledevice3.lockdown import create_using_usbmux
    from pymobiledevice3.services.afc import AfcService

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    log("[分析] 正在連接 iPhone 並比對本機緩衝庫與 Pixel 備份紀錄...")
    try:
        lockdown = await create_using_usbmux()
    except Exception as e:
        return False, f"iPhone 連線失敗 (請確認螢幕已解鎖並信任此電腦): {e}", {}

    vault_dir = get_vault_dir()
    state = load_state()
    pushed = state.get("pushed_to_pixel", {})

    safe_to_delete = []
    unbacked_up = []
    total_safe_bytes = 0
    total_unbacked_bytes = 0

    try:
        async with AfcService(lockdown=lockdown) as afc:
            async for path in afc.dirlist("DCIM", -1):
                clean = path.lstrip("/").replace("\\", "/")
                ext = os.path.splitext(clean)[1].lower()
                if ext in SUPPORTED_EXTENSIONS:
                    fname = os.path.basename(clean)
                    try:
                        stat = await afc.stat(clean)
                        fsize = int(stat.get("st_size", 0))
                    except Exception:
                        fsize = 0

                    local_path = os.path.join(vault_dir, fname)
                    # 雙重安全門檻：
                    # 1. 本地電腦緩衝庫確有此檔案且檔案大小 > 0
                    # 2. 已登記在 pushed_to_pixel (已送入 Pixel 相簿上傳佇列)
                    has_local = os.path.isfile(local_path) and os.path.getsize(local_path) > 0
                    has_pushed = fname in pushed

                    item_info = {
                        "remote_path": clean,
                        "filename": fname,
                        "size": fsize
                    }

                    if has_local and has_pushed:
                        safe_to_delete.append(item_info)
                        total_safe_bytes += fsize
                    else:
                        unbacked_up.append(item_info)
                        total_unbacked_bytes += fsize
    except Exception as e:
        return False, f"分析 iPhone 相簿失敗: {e}", {}

    safe_gb = total_safe_bytes / (1024 * 1024 * 1024)
    unbacked_gb = total_unbacked_bytes / (1024 * 1024 * 1024)
    total_iphone_cnt = len(safe_to_delete) + len(unbacked_up)

    result = {
        "total_iphone_cnt": total_iphone_cnt,
        "safe_to_delete": safe_to_delete,
        "safe_cnt": len(safe_to_delete),
        "safe_gb": safe_gb,
        "unbacked_up": unbacked_up,
        "unbacked_cnt": len(unbacked_up),
        "unbacked_gb": unbacked_gb
    }

    log(f"[分析完成] iPhone 內部共掃描到 {total_iphone_cnt} 個媒體檔案：")
    log(f"  [OK] 雙備份已完成 (電腦+Pixel): {len(safe_to_delete)} 張 ({safe_gb:.2f} GB) -> 100% 可安全從 iPhone 刪除釋放空間")
    if len(unbacked_up) > 0:
        log(f"  [!] 尚未完整備份: {len(unbacked_up)} 張 ({unbacked_gb:.2f} GB) -> 系統嚴格鎖定保護，絕不刪除！")
    else:
        log("  [*] 完美！iPhone 內所有相片均已在電腦緩衝庫與 Pixel 雙重留存。")

    return True, "分析完成", result

def analyze_iphone_redundancy(log_callback=None) -> Tuple[bool, str, Dict[str, any]]:
    try:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(_async_analyze_iphone_redundancy(log_callback))
    finally:
        loop.close()

async def _async_delete_iphone_photos(
    targets: List[Dict[str, any]],
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    from pymobiledevice3.lockdown import create_using_usbmux
    from pymobiledevice3.services.afc import AfcService

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    if not targets:
        return False, "無指定刪除對象", {}

    log(f"[清理準備] 即將對 iPhone 執行安全刪除操作，目標數量: {len(targets)} 張...")
    try:
        lockdown = await create_using_usbmux()
    except Exception as e:
        return False, f"iPhone 連線失敗: {e}", {}

    vault_dir = get_vault_dir()
    success_cnt = 0
    fail_cnt = 0
    freed_bytes = 0

    try:
        async with AfcService(lockdown=lockdown) as afc:
            for idx, item in enumerate(targets, 1):
                if stop_event and stop_event():
                    log("[!] 使用者手動暫停刪除作業。")
                    break

                rem_path = item["remote_path"]
                fname = item["filename"]
                fsize = item.get("size", 0)

                # 實體檔案防呆熔斷：電腦本地緩衝庫必須 100% 存在且大於 0 byte
                local_path = os.path.join(vault_dir, fname)
                if not (os.path.isfile(local_path) and os.path.getsize(local_path) > 0):
                    log(f"  [保護攔截] 略過 {fname}: 電腦緩衝庫無完整實體備份，絕對禁止刪除！")
                    fail_cnt += 1
                    continue

                if progress_callback:
                    progress_callback(idx, len(targets), fname, f"正在從 iPhone 刪除 ({idx}/{len(targets)})...")

                try:
                    await afc.rm(rem_path)
                    success_cnt += 1
                    freed_bytes += fsize
                    log(f"  [{idx}/{len(targets)}] 已成功刪除 iPhone 原檔: {fname} (釋出 {fsize/(1024*1024):.2f} MB)")
                except Exception as e:
                    log(f"  [X] 刪除失敗: {fname} - {e}")
                    fail_cnt += 1
    except Exception as e:
        return False, f"AFC 服務異常中斷: {e}", {"success": success_cnt, "failed": fail_cnt}

    freed_gb = freed_bytes / (1024 * 1024 * 1024)
    log(f"[清理結束] 成功從 iPhone 刪除 {success_cnt} 張相片，共釋放 {freed_gb:.2f} GB 空間！(失敗: {fail_cnt})")
    return True, f"刪除完成: 成功 {success_cnt} 張，釋出 {freed_gb:.2f} GB", {"success": success_cnt, "failed": fail_cnt, "freed_gb": freed_gb}

def delete_iphone_photos(
    targets: List[Dict[str, any]],
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    log_callback: Optional[Callable[[str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    try:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(_async_delete_iphone_photos(targets, progress_callback, log_callback, stop_event))
    finally:
        loop.close()


