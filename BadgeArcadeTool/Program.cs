using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using CommandLine;

namespace BadgeArcadeTool
{
    class Program
    {
        private static string server = "https://npdl.cdn.nintendowifi.net/p01/nsa/{0}/data/{1}?tm=2";
        private const string US_ID = "OvbmGLZ9senvgV3K";
        private const string JP_ID = "j0ITmVqVgfUxe0O9";
        private const string EU_ID = "J6la9Kj8iqTvAPOq";
        private static readonly string[] badge_filelist = {"allbadge_v130.dat", "allbadge_v131.dat"};
        private static readonly Dictionary<string, string> country_list = new Dictionary<string, string>() { {"US", US_ID}, {"JP", JP_ID}, {"EU", EU_ID} }; 
        private static bool keep_log = false;
        private static SARC sarc;
        public static Options settings = new Options();

        static void Main(string[] args)
        {
            var heading = "BadgeArcadeTool v1.0 - SciresM";
            var parser = new Parser();
            var opts = new Options();
            
            if (!parser.ParseArguments(args, opts) || opts.help)
            {
                Console.WriteLine(heading);
                Console.WriteLine(opts.GetUsage());
                return;
            }

            //Read settings.xml
            settings = opts.Reset
                ? new Options()
                :Util.DeserializeFile<Options>("settings.xml") ?? new Options();
            
            //Validate and Update the settings if necessary.
            if (!string.IsNullOrEmpty(opts.InputIP))
            {
                IPAddress ipaddress;
                if (IPAddress.TryParse(opts.InputIP, out ipaddress))
                    settings.InputIP = opts.InputIP;
                else
                {
                    Console.WriteLine(heading);
                    Console.WriteLine($"Error: Invalid IP Address ({opts.InputIP})");
                    return;
                }
            }

            //Set up default Settings if blank.
            if (string.IsNullOrEmpty(settings.InputIP))
                settings.InputIP = "192.168.1.137";

            //Save the settings.
            Util.Serialize(settings, "settings.xml");

            NetworkUtils.SetCryptoIPAddress(IPAddress.Parse(settings.InputIP));

            Directory.CreateDirectory("data");
            Directory.CreateDirectory("badges");

            Util.NewLogFile(heading);
            
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            Util.Log("Installed certificate bypass.");

            try
            {
                UpdateArchives();
            }
            catch (Exception ex)
            {
                keep_log = true;
                Util.Log($"An exception occurred: {ex.Message}");
            }

            Util.CloseLogFile(keep_log);
        }

        static void WriteSARCFileData(SFATEntry entry, string country, string path, string decompressed_path)
        {

            File.WriteAllBytes(path, sarc.GetFileData(entry));

            var prbdata = sarc.GetDecompressedData(entry);
            File.WriteAllBytes(decompressed_path, prbdata);

            if (BitConverter.ToUInt32(prbdata, 0) == 0x53425250) // 'PRBS'
            {
                var prb = new PRBS(prbdata);
                var png_dir_full = Path.Combine("png", Path.Combine(Path.Combine("full", country), prb.CategoryName));
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
                        using (var tile = prb.GetTile(i + 1))
                            tile.Save(Path.GetFullPath(Path.Combine(png_dir_tiles, prb.ImageName + $".tile_{i}.png")), ImageFormat.Png);
                    }
                }

                Util.Log($"Saved {country} images for {prb.ImageName}.");
            }
        }


        static void UpdateArchives()
        {
            Util.Log($"Testing Crypto Server ({NetworkUtils.crypto_ip})...");
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
                    var xml_path = Path.Combine(country_dir, Path.GetFileNameWithoutExtension(archive_path) + ".xml");
                    var server_file = string.Format(server, country_id, archive);
                    Util.Log($"{country} / {archive}...", false);
                    if (!File.Exists(archive_path))
                    {
                        Util.Log("Downloading...", false);
                        var arc = NetworkUtils.TryDownload(server_file);
                        if (arc != null)
                        {
                            File.WriteAllBytes(archive_path, arc);
                            if (File.Exists(sarc_path))
                                File.Delete(sarc_path);
                        }
                        else
                        {
                            Util.Log("Download failed");
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
                            Util.Log("Updating...", false);
                            var arc = NetworkUtils.TryDownload(server_file);
                            if (arc != null)
                            {
                                File.WriteAllBytes(archive_path, arc);
                                if (File.Exists(sarc_path))
                                    File.Delete(sarc_path);
                            }
                            else
                            {
                                Util.Log("Update failed");
                                continue;
                            }
                        }
                    }

                    if (!File.Exists(sarc_path) && passed_selftest)
                    {
                        keep_log = true;
                        Util.Log("Decrypting...", false);
                        var dec_boss = NetworkUtils.TryDecryptBOSS(File.ReadAllBytes(archive_path));
                        if (dec_boss == null)
                            continue;
                        File.WriteAllBytes(sarc_path, dec_boss.Skip(0x296).ToArray());

                        sarc = SARC.Analyze(sarc_path);
                        if (!sarc.valid)
                        {
                            Util.Log($"Not a valid SARC. Maybe bad decryption...?");
                            passed_selftest = false;
                            File.Delete(sarc_path);
                            continue;
                        }
                    }
                    else if (!File.Exists(sarc_path))
                    {
                        Util.Log("Done");
                        continue;
                    }
                    else
                    {
                        sarc = SARC.Analyze(sarc_path);
                        if (!sarc.valid)
                        {
                            Util.Log("SARC file corrupted");
                            File.Delete(sarc_path);
                            continue;
                        }
                    }

                    


                    Util.Log($"Extracting...");
                    var sarchashes = Util.DeserializeFile<SARCFileHashes>(xml_path) ?? new SARCFileHashes();


                    var data_dir = Path.Combine(country_dir, Path.GetFileNameWithoutExtension(archive_path), "files");
                    var decompressed_data_dir = Path.Combine(country_dir, Path.GetFileNameWithoutExtension(archive_path), "decompressed");
                    Directory.CreateDirectory(data_dir);
                    Directory.CreateDirectory(decompressed_data_dir);

                    foreach (var entry in sarc.SFat.Entries)
                    {
                        
                        var path = Path.Combine(data_dir, sarc.GetFilePath(entry));
                        var decompressed_path = Path.Combine(decompressed_data_dir, 
                            Path.ChangeExtension(sarc.GetFilePath(entry),null));

                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        Directory.CreateDirectory(Path.GetDirectoryName(decompressed_path));

                        var hashresult = sarchashes.IsHashEqual(sarc.GetFilePath(entry), sarc.GetFileHash(entry));
                        sarchashes.SetHash(sarc.GetFilePath(entry), sarc.GetFileHash(entry));
                        if (!File.Exists(path))
                        {
                            Util.Log(hashresult == SARCHashResult.NotFound
                                ? $"New {country} file: {Path.GetFileName(path)}"
                                : hashresult == SARCHashResult.Equal
                                    ? $"{country} file: {Path.GetFileName(path)} was deleted"
                                    : $"Updated {country} file: {Path.GetFileName(path)}");
                            WriteSARCFileData(entry, country, path, decompressed_path);
                        }
                        else
                        {
                            if (hashresult != SARCHashResult.NotEqual) continue;
                            Util.Log($"Updated {country} file: {Path.GetFileName(path)}");

                            //Can't do nothing for updated files, 
                            //or the file will ALWAYS say its updated every run, not the intended result.
                            WriteSARCFileData(entry, country, path, decompressed_path);
                        }

                    }
                    Util.Serialize(sarchashes, xml_path);
                    Util.Log($"{country} / {archive}...Extraction Complete");
                }
            }
        }
    }
}
