#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ETF Portfolio Studio 本機即時數據代理伺服器
提供全市場任意代號即時線上爬蟲查詢 API 與網頁託管
"""

import http.server
import socketserver
import json
import urllib.parse
import os
import sys
import threading
import time
import webbrowser

# 強制 Windows 主控台使用 UTF-8 輸出，杜絕任何終端機亂碼
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

# 匯入自定義抓取模組
from sync_live_data import fetch_single_etf

PORT = 8080
DIRECTORY = os.path.dirname(os.path.abspath(__file__))

class ETFRequestHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)

    def log_message(self, format, *args):
        # 僅過濾靜態資源日誌，保持終端畫面乾淨專注
        if '/api/' in (args[0] if args else ''):
            sys.stdout.write(f"[{time.strftime('%H:%M:%S')}] {format % args}\n")

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        
        # 1. 即時線上查詢單檔 ETF: /api/fetch?code=00915
        if parsed.path == '/api/fetch':
            query = urllib.parse.parse_qs(parsed.query)
            code = query.get('code', [''])[0].strip()
            
            if not code:
                self.send_response(400)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.end_headers()
                self.wfile.write(json.dumps({'error': '請提供代號 (code)'}).encode('utf-8'))
                return
            
            print(f"[*] 收到線上即時行情查詢: {code} ...", flush=True)
            data = fetch_single_etf(code)
            
            if data:
                self.send_response(200)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(json.dumps({'status': 'success', 'data': data}, ensure_ascii=False).encode('utf-8'))
                print(f"[+] 成功獲取 {code}: 市價 NT$ {data['price']} | 實測殖利率 {data['yield_pct']}%", flush=True)
            else:
                self.send_response(404)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(json.dumps({'status': 'error', 'message': f'查無此代號 {code} 之市場數據'}).encode('utf-8'))
                print(f"[-] 查無代號 {code} 之數據", flush=True)
            return

        # 2. 獲取全市場最新快取庫: /api/cache
        if parsed.path == '/api/cache':
            cache_file = os.path.join(DIRECTORY, 'etf_live_cache.json')
            if os.path.exists(cache_file):
                with open(cache_file, 'r', encoding='utf-8') as f:
                    content = f.read()
                self.send_response(200)
                self.send_header('Content-Type', 'application/json; charset=utf-8')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(content.encode('utf-8'))
            else:
                self.send_response(404)
                self.end_headers()
            return

        # 靜態檔案正常提供 (首頁預設重定向到 index.html)
        if parsed.path == '/':
            self.path = '/index.html'
            
        super().do_GET()

def open_browser_later(url):
    time.sleep(0.8)
    try:
        webbrowser.open(url)
    except Exception:
        pass

def run_server():
    socketserver.TCPServer.allow_reuse_address = True
    
    server_port = PORT
    httpd = None
    for p in range(PORT, PORT + 10):
        try:
            httpd = socketserver.TCPServer(("", p), ETFRequestHandler)
            server_port = p
            break
        except OSError:
            continue

    if not httpd:
        print("[!] 錯誤：無法綁定連接埠 8080~8089，請檢查是否有其他程式佔用。")
        return

    url = f"http://localhost:{server_port}/index.html"
    print("=" * 68)
    print(f"  ETF Portfolio Studio 即時金融數據微服務已就緒！")
    print(f"  網頁介面 : {url}")
    print(f"  即時 API : http://localhost:{server_port}/api/fetch?code=00915")
    print(f"  狀態說明 : 終端保持開啟即可隨時進行任意標的線上即時抓取")
    print(f"  關閉方式 : 直接關閉本視窗或按 Ctrl+C")
    print("=" * 68)
    print("正在開啟瀏覽器...", flush=True)

    # 在背景延遲開啟瀏覽器
    threading.Thread(target=open_browser_later, args=(url,), daemon=True).start()

    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n[!] 正在關閉伺服器...")
        httpd.server_close()

if __name__ == '__main__':
    run_server()
