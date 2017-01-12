using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static System.String;

namespace BadgeArcadeTool
{
    public class SARC
    {
        public string Signature;
        public ushort HeaderSize = 0x14;
        public ushort Endianness;
        public uint FileSize;
        public uint DataOffset;
        public uint Unknown;
        public SFAT SFat;
        public SFNT SFnt;
        public byte[] Data;

        public string FileName;
        public string FilePath;
        public string Extension;
        public bool valid;

        public static SARC Analyze(string path)
        {
            var sarc = new SARC
            {
                FileName = Path.GetFileNameWithoutExtension(path),
                FilePath = Path.GetDirectoryName(path),
                Extension = Path.GetExtension(path),
                valid = true
            };
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                sarc.Signature = new string(br.ReadChars(4));
                if (sarc.Signature != "SARC")
                {
                    sarc.valid = false;
                    return sarc;
                }
                sarc.HeaderSize = br.ReadUInt16();
                sarc.Endianness = br.ReadUInt16();
                sarc.FileSize = br.ReadUInt32();
                sarc.DataOffset = br.ReadUInt32();
                sarc.Unknown = br.ReadUInt32();
                sarc.SFat = new SFAT
                {
                    Signature = new string(br.ReadChars(4)),
                    HeaderSize = br.ReadUInt16(),
                    EntryCount = br.ReadUInt16(),
                    HashMult = br.ReadUInt32(),
                    Entries = new List<SFATEntry>()
                };
                if (sarc.SFat.Signature != "SFAT")
                {
                    sarc.valid = false;
                    return sarc;
                }
                for (var i = 0; i < sarc.SFat.EntryCount; i++)
                {
                    var s = new SFATEntry
                    {
                        FileNameHash = br.ReadUInt32(),
                        FileNameOffset = br.ReadUInt32(),
                        FileDataStart = br.ReadUInt32(),
                        FileDataEnd = br.ReadUInt32()
                    };
                    sarc.SFat.Entries.Add(s);
                }
                sarc.SFnt = new SFNT
                {
                    Signature = new string(br.ReadChars(4)),
                    HeaderSize = br.ReadUInt16(),
                    Unknown = br.ReadUInt16(),
                    StringOffset = (uint)br.BaseStream.Position
                };
                if (sarc.SFnt.Signature != "SFNT")
                {
                    sarc.valid = false;
                    return sarc;
                }

            }
            sarc.Data = File.ReadAllBytes(path);
            if (sarc.FileSize == sarc.Data.Length) return sarc;

            sarc.valid = false;
            sarc.Data = null;
            return sarc;
        }

        public string GetFilePath(SFATEntry entry)
        {
            if (!valid) return Empty;
            var sb = new StringBuilder();
            var ofs = SFnt.StringOffset + (entry.FileNameOffset & 0xFFFFFF) * 4;
            while (Data[ofs] != 0)
            {
                sb.Append((char)Data[ofs++]);
            }

            return sb.ToString().Replace('/', Path.DirectorySeparatorChar);
        }

        public byte[] GetFileData(SFATEntry entry)
        {
            if (!valid) return null;
            var len = entry.FileDataEnd - entry.FileDataStart;
            var d = new byte[len];
            Array.Copy(Data, entry.FileDataStart + DataOffset, d, 0, len);
            return d;
        }

        public byte[] GetDecompressedData(SFATEntry entry)
        {
            if (!valid) return null;
            var d = GetFileData(entry);
            return BitConverter.ToUInt32(d, 0) == 0x307A6159 
                ? Yaz0_Decompress(d) 
                : d;
        }


        public static byte[] Yaz0_Decompress(byte[] data)
        {
            var len = (uint)(data[4] << 24 | data[5] << 16 | data[6] << 8 | data[7]);
            var result = new byte[len];
            var Offs = 16;
            var dstoffs = 0;
            while (true)
            {
                var header = data[Offs++];
                for (var i = 0; i < 8; i++)
                {
                    if ((header & 0x80) != 0) result[dstoffs++] = data[Offs++];
                    else
                    {
                        var b = data[Offs++];
                        var offs = ((b & 0xF) << 8 | data[Offs++]) + 1;
                        var length = (b >> 4) + 2;
                        if (length == 2) length = data[Offs++] + 0x12;
                        for (var j = 0; j < length; j++)
                        {
                            result[dstoffs] = result[dstoffs - offs];
                            dstoffs++;
                        }
                    }
                    if (dstoffs >= len) return result;
                    header <<= 1;
                }
            }
        }
    }

    public class SFAT
    {
        public string Signature;
        public ushort HeaderSize;
        public ushort EntryCount;
        public uint HashMult;
        public List<SFATEntry> Entries;
    }

    public class SFATEntry
    {
        public uint FileNameHash;
        public uint FileNameOffset;
        public uint FileDataStart;
        public uint FileDataEnd;
    }

    public class SFNT
    {
        public string Signature;
        public ushort HeaderSize;
        public ushort Unknown;
        public uint StringOffset;
    }
}
