using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace BadgeArcadeTool
{
    class Program
    {
        public static DateTime now = DateTime.Now;
        private static string server = "https://npdl.cdn.nintendowifi.net/p01/nsa/{0}/data/{1}?tm=2";
        private const string US_ID = "OvbmGLZ9senvgV3K";
        private const string JP_ID = "j0ITmVqVgfUxe0O9";
        private const string EU_ID = "J6la9Kj8iqTvAPOq";
        private static readonly string[] badge_filelist = {"allbadge_v130.dat", "allbadge_v131.dat"};
        private static readonly Dictionary<string, string> country_list = new Dictionary<string, string>() { {"US", US_ID}, {"JP", JP_ID}, {"EU", EU_ID} }; 
        private static bool keep_log = false;
        private static StreamWriter log;

        static void CreateDirectoryIfNull(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public static void Log(string msg)
        {
            Console.WriteLine(msg);
            log.WriteLine(msg);
        }


        static void Main(string[] args)
        {
            CreateDirectoryIfNull("logs");
            CreateDirectoryIfNull("data");
            CreateDirectoryIfNull("badges");
            var logFile = $"logs/{now.ToString("MMMM dd, yyyy - HH-mm-ss")}.log";
            log = new StreamWriter(logFile, false, Encoding.Unicode);

            Log("BadgeArcadeTool v1.0 - SciresM");
            Log($"{now.ToString("MMMM dd, yyyy - HH-mm-ss")}");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            Log("Installed certificate bypass.");

            try
            {
                UpdateArchives();
            }
            catch (Exception ex)
            {
                keep_log = true;
                Log($"An exception occurred: {ex.Message}");
            }

            log.Close();
            if (!keep_log)
                File.Delete(logFile);
        }

        static void UpdateArchives()
        {
            foreach (var country in country_list.Keys)
            {
                Log($"Checking {country}...");
                var country_dir = Path.Combine("data", country);
                var country_id = country_list[country];
                CreateDirectoryIfNull(country_dir);
                foreach (var archive in badge_filelist)
                {
                    var archive_path = Path.Combine(country_dir, archive);
                    var updated = false;
                    var server_file = string.Format(server, country_id, archive);
                    if (!File.Exists(archive_path))
                    {
                        var arc = NetworkUtils.TryDownload(server_file);
                        if (arc != null)
                        {
                            updated = true;
                            File.WriteAllBytes(archive_path, arc);
                        }
                    }
                    else
                    {
                        var old = File.ReadAllBytes(archive_path);
                        var new_arc = NetworkUtils.DownloadFirstBytes(server_file);
                        if (!(new_arc.SequenceEqual(old.Take(new_arc.Length))))
                        {
                            var arc = NetworkUtils.TryDownload(server_file);
                            if (arc != null)
                            {
                                updated = true;
                                File.WriteAllBytes(archive_path, arc);
                            }
                        }
                    }

                    if (updated)
                    {
                        keep_log = true;
                        Log($"{country}'s {archive} is updated. Decrypting + Extracting...");
                        var dec_boss = NetworkUtils.TryDecryptBOSS(File.ReadAllBytes(archive_path));
                        if (dec_boss == null)
                            continue;
                        var sarcdata = dec_boss.Skip(0x296).ToArray();
                        File.WriteAllBytes(Path.Combine(country_dir, Path.GetFileNameWithoutExtension(archive_path) + ".sarc"), sarcdata);

                        var sarc = SARC.Analyze(Path.Combine(country_dir, Path.GetFileNameWithoutExtension(archive_path) + ".sarc"));

                        if (!sarc.valid)
                        {
                            Log($"{country}'s {archive} isn't a valid SARC. Maybe bad decryption...?");
                            continue;
                        }

                        var data_dir = Path.Combine(country_dir, "files");
                        CreateDirectoryIfNull(data_dir);

                        foreach (var entry in sarc.SFat.Entries)
                        {
                            var sb = new StringBuilder();
                            var ofs = sarc.SFnt.StringOffset + (entry.FileNameOffset & 0xFFFFFF)*4;
                            while (sarcdata[ofs] != 0)
                            {
                                sb.Append((char) sarcdata[ofs++]);
                            }
                            var path = Path.Combine(data_dir, sb.ToString().Replace('/', Path.DirectorySeparatorChar));
                            var len = entry.FileDataEnd - entry.FileDataStart;
                            var data = new byte[len];
                            Array.Copy(sarcdata, entry.FileDataStart + sarc.DataOffset, data, 0, len);

                            CreateDirectoryIfNull(Path.GetDirectoryName(path));
                            if (!File.Exists(path))
                            {
                                Log($"New {country} file: {Path.GetFileName(path)}");
                                File.WriteAllBytes(path, data);

                                if (Path.GetFileName(path).StartsWith("Pr_") && BitConverter.ToUInt32(data, 0) == 0x307A6159) // 'Yaz0'
                                {
                                    var prbdata = SARC.Yaz0_Decompress(data);
                                    if (BitConverter.ToUInt32(prbdata, 0) == 0x53425250) // 'PRBS'
                                    {
                                        var prb = new PRBS(prbdata);
                                        var png_dir = Path.Combine(Path.Combine("png", country), prb.CategoryName);
                                        CreateDirectoryIfNull(png_dir);
                                        using (var bmp = prb.GetImage())
                                            bmp.Save(Path.GetFullPath(Path.Combine(png_dir, prb.ImageName + ".png")), ImageFormat.Png);
                                        if (prb.NumTiles > 1)
                                        {
                                            using (var ptile = prb.GetTile(0))
                                                ptile.Save(Path.GetFullPath(Path.Combine(png_dir, prb.ImageName + ".downsampledpreview.png")), ImageFormat.Png);
                                            for (var i = 0; i < prb.NumTiles; i++)
                                            {
                                                using (var tile = prb.GetTile(i+1))
                                                    tile.Save(Path.GetFullPath(Path.Combine(png_dir, prb.ImageName + $".tile_{i}.png")), ImageFormat.Png);
                                            }
                                        }

                                        Log($"Saved {country} images for {prb.ImageName}.");
                                    }
                                }
                            }
                            
                        }

                    }
                }
            }
        }
    }
}
