using System;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DynamometerTools
{
    class TorqueMeterTester
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.Default;

            Console.WriteLine("===============================================================");
            Console.WriteLine("  [KISTLER 4700B 扭力感測器 - 原廠官方 MENU:DISP? 協議直讀測試]");
            Console.WriteLine("===============================================================");

            string selectedPort = "COM4";
            int baud = 1000000;

            Console.WriteLine(string.Format("\n>> 正在開啟 {0} @ {1} bps (8-N-1)...", selectedPort, baud));

            try
            {
                using (SerialPort sp = new SerialPort(selectedPort, baud, Parity.None, 8, StopBits.One))
                {
                    sp.ReadTimeout = 1000;
                    sp.WriteTimeout = 1000;
                    sp.Open();
                    Console.WriteLine(">> 串列埠開啟成功！開始以原廠 MENU:DISP? 指令輪詢，按 Ctrl+C 可停止...");
                    Console.WriteLine(new string('-', 75));
                    Console.WriteLine(string.Format("{0,6} | {1,14} | {2,12} | {3,12} | {4,-20}", "次數", "實測轉矩(Nm)", "實測轉速(rpm)", "功率(kW)", "狀態"));
                    Console.WriteLine(new string('-', 75));

                    int count = 1;
                    while (true)
                    {
                        sp.DiscardInBuffer();
                        // 原廠 SensorTool 發送之標準查詢指令
                        byte[] cmd = Encoding.ASCII.GetBytes("MENU:DISP?\r");
                        sp.Write(cmd, 0, cmd.Length);
                        Thread.Sleep(80);

                        try
                        {
                            string line = sp.ReadLine().Trim();
                            if (!string.IsNullOrEmpty(line))
                            {
                                // 解碼 Hex 字串
                                string decodedText = DecodeHexToAscii(line);

                                double torque = 0.0;
                                double speed = 0.0;
                                double power = 0.0;

                                Match mTorq = Regex.Match(decodedText, @"Torque\s+([-\+]?\d+(\.\d+)?)");
                                Match mSpd = Regex.Match(decodedText, @"Speed\s+([-\+]?\d+(\.\d+)?)");
                                Match mPwr = Regex.Match(decodedText, @"Power\s+([-\+]?\d+(\.\d+)?)");

                                if (mTorq.Success) double.TryParse(mTorq.Groups[1].Value, out torque);
                                if (mSpd.Success) double.TryParse(mSpd.Groups[1].Value, out speed);
                                if (mPwr.Success) double.TryParse(mPwr.Groups[1].Value, out power);

                                Console.WriteLine(string.Format("[{0:D3}]  | {1,12:F3} Nm | {2,10:F0} rpm | {3,10:F3} kW | {4,-20}", count, torque, speed, power, "成功取得數據"));
                            }
                        }
                        catch (TimeoutException)
                        {
                            Console.WriteLine(string.Format("[{0:D3}]  | 等待回應逾時...", count));
                        }

                        count++;
                        Thread.Sleep(200);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("\n[錯誤] 無法開啟串列埠 {0}: {1}", selectedPort, ex.Message));
            }
            finally
            {
                Console.WriteLine("\n>> 測試結束。按任意鍵退出...");
                Console.ReadKey();
            }
        }

        private static string DecodeHexToAscii(string hex)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hex.Length - 1; i += 2)
                {
                    string hs = hex.Substring(i, 2);
                    if (hs.Equals("FF", StringComparison.OrdinalIgnoreCase)) break;
                    byte b = Convert.ToByte(hs, 16);
                    sb.Append((char)b);
                }
                return sb.ToString();
            }
            catch
            {
                return hex;
            }
        }
    }
}
