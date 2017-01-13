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

        public static void Log(string msg, bool newline = true)
        {
            if (newline)
            {
                Console.WriteLine(msg);
                log.WriteLine(msg);
            }
            else
            {
                Console.Write(msg);
                log.Write(msg);
            }
        }


        static void Main(string[] args)
        {
            Directory.CreateDirectory("logs");
            Directory.CreateDirectory("data");
            Directory.CreateDirectory("badges");
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
            Log("Testing Crypto Server...");
            var passed_selftest = NetworkUtils.TestCryptoServer();

            foreach (var country in country_list.Keys)
            {
                var country_dir = Path.Combine("data", country);
                var country_id = country_list[country];
                Directory.CreateDirectory(country_dir);
                foreach (var archive in badge_filelist)
                {
                    var archive_path = Path.Combine(country_dir, archive);
                    var sarc_path = Path.Combine(country_dir, Path.GetFileNameWithoutExtension(archive_path) + ".sarc");
                    var server_file = string.Format(server, country_id, archive);
                    Log($"{country} / {archive}...", false);
                    if (!File.Exists(archive_path))
                    {
                        Log("Downloading...", false);
                        var arc = NetworkUtils.TryDownload(server_file);
                        if (arc != null)
                        {
                            File.WriteAllBytes(archive_path, arc);
                            if (File.Exists(sarc_path))
                                File.Delete(sarc_path);
                        }
                        else
                        {
                            Log("Download failed");
                            continue;
                        }
                    }
                    else
                    {
                        var old = File.ReadAllBytes(archive_path);
                        var new_arc = NetworkUtils.DownloadFirstBytes(server_file);
                        if (new_arc == null) continue;
                        if (!(new_arc.SequenceEqual(old.Take(new_arc.Length))))
                        {
                            Log("Updating...", false);
                            var arc = NetworkUtils.TryDownload(server_file);
                            if (arc != null)
                            {
                                File.WriteAllBytes(archive_path, arc);
                                if (File.Exists(sarc_path))
                                    File.Delete(sarc_path);
                            }
                            else
                            {
                                Log("Update failed");
                                continue;
                            }
                        }
                    }

                    SARC sarc;
                    if (!File.Exists(sarc_path) && passed_selftest)
                    {
                        keep_log = true;
                        Log("Decrypting...", false);
                        var dec_boss = NetworkUtils.TryDecryptBOSS(File.ReadAllBytes(archive_path));
                        if (dec_boss == null)
                            continue;
                        File.WriteAllBytes(sarc_path, dec_boss.Skip(0x296).ToArray());

                        sarc = SARC.Analyze(sarc_path);
                        if (!sarc.valid)
                        {
                            Log($"Not a valid SARC. Maybe bad decryption...?");
                            passed_selftest = false;
                            File.Delete(sarc_path);
                            continue;
                        }
                    }
                    else if (!File.Exists(sarc_path))
                    {
                        Log("Done");
                        continue;
                    }
                    else
                    {
                        sarc = SARC.Analyze(sarc_path);
                        if (!sarc.valid)
                        {
                            Log("SARC file corrupted");
                            File.Delete(sarc_path);
                            continue;
                        }
                    }

                    


                    Log($"Extracting...");

                    var data_dir = Path.Combine(country_dir, "files");
                    var decompressed_data_dir = Path.Combine(country_dir, "decompressed");
                    Directory.CreateDirectory(data_dir);
                    Directory.CreateDirectory(decompressed_data_dir);

                    foreach (var entry in sarc.SFat.Entries)
                    {
                        
                        var path = Path.Combine(data_dir, sarc.GetFilePath(entry));
                        var decompressed_path = Path.Combine(decompressed_data_dir, 
                            Path.ChangeExtension(sarc.GetFilePath(entry),null));

                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        Directory.CreateDirectory(Path.GetDirectoryName(decompressed_path));

                        var file_data = sarc.GetFileData(entry);

                        if (!File.Exists(path))
                        {
                            Log($"New {country} file: {Path.GetFileName(path)}");
                            File.WriteAllBytes(path, file_data);

                            var prbdata = sarc.GetDecompressedData(entry);
                            File.WriteAllBytes(decompressed_path, prbdata);

                            if (BitConverter.ToUInt32(prbdata, 0) == 0x53425250) // 'PRBS'
                            {
                                var prb = new PRBS(prbdata);
                                var png_dir_full = Path.Combine("png",Path.Combine(Path.Combine("full", country), prb.CategoryName));
                                var png_dir_tiles = Path.Combine("png", Path.Combine(Path.Combine("tiles", country), prb.CategoryName));
                                var png_dir_downsampled = Path.Combine("png", Path.Combine(Path.Combine("downsampled", country), prb.CategoryName));
                                Directory.CreateDirectory(png_dir_full);
                                Directory.CreateDirectory(png_dir_tiles);
                                Directory.CreateDirectory(png_dir_downsampled);
                                using (var bmp = prb.GetImage())
                                {
                                    bmp.Save(Path.GetFullPath(Path.Combine(png_dir_full, prb.ImageName + ".png")),
                                        ImageFormat.Png);

                                    if (prb.NumTiles == 1)
                                    {
                                        bmp.Save(
                                            Path.GetFullPath(Path.Combine(png_dir_tiles, prb.ImageName + ".png")),
                                            ImageFormat.Png);
                                        bmp.Save(
                                            Path.GetFullPath(Path.Combine(png_dir_downsampled, prb.ImageName + ".png")),
                                            ImageFormat.Png);
                                    }
                                }
                                if (prb.NumTiles > 1)
                                {
                                    using (var ptile = prb.GetTile(0))
                                        ptile.Save(Path.GetFullPath(Path.Combine(png_dir_downsampled, prb.ImageName + ".downsampledpreview.png")), ImageFormat.Png);
                                    for (var i = 0; i < prb.NumTiles; i++)
                                    {
                                        using (var tile = prb.GetTile(i+1))
                                            tile.Save(Path.GetFullPath(Path.Combine(png_dir_tiles, prb.ImageName + $".tile_{i}.png")), ImageFormat.Png);
                                    }
                                }

                                Log($"Saved {country} images for {prb.ImageName}.");
                            }
                        }
                        else
                        {
                            var old_data = File.ReadAllBytes(path);
                            var data_update = old_data.Length != file_data.Length;
                            for (var i = 0; i < file_data.Length; i++)
                            {
                                if (file_data[i] != old_data[i])
                                    data_update = true;
                                if (data_update)
                                    break;
                            }
                            if (!data_update) continue;
                            Log($"Updated {country} file: {Path.GetFileName(path)}");

                            // Do nothing for updated files.
                        }

                    }
                    Log($"{country} / {archive}...Extraction Complete");
                }
            }
        }
    }
}
