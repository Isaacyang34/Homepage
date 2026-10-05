import http.server
import socketserver
import sys
import threading
import time
import webbrowser

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

PORT = 8088
URL = f"http://localhost:{PORT}/"

class QuietHTTPRequestHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, format, *args):
        sys.stdout.write(f"[{self.log_date_time_string()}] {args[0]} {args[1]} -> {args[2]}\n")

def open_browser():
    time.sleep(1.2)
    print(f"[*] 正在開啟預設瀏覽器: {URL}")
    webbrowser.open(URL)

def main():
    print("=" * 65)
    print("  🏸 羽樂排 (ShuttleJoy) 羽球即時排隊與球局調度系統 v1.0")
    print("  🏆 由 [羽球狂熱份子] 打造")
    print(f"  📌 本地服務網址: {URL}")
    print("=" * 65)

    threading.Thread(target=open_browser, daemon=True).start()

    with socketserver.TCPServer(("", PORT), QuietHTTPRequestHandler) as httpd:
        print(f"[+] 伺服器已在連接埠 {PORT} 成功啟動，按下 Ctrl+C 即可終止。")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\n[-] 伺服器已停止。")

if __name__ == "__main__":
    main()
