using System;
using System.Linq;
using System.Net;
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

        private static void DownloadProgressCallback(object sender, DownloadProgressChangedEventArgs e)
        {
            progress.Report((double) e.BytesReceived/boss_size);
        }

        public static byte[] TryDownload(string file)
        {
            using (progress = new ProgressBar())
            {
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
                catch (WebException wex)
                {
                    return null;
                }
            }
        }

        public static byte[] DownloadFirstBytes(string file)
        {
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
            catch (WebException wex)
            {
                return null;
            }
        }

        public static byte[] TryDecryptBOSS(byte[] boss) // https://github.com/SciresM/3ds-crypto-server
        {
            var iv = new byte[0x10];
            Array.Copy(boss, 0x1C, iv, 0, 0xC);
            iv[0xF] = 1;

            var metadata = new byte[1024];
            BitConverter.GetBytes(0xCAFEBABE).CopyTo(metadata, 0);
            BitConverter.GetBytes(boss.Length - 0x28).CopyTo(metadata, 4);
            BitConverter.GetBytes(3).CopyTo(metadata, 8);
            BitConverter.GetBytes(3).CopyTo(metadata, 0xC);
            iv.CopyTo(metadata, 0x20);

            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sock.Connect(crypto_ip, crypto_port);
            sock.Send(metadata);

            var _bufsize = new byte[4];
            sock.Receive(_bufsize);

            var bufsize = BitConverter.ToInt32(_bufsize, 0);
            sock.ReceiveBufferSize = bufsize;
            sock.SendBufferSize = bufsize;

            var dec = new byte[boss.Length];
            boss.CopyTo(dec, 0);
            var ofs = 0x28;
            var i = 0;
            var tot = dec.Length / bufsize;
            using (var progress = new ProgressBar())
            {
                while (ofs < boss.Length)
                {
                    var buf = new byte[ofs + bufsize < boss.Length ? bufsize : boss.Length - ofs];
                    Array.Copy(boss, ofs, buf, 0, buf.Length);
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
                            progress.Report((double) ofs/boss.Length);
                        }
                        i++;
                    }
                    catch (SocketException sex)
                    {
                        Program.Log("Failed to decrypt BOSS file due to socket connection error.");
                        sock.Close();
                        return null;
                    }
                }
            }
            sock.Send(BitConverter.GetBytes(0xDEADCAFE));
            sock.Close();
            return dec;
        }

        public static bool TestCryptoServer()
        {
            var iv = new byte[0x10];
            var keyY = new byte[0x10];
            var metadata = new byte[1024];
            BitConverter.GetBytes(0xCAFEBABE).CopyTo(metadata, 0);
            BitConverter.GetBytes(0x10).CopyTo(metadata, 4);
            BitConverter.GetBytes(0x2C | 0x80 | 0x40).CopyTo(metadata, 8);
            BitConverter.GetBytes(1).CopyTo(metadata, 0xC);
            keyY.CopyTo(metadata, 0x10);
            iv.CopyTo(metadata, 0x20);

            var test_vector = new byte[] { 0xBC, 0xC4, 0x16, 0x2C, 0x2A, 0x06, 0x91, 0xEE, 0x47, 0x18, 0x86, 0xB8, 0xEB, 0x2F, 0xB5, 0x48 };
            var test_vector_dec = new byte[0x10];
            for (var i = 0; i < test_vector_dec.Length; i++)
                test_vector_dec[i] = 0xFF;

            try
            {
                var pingresult = new Ping().Send(crypto_ip, 2000).Status == IPStatus.Success;
                if (!pingresult)
                {
                    Program.Log("Crypto Server selftest failed due to server being offline.");
                    return false;
                }

                var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                sock.Connect(crypto_ip, crypto_port);
                sock.Send(metadata);

                var _bufsize = new byte[4];
                sock.Receive(_bufsize);

                var bufsize = BitConverter.ToInt32(_bufsize, 0);
                sock.ReceiveBufferSize = bufsize;
                sock.SendBufferSize = bufsize;

                var ofs = 0;
                while (ofs < test_vector.Length)
                {
                    var buf = new byte[ofs + bufsize < test_vector.Length ? bufsize : test_vector.Length - ofs];
                    Array.Copy(test_vector, ofs, buf, 0, buf.Length);
                    var s = sock.Send(buf);
                    var r = buf.Length;
                    while (r > 0)
                    {
                        var d = sock.Receive(buf);
                        r -= d;
                        Array.Copy(buf, 0, test_vector_dec, ofs, d);
                        ofs += d;
                        Array.Resize(ref buf, r);
                    }
                }

                sock.Send(BitConverter.GetBytes(0xDEADCAFE));
                sock.Close();

                if (test_vector_dec.All(t => t == 0))
                {
                    Program.Log("Crypto Server test succeeded!");
                    return true;
                }
                Program.Log(
                    "Crypto Server test failed due to incorrect output. Check that the server is configured properly.");
                return false;
            }
            catch (SocketException sex)
            {
                Program.Log($"Crypto Server selftest failed due to socket exception: {sex.Message}");
                return false;
            }
            catch (PingException pex)
            {
                Program.Log($"Crypto Server selftest failed due to ping exception: {pex.Message}, Inner Exception: {pex.InnerException.Message}");
                return false;
            }
        }
    }
}