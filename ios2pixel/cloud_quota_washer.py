import os
import sys
import json
import time
import asyncio
import subprocess
import urllib.request
import urllib.error
from typing import List, Dict, Optional, Tuple, Callable

CDP_PORT = 9222
CDP_URL = f"http://127.0.0.1:{CDP_PORT}"

def get_base_dir() -> str:
    if getattr(sys, 'frozen', False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))

def get_manifest_path() -> str:
    return os.path.join(get_base_dir(), "cloud_quota_manifest.json")

def get_cloud_ledger_path() -> str:
    return os.path.join(get_base_dir(), "google_photos_cloud_ledger.json")

def get_profile_dir() -> str:
    p_dir = os.path.join(get_base_dir(), "gphotos_profile")
    os.makedirs(p_dir, exist_ok=True)
    return p_dir

def find_chrome() -> Optional[str]:
    candidates = [
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    ]
    for c in candidates:
        if os.path.isfile(c):
            return c
    return None

def is_cdp_ready() -> bool:
    try:
        req = urllib.request.Request(f"{CDP_URL}/json", headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=2) as resp:
            return resp.status == 200
    except Exception:
        return False

def launch_chrome_with_cdp(target_url: str = "https://photos.google.com") -> Tuple[bool, str]:
    if is_cdp_ready():
        return True, "CDP 服務已連線"
    
    chrome_path = find_chrome()
    if not chrome_path:
        return False, "未找到 Google Chrome 或 Edge 瀏覽器，請確認已安裝 Chrome"
    
    profile_dir = get_profile_dir()
    args = [
        chrome_path,
        f"--remote-debugging-port={CDP_PORT}",
        f"--user-data-dir={profile_dir}",
        "--no-first-run",
        "--no-default-browser-check",
        target_url
    ]
    try:
        subprocess.Popen(args)
        # Wait up to 12s for CDP port to open
        for _ in range(24):
            time.sleep(0.5)
            if is_cdp_ready():
                return True, "Chrome CDP 連線就緒"
        return False, "Chrome 已啟動，但未能開啟除錯連接埠 (9222)"
    except Exception as e:
        return False, f"啟動 Chrome 失敗: {e}"

class CdpSession:
    def __init__(self, ws):
        self.ws = ws
        self.msg_id = 0
        self.pending = {}

    async def _listen(self):
        try:
            async for raw in self.ws:
                msg = json.loads(raw)
                req_id = msg.get("id")
                if req_id and req_id in self.pending:
                    fut = self.pending.pop(req_id)
                    if "error" in msg:
                        fut.set_exception(Exception(msg["error"].get("message", "CDP Error")))
                    else:
                        fut.set_result(msg.get("result"))
        except Exception:
            pass

    async def send(self, method: str, params: dict = None) -> dict:
        self.msg_id += 1
        curr_id = self.msg_id
        if params is None:
            params = {}
        loop = asyncio.get_event_loop()
        fut = loop.create_future()
        self.pending[curr_id] = fut
        await self.ws.send(json.dumps({"id": curr_id, "method": method, "params": params}))
        return await fut

    async def evaluate(self, expression: str, await_promise: bool = True):
        res = await self.send("Runtime.evaluate", {
            "expression": expression,
            "awaitPromise": await_promise,
            "returnByValue": True
        })
        if res.get("exceptionDetails"):
            err = res["exceptionDetails"].get("text", "") or json.dumps(res["exceptionDetails"])
            raise Exception(f"JS Error: {err}")
        return res.get("result", {}).get("value")

async def get_photos_tab_ws_url() -> Tuple[str, bool]:
    req = urllib.request.Request(f"{CDP_URL}/json", headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=5) as resp:
        tabs = json.loads(resp.read().decode("utf-8"))
    
    photos_tab = None
    for t in tabs:
        url = t.get("url", "")
        if "photos.google.com" in url:
            photos_tab = t
            break
            
    if photos_tab:
        return photos_tab.get("webSocketDebuggerUrl"), False
    
    if len(tabs) > 0:
        return tabs[0].get("webSocketDebuggerUrl"), True
    
    raise Exception("Chrome 中未找到任何可用分頁")

async def _async_scan_cloud_quota(
    log_callback: Optional[Callable[[str], None]] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    import websockets

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    log("[1/4] 正在連接 Chrome CDP 服務 (連接埠 9222)...")
    ws_url, needs_nav = await get_photos_tab_ws_url()
    
    async with websockets.connect(ws_url, max_size=100 * 1024 * 1024) as ws:
        cdp = CdpSession(ws)
        listen_task = asyncio.create_task(cdp._listen())

        try:
            if needs_nav:
                log("[1/4] 分頁導航至 https://photos.google.com ...")
                await cdp.send("Page.navigate", {"url": "https://photos.google.com/"})
                await asyncio.sleep(5)

            log("[2/4] 正在提取 Google 相簿認證 Token (WIZ_global_data)...")
            tokens = None
            for wait_sec in range(25):
                try:
                    tokens = await cdp.evaluate("""
                        (() => {
                            const g = window.WIZ_global_data;
                            if (!g || !g.SNlM0e) return null;
                            return {
                                at: g.SNlM0e,
                                fsid: g.FdrFJe,
                                bl: g.cfb2h,
                                path: g.eptZe || '/'
                            };
                        })()
                    """)
                    if tokens and tokens.get("at"):
                        break
                except Exception:
                    pass
                await asyncio.sleep(1)

            if not tokens or not tokens.get("at"):
                err_msg = "未偵測到 Google 相簿認證資料，請確認 Chrome 已完成登入且頁面已完全載入"
                log(f"[X] {err_msg}")
                return False, err_msg, {}

            log(f"[2/4] Token 擷取成功！開始枚舉相簿清單 (RPC: lcxiM)...")

            # Phase 1: Library Enumeration (lcxiM, 500 items per page)
            all_media = []
            page_token = None
            page_num = 0
            page_size = 500

            while True:
                if stop_event and stop_event():
                    log("[!] 使用者手動終止掃描。")
                    return False, "使用者終止", {}

                page_num += 1
                token_js = f'"{page_token}"' if page_token else 'null'
                page_res = await cdp.evaluate(f"""
                    (async () => {{
                        const rpcid = 'lcxiM';
                        const reqData = [{token_js}, null, {page_size}, null, 1, 1];
                        const wrapped = [[[rpcid, JSON.stringify(reqData), null, 'generic']]];
                        const body = 'f.req=' + encodeURIComponent(JSON.stringify(wrapped)) + '&at=' + encodeURIComponent('{tokens["at"]}') + '&';
                        const params = new URLSearchParams({{
                            rpcids: rpcid,
                            'source-path': window.location.pathname,
                            'f.sid': '{tokens["fsid"]}',
                            bl: '{tokens["bl"]}',
                            pageId: 'none',
                            rt: 'c'
                        }});
                        const url = 'https://photos.google.com{tokens["path"]}data/batchexecute?' + params.toString();
                        const resp = await fetch(url, {{
                            headers: {{ 'content-type': 'application/x-www-form-urlencoded;charset=UTF-8' }},
                            body,
                            method: 'POST',
                            credentials: 'include'
                        }});
                        const text = await resp.text();
                        const lines = text.split('\\n').filter(l => l.includes('wrb.fr'));
                        if (!lines.length) return {{ error: 'No wrb.fr envelope', status: resp.status }};
                        const parsed = JSON.parse(lines[0]);
                        const payload = JSON.parse(parsed[0][2]);
                        const items = payload?.[0]?.map(item => ({{
                            mediaKey: item?.[0],
                            dedupKey: item?.[3],
                            timestamp: item?.[2]
                        }})) || [];
                        return {{ items, nextPageId: payload?.[1] || null }};
                    }})()
                """)

                if not page_res or page_res.get("error"):
                    log(f"  [!] 第 {page_num} 頁擷取中斷: {page_res.get('error') if page_res else '無回應'}")
                    break

                items = page_res.get("items", [])
                all_media.extend(items)
                log(f"  ...已檢索第 {page_num} 頁，累計掃描 {len(all_media)} 個相簿項目")

                next_page = page_res.get("nextPageId")
                if not next_page or len(items) == 0:
                    break
                page_token = next_page

            log(f"[3/4] 相簿清單檢索完畢，共 {len(all_media)} 個項目！開始批次查詢空間配額 (RPC: EWgK9e)...")

            # Phase 2: Batch check quota via EWgK9e (up to 5000 items per call)
            all_cloud_items = []
            quota_items = []
            info_batch_size = 5000
            total_batches = (len(all_media) + info_batch_size - 1) // info_batch_size if all_media else 1

            for b_idx in range(0, len(all_media), info_batch_size):
                if stop_event and stop_event():
                    log("[!] 使用者手動暫停配額檢查。")
                    break

                batch = all_media[b_idx: b_idx + info_batch_size]
                batch_keys = [it["mediaKey"] for it in batch]
                batch_num = (b_idx // info_batch_size) + 1

                batch_json = json.dumps(batch_keys)
                batch_res = await cdp.evaluate(f"""
                    (async () => {{
                        const rpcid = 'EWgK9e';
                        const mediaKeys = {batch_json};
                        const mappedKeys = mediaKeys.map(id => [id]);
                        const reqData = [[[mappedKeys], [[null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,[],null,null,null,null,null,null,null,null,null,null,[]]]]];
                        const wrapped = [[[rpcid, JSON.stringify(reqData), null, 'generic']]];
                        const body = 'f.req=' + encodeURIComponent(JSON.stringify(wrapped)) + '&at=' + encodeURIComponent('{tokens["at"]}') + '&';
                        const params = new URLSearchParams({{
                            rpcids: rpcid,
                            'source-path': window.location.pathname,
                            'f.sid': '{tokens["fsid"]}',
                            bl: '{tokens["bl"]}',
                            pageId: 'none',
                            rt: 'c'
                        }});
                        const url = 'https://photos.google.com{tokens["path"]}data/batchexecute?' + params.toString();
                        const resp = await fetch(url, {{
                            headers: {{ 'content-type': 'application/x-www-form-urlencoded;charset=UTF-8' }},
                            body,
                            method: 'POST',
                            credentials: 'include'
                        }});
                        const text = await resp.text();
                        const lines = text.split('\\n').filter(l => l.includes('wrb.fr'));
                        if (!lines.length) return {{ error: 'No wrb.fr envelope', status: resp.status }};
                        const parsed = JSON.parse(lines[0]);
                        const payload = JSON.parse(parsed[0][2]);
                        const itemsData = payload?.[0]?.[1] || [];
                        const results = itemsData.map(item => {{
                            const lastArr = item?.[1]?.at(-1);
                            return {{
                                mediaKey: item?.[0],
                                fileName: item?.[1]?.[3],
                                size: item?.[1]?.[9],
                                timestamp: item?.[1]?.[6],
                                takesUpSpace: lastArr?.[0] === 1,
                                spaceTaken: lastArr?.[1] || 0,
                                isOriginalQuality: lastArr?.[2] === 2
                            }};
                        }});
                        return {{ results }};
                    }})()
                """)

                if not batch_res or batch_res.get("error"):
                    log(f"  [X] 配額批次 #{batch_num} 查詢異常: {batch_res.get('error') if batch_res else '無回應'}")
                    continue

                res_items = batch_res.get("results", [])
                space_consuming = [it for it in res_items if it.get("takesUpSpace")]
                
                # Merge dedupKey from Phase 1
                media_to_dedup = {it["mediaKey"]: it.get("dedupKey") for it in batch}
                for item in res_items:
                    item["dedupKey"] = media_to_dedup.get(item["mediaKey"])
                    all_cloud_items.append(item)
                    if item.get("takesUpSpace"):
                        quota_items.append(item)

                log(f"  ...配額批次 #{batch_num}/{total_batches}: 檢查 {len(batch)} 個項目，抓出 {len([i for i in res_items if i.get('takesUpSpace')])} 個佔空間相片 (累計佔空間: {len(quota_items)} 個)")

            total_space_bytes = sum(it.get("spaceTaken", 0) for it in quota_items)
            total_space_gb = total_space_bytes / (1024 * 1024 * 1024)

            # 1. Save Full Cloud Ledger (both 0-quota and quota-consuming photos)
            ledger_path = get_cloud_ledger_path()
            with open(ledger_path, "w", encoding="utf-8") as f:
                json.dump({
                    "scanned_time": time.time(),
                    "total_items": len(all_cloud_items),
                    "zero_quota_count": len(all_cloud_items) - len(quota_items),
                    "quota_items_count": len(quota_items),
                    "items": all_cloud_items
                }, f, indent=2, ensure_ascii=False)

            # 2. Save Quota Manifest (for washing pipeline)
            manifest_path = get_manifest_path()
            with open(manifest_path, "w", encoding="utf-8") as f:
                json.dump({
                    "scanned_time": time.time(),
                    "total_scanned": len(all_media),
                    "quota_items_count": len(quota_items),
                    "total_space_bytes": total_space_bytes,
                    "total_space_gb": total_space_gb,
                    "items": quota_items
                }, f, indent=2, ensure_ascii=False)

            log(f"[4/4] 雲端相簿資產全量比對完成！")
            log(f"  ● Google 雲端相簿總收錄: {len(all_cloud_items)} 個項目")
            log(f"  ● 原生 0 空間免扣額原檔: {len(all_cloud_items) - len(quota_items)} 個項目")
            log(f"  ● 佔用容量需洗白項目: {len(quota_items)} 個 (合計 {total_space_gb:.2f} GB)")
            log(f"  ● 雲端總帳已存檔: {ledger_path}")

            # 3. Perform automatic reconciliation with local sync_state
            reconcile_cloud_ledger_with_sync_state(all_cloud_items, log_callback=log)

            summary = {
                "total_scanned": len(all_media),
                "zero_quota_count": len(all_cloud_items) - len(quota_items),
                "quota_count": len(quota_items),
                "total_space_gb": total_space_gb,
                "manifest_path": manifest_path,
                "ledger_path": ledger_path
            }
            return True, f"對帳完成: 雲端收錄 {len(all_cloud_items)} 個項目 (0 空間 {len(all_cloud_items) - len(quota_items)} 個，待洗白 {len(quota_items)} 個)", summary

        finally:
            listen_task.cancel()

def scan_cloud_quota(
    log_callback: Optional[Callable[[str], None]] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    try:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(_async_scan_cloud_quota(log_callback, progress_callback, stop_event))
    finally:
        loop.close()

def reconcile_cloud_ledger_with_sync_state(
    ledger_items: Optional[List[Dict[str, any]]] = None,
    log_callback: Optional[Callable[[str], None]] = None
) -> Tuple[int, int]:
    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    if ledger_items is None:
        ledger_path = get_cloud_ledger_path()
        if not os.path.exists(ledger_path):
            log("[!] 尚未建立雲端相簿總帳，請先執行『與 Google 相簿比對對帳』！")
            return 0, 0
        try:
            with open(ledger_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                ledger_items = data.get("items", [])
        except Exception as e:
            log(f"[X] 讀取雲端總帳失敗: {e}")
            return 0, 0

    from core_sync import load_state, save_state, get_vault_dir

    cloud_zero_quota_names = {it["fileName"] for it in ledger_items if it.get("fileName") and not it.get("takesUpSpace")}

    state = load_state()
    pushed = state.get("pushed_to_pixel", {})
    if isinstance(pushed, list):
        pushed = {k: {"pushed_time": 0} for k in pushed if isinstance(k, str)}
    elif not isinstance(pushed, dict):
        pushed = {}
    old_pushed_len = len(pushed)

    vault_dir = get_vault_dir()
    vault_files = set(os.listdir(vault_dir)) if os.path.exists(vault_dir) else set()
    already_in_cloud = vault_files.intersection(cloud_zero_quota_names)

    for name in cloud_zero_quota_names:
        if name not in pushed:
            pushed[name] = {"pushed_time": time.time(), "cloud_reconciled": True}
    state["pushed_to_pixel"] = pushed
    save_state(state)

    new_marked = len(pushed) - old_pushed_len
    log(f"[對帳完成] 雲端-本地相簿比對登記完畢：")
    log(f"  ● 雲端總計 0 空間免費照片: {len(cloud_zero_quota_names)} 張")
    log(f"  ● 電腦緩衝庫符合已備份: {len(already_in_cloud)} 張 (全數自動登錄，絕不重複送入 Pixel)")
    log(f"  ● 本地 state 新增登記: {new_marked} 筆")
    return len(already_in_cloud), new_marked

# ==========================================
# 階段 2：自動下載真正佔用空間之雲端原檔至電腦緩衝庫
# ==========================================
async def _async_download_quota_items(
    manifest_items: List[Dict[str, any]],
    vault_dir: str,
    log_callback: Optional[Callable[[str], None]] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    import websockets

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    ws_url, _ = await get_photos_tab_ws_url()
    async with websockets.connect(ws_url, max_size=100 * 1024 * 1024) as ws:
        cdp = CdpSession(ws)
        listen_task = asyncio.create_task(cdp._listen())

        try:
            tokens = await cdp.evaluate("""
                (() => {
                    const g = window.WIZ_global_data;
                    return { at: g.SNlM0e, fsid: g.FdrFJe, bl: g.cfb2h, path: g.eptZe };
                })()
            """)

            cookies_res = await cdp.send("Network.getCookies")
            cookie_header = "; ".join([f"{c['name']}={c['value']}" for c in cookies_res.get("cookies", []) if "name" in c and "value" in c])

            success_cnt = 0
            fail_cnt = 0
            total_items = len(manifest_items)

            # Keep track of used filenames to avoid collisions
            used_paths = {}

            for idx, item in enumerate(manifest_items, 1):
                if stop_event and stop_event():
                    log("[!] 使用者手動暫停下載。")
                    break

                media_key = item["mediaKey"]
                filename = item.get("fileName") or f"{media_key}.jpg"
                base_name, ext = os.path.splitext(filename)
                
                dest_path = os.path.join(vault_dir, filename)
                if dest_path in used_paths and used_paths[dest_path] != media_key:
                    dest_path = os.path.join(vault_dir, f"{base_name}_{media_key[:8]}{ext}")
                used_paths[dest_path] = media_key

                # Skip if already exists on disk with full size (> 10KB)
                if os.path.isfile(dest_path) and os.path.getsize(dest_path) > 1024:
                    item["downloaded"] = True
                    item["local_path"] = dest_path
                    success_cnt += 1
                    continue

                if progress_callback:
                    progress_callback(idx, total_items, filename, f"正在下載雲端扣額相片 ({idx}/{total_items})...")

                # Fetch signed download url via pLFTfd
                dl_res = await cdp.evaluate(f"""
                    (async () => {{
                        const rpcid = 'pLFTfd';
                        const reqData = [["{media_key}"], [1]];
                        const wrapped = [[[rpcid, JSON.stringify(reqData), null, 'generic']]];
                        const body = 'f.req=' + encodeURIComponent(JSON.stringify(wrapped)) + '&at=' + encodeURIComponent('{tokens["at"]}') + '&';
                        const params = new URLSearchParams({{
                            rpcids: rpcid,
                            'source-path': window.location.pathname,
                            'f.sid': '{tokens["fsid"]}',
                            bl: '{tokens["bl"]}',
                            pageId: 'none',
                            rt: 'c'
                        }});
                        const url = 'https://photos.google.com{tokens["path"]}data/batchexecute?' + params.toString();
                        const resp = await fetch(url, {{
                            headers: {{ 'content-type': 'application/x-www-form-urlencoded;charset=UTF-8' }},
                            body,
                            method: 'POST',
                            credentials: 'include'
                        }});
                        const text = await resp.text();
                        const lines = text.split('\\n').filter(l => l.includes('wrb.fr'));
                        if (!lines.length) return {{ error: 'No wrb.fr envelope' }};
                        const parsed = JSON.parse(lines[0]);
                        const payload = JSON.parse(parsed[0][2]);

                        const findUrl = (obj, depth = 0) => {{
                            if (depth > 8 || !obj) return null;
                            if (typeof obj === 'string' && (obj.startsWith('https://') || obj.startsWith('http://'))) return obj;
                            if (Array.isArray(obj)) {{
                                for (const item of obj) {{
                                    const found = findUrl(item, depth + 1);
                                    if (found) return found;
                                }}
                            }}
                            return null;
                        }};
                        const dlUrl = findUrl(payload);
                        return {{ dlUrl }};
                    }})()
                """)

                dl_url = dl_res.get("dlUrl") if (dl_res and isinstance(dl_res, dict)) else None
                if not dl_url or not isinstance(dl_url, str) or not dl_url.startswith("http"):
                    log(f"  [X] 無法取得有效下載連結: {filename}")
                    fail_cnt += 1
                    continue

                # Download file
                try:
                    req = urllib.request.Request(dl_url, headers={
                        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                        "Cookie": cookie_header
                    })
                    part_path = dest_path + ".part"
                    with urllib.request.urlopen(req, timeout=60) as resp, open(part_path, "wb") as f:
                        while chunk := resp.read(1024 * 1024):
                            f.write(chunk)
                    if os.path.exists(dest_path):
                        os.remove(dest_path)
                    os.rename(part_path, dest_path)

                    item["downloaded"] = True
                    item["local_path"] = dest_path
                    success_cnt += 1
                    file_mb = os.path.getsize(dest_path) / (1024 * 1024)
                    log(f"  [{idx}/{total_items}] 下載完成: {filename} ({file_mb:.2f} MB)")
                except Exception as e:
                    log(f"  [X] 下載失敗 {filename}: {e}")
                    fail_cnt += 1

                # Save progress every 50 items
                if idx % 50 == 0:
                    manifest_path = get_manifest_path()
                    try:
                        with open(manifest_path, "w", encoding="utf-8") as f:
                            json.dump({"items": manifest_items}, f, indent=2, ensure_ascii=False)
                    except Exception:
                        pass

                await asyncio.sleep(0.1)

            return True, f"下載完畢: 成功 {success_cnt} 張，失敗 {fail_cnt} 張", {"downloaded": success_cnt, "failed": fail_cnt}
        finally:
            listen_task.cancel()

def download_quota_items(
    manifest_items: List[Dict[str, any]],
    vault_dir: str,
    log_callback: Optional[Callable[[str], None]] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    try:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(_async_download_quota_items(manifest_items, vault_dir, log_callback, progress_callback, stop_event))
    finally:
        loop.close()

# ==========================================
# 階段 3：安全將雲端扣額相片移入垃圾桶 (釋出配額)
# ==========================================
async def _async_trash_quota_items(
    manifest_items: List[Dict[str, any]],
    vault_dir: str,
    log_callback: Optional[Callable[[str], None]] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    import websockets

    def log(msg: str):
        if log_callback: log_callback(msg)
        else: print(msg)

    # STRICT SANITY CHECK: Every target MUST have verified local copy and not already trashed!
    verified_targets = []
    for it in manifest_items:
        if it.get("trashed"):
            continue
        fname = it.get("fileName")
        lpath = it.get("local_path") or os.path.join(vault_dir, fname)
        if os.path.isfile(lpath) and os.path.getsize(lpath) > 1024:
            verified_targets.append(it)
        else:
            log(f"  [保護攔截] 略過 {fname}: 電腦本地無實體檔案，嚴禁移入垃圾桶！")

    if not verified_targets:
        already_trashed = len([it for it in manifest_items if it.get("trashed")])
        if already_trashed > 0:
            return True, f"所有扣額相片皆已移入垃圾桶 (共 {already_trashed} 張)", {"trashed": already_trashed, "failed": 0}
        return False, "無任何通過本機實體驗證的刪除目標", {}

    success_cnt = 0
    fail_cnt = 0
    total_items = len(verified_targets)
    batch_size = 20

    async def get_fresh_cdp():
        ws_url, _ = await get_photos_tab_ws_url()
        ws = await websockets.connect(ws_url, max_size=100 * 1024 * 1024)
        cdp = CdpSession(ws)
        task = asyncio.create_task(cdp._listen())
        tokens = await cdp.evaluate("""
            (() => {
                const g = window.WIZ_global_data || {};
                return { at: g.SNlM0e, fsid: g.FdrFJe, bl: g.cfb2h, path: g.eptZe || '/' };
            })()
        """)
        return ws, cdp, task, tokens

    ws, cdp, listen_task, tokens = await get_fresh_cdp()

    try:
        for i in range(0, total_items, batch_size):
            if stop_event and stop_event():
                log("[!] 使用者手動暫停移入垃圾桶。")
                break

            batch = verified_targets[i: i + batch_size]
            dedup_keys = [it["dedupKey"] for it in batch if it.get("dedupKey")]
            if not dedup_keys:
                continue

            if progress_callback:
                progress_callback(i + len(batch), total_items, f"{len(batch)} 張照片", f"正在移至雲端垃圾桶 ({i+1}~{min(i+len(batch), total_items)}/{total_items})...")

            trash_json = json.dumps(dedup_keys)
            trash_script = f"""
                (async () => {{
                    const rpcid = 'XwAOJf';
                    const reqData = [null, 1, {trash_json}, 3];
                    const wrapped = [[[rpcid, JSON.stringify(reqData), null, 'generic']]];
                    const body = 'f.req=' + encodeURIComponent(JSON.stringify(wrapped)) + '&at=' + encodeURIComponent('{tokens["at"]}') + '&';
                    const params = new URLSearchParams({{
                        rpcids: rpcid,
                        'source-path': window.location.pathname,
                        'f.sid': '{tokens["fsid"]}',
                        bl: '{tokens["bl"]}',
                        pageId: 'none',
                        rt: 'c'
                    }});
                    const url = 'https://photos.google.com{tokens["path"]}data/batchexecute?' + params.toString();
                    const resp = await fetch(url, {{
                        headers: {{ 'content-type': 'application/x-www-form-urlencoded;charset=UTF-8' }},
                        body,
                        method: 'POST',
                        credentials: 'include'
                    }});
                    return {{ status: resp.status, ok: resp.ok }};
                }})()
            """

            # Robust evaluate with auto-reconnect on navigation/disconnection
            trash_res = None
            for attempt in range(3):
                try:
                    trash_res = await cdp.evaluate(trash_script)
                    break
                except Exception as eval_err:
                    err_text = str(eval_err)
                    if "navigated or closed" in err_text or "closed" in err_text or "Connection" in err_text:
                        log(f"  [連線重置] 偵測到分頁跳轉，正在自動重新建立連線 (第 {attempt+1}/3 次)...")
                        await asyncio.sleep(1.0)
                        listen_task.cancel()
                        await ws.close()
                        ws, cdp, listen_task, tokens = await get_fresh_cdp()
                    else:
                        log(f"  [X] RPC 執行錯誤: {eval_err}")
                        break

            if trash_res and trash_res.get("ok"):
                success_cnt += len(batch)
                for b_it in batch:
                    b_it["trashed"] = True
                log(f"  [OK] 批次移入垃圾桶成功: {len(batch)} 張 (累計: {success_cnt}/{total_items})")
            else:
                fail_cnt += len(batch)
                log(f"  [X] 移入垃圾桶失敗: status={trash_res.get('status') if trash_res else '無回應'}")

            # Save progress periodically
            if (i // batch_size) % 10 == 0:
                manifest_path = get_manifest_path()
                try:
                    with open(manifest_path, "w", encoding="utf-8") as f:
                        json.dump({"items": manifest_items}, f, indent=2, ensure_ascii=False)
                except Exception:
                    pass

            await asyncio.sleep(0.3)

        # Update manifest after loop ends
        manifest_path = get_manifest_path()
        if os.path.exists(manifest_path):
            with open(manifest_path, "w", encoding="utf-8") as f:
                json.dump({"items": manifest_items}, f, indent=2, ensure_ascii=False)

        log(f"[完成] 雲端垃圾桶移入完成！成功 {success_cnt} 張，失敗 {fail_cnt} 張。")
        log("💡 提醒: 請前往 Google 相簿『垃圾桶』點擊『清空垃圾桶』，Google 儲存空間將立即歸零釋放！")
        return True, f"移入垃圾桶完成 (成功 {success_cnt} 張)", {"trashed": success_cnt, "failed": fail_cnt}

    finally:
        listen_task.cancel()
        try:
            await ws.close()
        except Exception:
            pass

def trash_quota_items(
    manifest_items: List[Dict[str, any]],
    vault_dir: str,
    log_callback: Optional[Callable[[str], None]] = None,
    progress_callback: Optional[Callable[[int, int, str, str], None]] = None,
    stop_event: Optional[Callable[[], bool]] = None
) -> Tuple[bool, str, Dict[str, any]]:
    try:
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        return loop.run_until_complete(_async_trash_quota_items(manifest_items, vault_dir, log_callback, progress_callback, stop_event))
    finally:
        loop.close()
