using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BadgeArcadeTool
{
    class PRBS
    {
        private byte[] Data;
        public string ImageName => Encoding.ASCII.GetString(Data, 0x44, 0x30).TrimEnd((char) 0);
        public string CategoryName => Encoding.ASCII.GetString(Data, 0x74, 0x30).TrimEnd((char)0);
        public int TilesWide => BitConverter.ToInt32(Data, 0xB8);
        public int TilesHigh => BitConverter.ToInt32(Data, 0xBC);
        public int NumTiles => TilesWide*TilesHigh;
        public PRBS(byte[] d)
        {
            Data = (byte[])d.Clone();
        }

        internal static int[] Convert5To8 = { 0x00,0x08,0x10,0x18,0x20,0x29,0x31,0x39,
                                              0x41,0x4A,0x52,0x5A,0x62,0x6A,0x73,0x7B,
                                              0x83,0x8B,0x94,0x9C,0xA4,0xAC,0xB4,0xBD,
                                              0xC5,0xCD,0xD5,0xDE,0xE6,0xEE,0xF6,0xFF };

        public Bitmap GetTile(int index)
        {
            var rgb_ofs = 0x1100 + index * (0x2800 + 0xA00);
            var alpha_ofs = 0x3100 + index * (0x2800 + 0xA00);
            using (var bmp = new Bitmap(64, 64))
            {
                var bmpdata = bmp.LockBits(new Rectangle(0, 0, 64, 64), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                var ptr = bmpdata.Scan0;

                var bytes = Math.Abs(bmpdata.Stride)*bmp.Height;
                var rgbvals = new byte[bytes];

                Marshal.Copy(ptr, rgbvals, 0, bytes);

                for (var i = 0; i < bytes; i++)
                    rgbvals[i] = 0x00;

                for (var i = 0; i < 64*64; i++)
                {
                    var rgb = BitConverter.ToUInt16(Data, rgb_ofs + i*2);
                    var r = Convert5To8[(rgb & 0xF800) >> 11];
                    var g = (rgb & 0x07E0) >> 3;
                    var b = Convert5To8[(rgb & 0x001F)];
                    var a = ((Data[alpha_ofs+i/2] >> (4 * (i % 2))) & 0x0F) * 0x11;

                    var x = 8 * ((i / 64) % 8) + (((i % 64) & 0x01) >> 0) + (((i % 64) & 0x04) >> 1) + (((i % 64) & 0x10) >> 2);
                    var y = 8 * (i / 512) + (((i % 64) & 0x02) >> 1) + (((i % 64) & 0x08) >> 2) + (((i % 64) & 0x20) >> 3);

                    rgbvals[y * 64 * 4 + x * 4 + 0] = (byte)b;
                    rgbvals[y * 64 * 4 + x * 4 + 1] = (byte)g;
                    rgbvals[y * 64 * 4 + x * 4 + 2] = (byte)r;
                    rgbvals[y * 64 * 4 + x * 4 + 3] = (byte)a;
                }

                Marshal.Copy(rgbvals, 0, ptr, bytes);
                bmp.UnlockBits(bmpdata);
                return (Bitmap)(bmp.Clone());
            }
        }

        public Bitmap GetImage()
        {
            if (NumTiles == 1)
                return GetTile(0);
            using (var bmp = new Bitmap(64*TilesWide, 64*TilesHigh))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    for (var i = 0; i < NumTiles; i++)
                    {
                        using (var tile = GetTile(i+1))
                            g.DrawImage(tile, new Point(64*(i % TilesWide), 64*(i / TilesWide)));
                    }
                }
                return (Bitmap)(bmp.Clone());
            }
        }
    }
}
