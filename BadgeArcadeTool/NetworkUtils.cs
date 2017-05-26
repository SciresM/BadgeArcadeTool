using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace BadgeArcadeTool
{
    public class NetworkUtils
    {
        public static ProgressBar progress;
        public static byte[] download_data;
        public static long boss_size;
        public static SocketException sex;
        public static WebException wex;
        public static Exception ex;

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

        
    }
}