using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DynamometerHMI
{
    /// <summary>
    /// 提供多網卡環境（如有線乙太網 + Wi-Fi 雙網卡）下的智慧網卡綁定與路由輔助功能。
    /// 解決當連上 Wi-Fi (網際網路) 時，對內實體設備 (192.168.0.x) 的連線請求被系統預設閘道誤轉向 Wi-Fi 的衝突問題。
    /// </summary>
    public static class NetworkHelper
    {
        /// <summary>
        /// 智慧建立綁定本地適當網卡 IP 的 TcpClient。
        /// 當電腦同時開啟 Wi-Fi (連外網) 與實體有線乙太網孔 (連接電表 WT333E、溫度計 GL820 等) 時，
        /// 自動探測與目標 IP 同網段之本地網卡進行 IPEndPoint 綁定，徹底杜絕雙網卡路由衝突。
        /// </summary>
        /// <param name="targetIpStr">目標設備 IP (如 "192.168.0.11" 或 "192.168.0.3")</param>
        /// <returns>已綁定本機適當 IP 端點的 TcpClient，若無符合者則返回預設 TcpClient</returns>
        public static TcpClient CreateBoundTcpClient(string targetIpStr)
        {
            try
            {
                IPAddress targetAddr;
                if (IPAddress.TryParse(targetIpStr, out targetAddr) && targetAddr.AddressFamily == AddressFamily.InterNetwork)
                {
                    byte[] targetBytes = targetAddr.GetAddressBytes();
                    IPAddress bestMatch = null;

                    foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                        IPInterfaceProperties ipProps = nic.GetIPProperties();
                        foreach (UnicastIPAddressInformation uni in ipProps.UnicastAddresses)
                        {
                            if (uni.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                            byte[] localBytes = uni.Address.GetAddressBytes();
                            byte[] maskBytes = uni.IPv4Mask != null ? uni.IPv4Mask.GetAddressBytes() : new byte[] { 255, 255, 255, 0 };

                            // 1. 比對是否處於同一個子網 (Subnet Match)
                            bool match = true;
                            for (int i = 0; i < 4; i++)
                            {
                                if ((targetBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                                {
                                    match = false;
                                    break;
                                }
                            }

                            // 2. 備援：若子網遮罩異常但前 3 碼完全吻合 (Class C /24: 192.168.0.x)
                            if (!match && localBytes[0] == targetBytes[0] && localBytes[1] == targetBytes[1] && localBytes[2] == targetBytes[2])
                            {
                                match = true;
                            }

                            if (match)
                            {
                                // 若為 Ethernet (實體有線網卡)，優先度最高，立刻鎖定綁定
                                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                                {
                                    return new TcpClient(new IPEndPoint(uni.Address, 0));
                                }
                                if (bestMatch == null)
                                {
                                    bestMatch = uni.Address;
                                }
                            }
                        }
                    }

                    if (bestMatch != null)
                    {
                        return new TcpClient(new IPEndPoint(bestMatch, 0));
                    }
                }
            }
            catch
            {
                // 發生任何異常時平滑退回系統預設路由分配
            }
            return new TcpClient();
        }
    }
}
