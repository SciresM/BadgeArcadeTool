using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace BadgeArcadeTool
{
    public class NetworkUtils
    {
        public static IPAddress crypto_ip = new IPAddress(new byte[] { 192, 168, 1, 137 });
        public static int crypto_port = 8081;
        public static ProgressBar progress;
        public static byte[] download_data;
        public static long boss_size;
        public static SocketException sex;
        public static WebException wex;
        public static Exception ex;
        

        public static void SetCryptoIPAddress(IPAddress crypto_ip_arg = default(IPAddress))
        {
            if (crypto_ip_arg != default(IPAddress))
                crypto_ip = crypto_ip_arg;
        }

        private static void DownloadProgressCallback(object sender, DownloadProgressChangedEventArgs e)
        {
            progress.Report((double) e.BytesReceived/boss_size);
        }

        public static byte[] TryDownload(string file)
        {
            using (progress = new ProgressBar())
            {
                wex = null;
                try
                {
                    //Sometimes the server refuses to disclose the file size, so progress bar shows as 0% until complete.
                    //So, instead, lets extract the boss file size from the boss header.
                    var header = DownloadFirstBytes(file);
                    if (header == null || BitConverter.ToUInt64(header,0) != 0x0100010073736F62 ) return null;
                    boss_size = header[8] << 24 | header[9] << 16 | header[10] << 8 | header[11];
                   

                    var client = new WebClient();
                    client.DownloadProgressChanged += DownloadProgressCallback;
                    var dataTask = client.DownloadDataTaskAsync(file);
                    while (!dataTask.IsCompleted)
                    {
                        if (dataTask.IsFaulted) return null;
                        Thread.Sleep(20);
                    }
                    return dataTask.Result;
                }
                catch (WebException e)
                {
                    wex = e;
                    return null;
                }
            }
        }

        public static byte[] DownloadFirstBytes(string file)
        {
            wex = null;
            const int bytes = 0x400;
            try
            {
                var req = (HttpWebRequest) WebRequest.Create(file);
                req.AddRange(0, bytes - 1);

                using (var resp = req.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    var buf = new byte[bytes];
                    var read = stream.Read(buf, 0, bytes);
                    Array.Resize(ref buf, read);
                    return buf;
                }
            }
            catch (WebException e)
            {
                wex = e;
                return null;
            }
        }

        private static byte[] DecryptData(byte[] metadata, byte[] data, int ofs)
        {
            byte[] dec = null;
            sex = null;
            ex = null;
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                sock.Connect(crypto_ip, crypto_port);
                sock.Send(metadata);

                var _bufsize = new byte[4];
                sock.Receive(_bufsize);

                var bufsize = BitConverter.ToInt32(_bufsize, 0);
                sock.ReceiveBufferSize = bufsize;
                sock.SendBufferSize = bufsize;

                dec = new byte[data.Length];
                data.CopyTo(dec, 0);
                using (progress = new ProgressBar())
                {
                    while (ofs < data.Length)
                    {
                        var buf = new byte[ofs + bufsize < data.Length ? bufsize : data.Length - ofs];
                        Array.Copy(data, ofs, buf, 0, buf.Length);
                        try
                        {
                            var s = sock.Send(buf);
                            var r = buf.Length;
                            while (r > 0)
                            {
                                var d = sock.Receive(buf);
                                r -= d;
                                Array.Copy(buf, 0, dec, ofs, d);
                                ofs += d;
                                Array.Resize(ref buf, r);
                                progress.Report((double)ofs / data.Length);
                            }
                        }
                        catch (SocketException e)
                        {
                            sex = e;
                            sock.Close();
                            return null;
                        }
                    }
                }

                sock.Send(BitConverter.GetBytes(0xDEADCAFE));
            }
            catch (Exception e)
            {
                ex = e;
            }
            sock.Close();
            return dec;
        }

        public enum CryptoMode
        {
            CBC_Enc,
            CBC_Dec,
            CTR_Enc,
            CTR_Dec,
            CCM_Enc,
            CCM_Dec
        }

        public enum PSPXI_AES
        {
            ClCertA = 0,
            UDS_WLAN,
            MiiQR,
            BOSS,
            Unknown,
            DownloadPlay,
            StreetPass,
            //Invalid = 7,
            Friends = 8,
            NFC
        }

        public static byte[] TryDecryptData(byte[] data, CryptoMode mode, PSPXI_AES pspxi, byte[] iv, int ofs = 0)
        {
            //return TryDecryptData(data, mode, (int) pspxi, iv, ofs);
            var metadata = new byte[1024];
            BitConverter.GetBytes(0xCAFEBABE).CopyTo(metadata, 0);
            BitConverter.GetBytes(data.Length - ofs).CopyTo(metadata, 4);
            BitConverter.GetBytes((int)pspxi).CopyTo(metadata, 8);
            BitConverter.GetBytes((int)mode).CopyTo(metadata, 0x0C);
            iv.CopyTo(metadata, 0x20);
            return DecryptData(metadata, data, ofs);
        }

        public static byte[] TryDecryptData(byte[] data, CryptoMode mode, int keyslot, byte[] iv, int ofs = 0, byte[] keyY = null)
        {
            var metadata = new byte[1024];
            BitConverter.GetBytes(0xCAFEBABE).CopyTo(metadata, 0);
            BitConverter.GetBytes(data.Length - ofs).CopyTo(metadata, 4);
            BitConverter.GetBytes((keyslot & 0x3F) | (keyY != null ? 0x40 : 0x00) | 0x80).CopyTo(metadata, 8);
            BitConverter.GetBytes((int) mode).CopyTo(metadata, 0x0C);
            keyY?.CopyTo(metadata, 0x10);
            iv.CopyTo(metadata, 0x20);
            return DecryptData(metadata, data, ofs);
        }

        public static byte[] TryDecryptBOSS(byte[] boss) // https://github.com/SciresM/3ds-crypto-server
        {
            var iv = new byte[0x10];
            Array.Copy(boss, 0x1C, iv, 0, 0xC);
            iv[0xF] = 1;

            var dec = TryDecryptData(boss, CryptoMode.CTR_Dec, PSPXI_AES.BOSS, iv, 0x28);
            if(dec == null)
                Util.Log(sex == null
                    ? $"Failed to decrypt BOSS file due to an exception: {ex}"
                    : $"Failed to decrypt BOSS file due to a socket exception: {sex}");
            return dec;
        }

        public static bool TestCryptoServer()
        {
            var iv = new byte[0x10];
            var keyY = new byte[0x10];
            var test_vector = new byte[] { 0xBC, 0xC4, 0x16, 0x2C, 0x2A, 0x06, 0x91, 0xEE, 0x47, 0x18, 0x86, 0xB8, 0xEB, 0x2F, 0xB5, 0x48 };
            
            try
            {
                var pingresult = new Ping().Send(crypto_ip, 2000).Status == IPStatus.Success;
                if (!pingresult)
                {
                    Util.Log("Crypto Server selftest failed due to server being offline.");
                    return false;
                }
                var dec = TryDecryptData(test_vector, CryptoMode.CBC_Dec, 0x2C, iv, 0, keyY);

                if (dec == null)
                {
                    Util.Log(sex == null
                        ? $"Crypto Server test failed due to an exception: {ex}"
                        : $"Crypto Server test failed due to Socket Exception: {sex}");
                    return false;
                }

                if (dec.All(t => t == 0) && dec.Length == 0x10)
                {
                    Util.Log("Crypto Server test succeeded!");
                    return true;
                }
                Util.Log(
                    "Crypto Server test failed due to incorrect output. Check that the server is configured properly.");
                return false;
            }
            catch (PingException pex)
            {
                Util.Log($"Crypto Server selftest failed due to ping exception: {pex.Message}, Inner Exception: {pex.InnerException.Message}");
                return false;
            }
            
        }
    }
}