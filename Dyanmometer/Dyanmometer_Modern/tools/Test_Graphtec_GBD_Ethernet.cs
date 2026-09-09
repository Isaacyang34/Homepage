using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DynamometerTools
{
    class GbdLoggerTester
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.Default;
            Console.WriteLine("===============================================================");
            Console.WriteLine("  [Graphtec GL820 溫度記錄器測試工具 - 相容 XP/7/10/11 版]");
            Console.WriteLine("===============================================================");

            string ip = "192.168.0.3";
            int port = 8023;

            if (args.Length >= 1) ip = args[0];
            if (args.Length >= 2) int.TryParse(args[1], out port);

            Console.Write(string.Format("\n請輸入 GL820 記錄器 IP 位址 [現場預設 {0}]: ", ip));
            string inputIp = Console.ReadLine();
            if (!string.IsNullOrEmpty(inputIp) && inputIp.Trim().Length > 0) ip = inputIp.Trim();

            Console.Write(string.Format("請輸入 TCP 連接埠 [預設 {0}]: ", port));
            string inputPort = Console.ReadLine();
            if (!string.IsNullOrEmpty(inputPort))
            {
                int p;
                if (int.TryParse(inputPort.Trim(), out p)) port = p;
            }

            Console.WriteLine(string.Format("\n>> 正在連接 GBD 溫度記錄器 {0}:{1} ...", ip, port));
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(ip, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));

                    if (!success || !client.Connected)
                    {
                        Console.WriteLine(string.Format("[錯誤] 連線失敗：無法連接至 {0}:{1}", ip, port));
                        return;
                    }

                    client.EndConnect(result);
                    Console.WriteLine(">> 網路連線成功！");

                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 2000;
                        stream.WriteTimeout = 2000;

                        // 查詢設備型號
                        byte[] idnCmd = Encoding.ASCII.GetBytes("*IDN?\r\n");
                        stream.Write(idnCmd, 0, idnCmd.Length);
                        Thread.Sleep(150);

                        byte[] buf = new byte[2048];
                        if (stream.DataAvailable)
                        {
                            int read = stream.Read(buf, 0, buf.Length);
                            string idn = Encoding.ASCII.GetString(buf, 0, read).Trim();
                            Console.WriteLine(string.Format(">> 記錄器型號識別 (*IDN?): {0}", idn));
                        }

                        // 測試取樣指令 (使用 GL820 支援之 :DATA:MEAS?)
                        string queryCmd = ":DATA:MEAS?\r\n";

                        Console.WriteLine(string.Format("\n>> 開始每秒讀取一次多通道溫度 ({0})，按 Ctrl+C 可停止...", queryCmd.Trim()));
                        Console.WriteLine(new string('-', 75));

                        int count = 1;
                        while (true)
                        {
                            byte[] valCmd = Encoding.ASCII.GetBytes(queryCmd);
                            stream.Write(valCmd, 0, valCmd.Length);
                            Thread.Sleep(120);

                            if (stream.DataAvailable)
                            {
                                int read = stream.Read(buf, 0, buf.Length);
                                string resp = Encoding.ASCII.GetString(buf, 0, read).Trim();
                                Console.WriteLine(string.Format("[{0:D3}] 數據: {1}", count, resp));
                            }
                            else
                            {
                                Console.WriteLine(string.Format("[{0:D3}] 等待取樣數據回應中...", count));
                            }
                            count++;
                            Thread.Sleep(1000);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("\n[錯誤] 發生異常: {0}", ex.Message));
            }
            finally
            {
                Console.WriteLine("\n>> 測試結束。按任意鍵退出...");
                Console.ReadKey();
            }
        }
    }
}
