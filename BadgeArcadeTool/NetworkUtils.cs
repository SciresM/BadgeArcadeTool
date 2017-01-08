using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;

namespace BadgeArcadeTool
{
    public class NetworkUtils
    {
        public static byte[] TryDownload(string file)
        {
            try
            {
                return new WebClient().DownloadData(file);
            }
            catch (WebException wex)
            {
                Program.Log($"Failed to download {file}.");
                return null;
            }
        }

        public static byte[] DownloadFirstBytes(string file)
        {
            const int bytes = 0x400;
            var req = (HttpWebRequest)WebRequest.Create(file);
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
            sock.Connect(new IPAddress(new byte[] { 192, 168, 1, 137 }), 8081);
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
            sock.Send(BitConverter.GetBytes(0xDEADCAFE));
            sock.Close();
            return dec;
        }
    }
}