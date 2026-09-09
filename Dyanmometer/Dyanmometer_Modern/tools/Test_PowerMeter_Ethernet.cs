using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DynamometerTools
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.Default;
            Console.WriteLine("===============================================================");
            Console.WriteLine("  [橫河 YOKOGAWA WT333E 乙太網路連線測試工具 - 相容 XP/7/10/11 版]");
            Console.WriteLine("===============================================================");

            string ip = "192.168.0.11";
            int port = 10001;

            if (args.Length >= 1) ip = args[0];
            if (args.Length >= 2) int.TryParse(args[1], out port);

            Console.Write(string.Format("\n請輸入 WT333E 功率計 IP 位址 [現場預設 {0}]: ", ip));
            string inputIp = Console.ReadLine();
            if (!string.IsNullOrEmpty(inputIp) && inputIp.Trim().Length > 0) ip = inputIp.Trim();

            Console.Write(string.Format("請輸入 TCP 連接埠 [預設 {0}，或輸入 scan 進行自動掃描]: ", port));
            string inputPort = Console.ReadLine();
            if (!string.IsNullOrEmpty(inputPort))
            {
                if (inputPort.Trim().ToLower() == "scan")
                {
                    Console.WriteLine("\n>> 正在自動掃描 WT333E 的可用通訊埠 (10001, 51064, 51065, 111, 80)...");
                    int[] testPorts = new int[] { 10001, 51064, 51065, 111, 80 };
                    foreach (int tp in testPorts)
                    {
                        try
                        {
                            using (TcpClient tc = new TcpClient())
                            {
                                IAsyncResult ar = tc.BeginConnect(ip, tp, null, null);
                                if (ar.AsyncWaitHandle.WaitOne(400) && tc.Connected)
                                {
                                    Console.WriteLine(string.Format("  [V] 找到開放埠 Port {0}！", tp));
                                    port = tp;
                                    break;
                                }
                                else
                                {
                                    Console.WriteLine(string.Format("  [X] Port {0} 關閉", tp));
                                }
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    int p;
                    if (int.TryParse(inputPort.Trim(), out p)) port = p;
                }
            }

            Console.WriteLine(string.Format("\n>> 正在連接 WT333E {0}:{1} ...", ip, port));
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(ip, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3));

                    if (!success || !client.Connected)
                    {
                        Console.WriteLine(string.Format("[錯誤] 連線失敗：無法連接至 {0}:{1}", ip, port));
                        Console.WriteLine(">> 排除建議：請在 WT333E 面板按 [UTILITY] -> [Network] -> [Server] 將 Socket Server 設為 ON。");
                        return;
                    }

                    client.EndConnect(result);
                    Console.WriteLine(">> 網路連線成功！");

                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = 3000;
                        stream.WriteTimeout = 3000;

                        byte[] idnCmd = Encoding.ASCII.GetBytes("*IDN?\n");
                        stream.Write(idnCmd, 0, idnCmd.Length);
                        Thread.Sleep(150);

                        byte[] buf = new byte[2048];
                        if (stream.DataAvailable)
                        {
                            int read = stream.Read(buf, 0, buf.Length);
                            string idn = Encoding.ASCII.GetString(buf, 0, read).Trim();
                            Console.WriteLine(string.Format(">> 儀表識別碼 (*IDN?): {0}", idn));
                        }

                        Console.WriteLine("\n>> 開始讀取三相電氣數據 (:NUMeric:NORMal:VALue?)，按 Ctrl+C 可停止...");
                        Console.WriteLine(new string('-', 75));

                        int count = 1;
                        while (true)
                        {
                            byte[] valCmd = Encoding.ASCII.GetBytes(":NUMeric:NORMal:VALue?\n");
                            stream.Write(valCmd, 0, valCmd.Length);
                            Thread.Sleep(100);

                            if (stream.DataAvailable)
                            {
                                int read = stream.Read(buf, 0, buf.Length);
                                string resp = Encoding.ASCII.GetString(buf, 0, read).Trim();
                                Console.WriteLine(string.Format("[{0:D3}] 數據回應: {1}", count, resp));
                            }
                            count++;
                            Thread.Sleep(500);
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
                Console.WriteLine("\n>> 測試已結束。按任意鍵退出...");
                Console.ReadKey();
            }
        }
    }
}
