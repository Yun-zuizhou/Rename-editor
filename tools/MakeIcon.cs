// 生成图标：三种变体 × 多尺寸 .ico + 预览 PNG
// 用 csc（C# 5）编译：不允许插值字符串、nameof、?. 等
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    // ---------- 画圆角方形底色 ----------
    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        float d = r * 2f;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // 画一张 1024x1024 的大图，最后缩放
    static Bitmap Render(string variant, int master)
    {
        Bitmap bmp = new Bitmap(master, master, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float S = master;
            RectangleF tile = new RectangleF(S * 0.03f, S * 0.03f, S * 0.94f, S * 0.94f);
            float radius = S * 0.225f;

            Color top, bottom;
            if (variant == "main") { top = Color.FromArgb(0x38, 0x8A, 0xFF); bottom = Color.FromArgb(0x0B, 0x4F, 0xD0); }
            else if (variant == "quick") { top = Color.FromArgb(0xFF, 0xB2, 0x35); bottom = Color.FromArgb(0xE0, 0x74, 0x00); }
            else { top = Color.FromArgb(0x6B, 0x74, 0x82); bottom = Color.FromArgb(0x3A, 0x42, 0x4F); }

            using (GraphicsPath tp = RoundRect(tile.X, tile.Y, tile.Width, tile.Height, radius))
            {
                // 顶部微高光 + 主渐变
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new RectangleF(tile.X, tile.Y, tile.Width, tile.Height), top, bottom, 90f))
                {
                    ColorBlend blend = new ColorBlend();
                    blend.Colors = new Color[] { top, Blend(top, bottom, 0.45f), bottom };
                    blend.Positions = new float[] { 0f, 0.55f, 1f };
                    br.InterpolationColors = blend;
                    g.FillPath(br, tp);
                }
                // 内描边（Win11 卡片质感）
                using (Pen pen = new Pen(Color.FromArgb(46, 255, 255, 255), S * 0.012f))
                    g.DrawPath(pen, tp);
            }

            Color ink = Color.FromArgb(255, 255, 255, 255);

            if (variant == "undo")
            {
                // 逆时针回转箭头
                using (Pen pen = new Pen(ink, S * 0.085f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    RectangleF arc = new RectangleF(S * 0.24f, S * 0.26f, S * 0.52f, S * 0.52f);
                    g.DrawArc(pen, arc, 150f, 280f);
                }
                using (SolidBrush b = new SolidBrush(ink))
                {
                    PointF[] tri = new PointF[] {
                        new PointF(S * 0.215f, S * 0.145f),
                        new PointF(S * 0.415f, S * 0.215f),
                        new PointF(S * 0.235f, S * 0.365f) };
                    g.FillPolygon(b, tri);
                }
            }
            else if (variant == "quick")
            {
                // 闪电
                using (SolidBrush b = new SolidBrush(ink))
                {
                    PointF[] bolt = new PointF[] {
                        new PointF(S*0.560f, S*0.150f),
                        new PointF(S*0.300f, S*0.545f),
                        new PointF(S*0.470f, S*0.545f),
                        new PointF(S*0.420f, S*0.865f),
                        new PointF(S*0.700f, S*0.450f),
                        new PointF(S*0.520f, S*0.450f) };
                    g.FillPolygon(b, bolt);
                }
            }
            else
            {
                // 三条“列表”横线 + 右下铅笔
                using (SolidBrush b = new SolidBrush(ink))
                {
                    FillBar(g, b, S, 0.185f, 0.230f, 0.44f, 0.085f);   // 长条
                    FillBar(g, b, S, 0.185f, 0.400f, 0.34f, 0.085f);   // 短条
                }
                // 铅笔（旋转坐标系）
                GraphicsState st = g.Save();
                g.TranslateTransform(S * 0.565f, S * 0.700f);
                g.RotateTransform(-45f);
                using (SolidBrush b = new SolidBrush(ink))
                {
                    g.FillRectangle(b, -S * 0.075f, -S * 0.075f, S * 0.400f, S * 0.150f);
                    PointF[] tip = new PointF[] {
                        new PointF(-S*0.075f, -S*0.075f),
                        new PointF(-S*0.075f,  S*0.075f),
                        new PointF(-S*0.175f,  0f) };
                    g.FillPolygon(b, tip);
                }
                g.Restore(st);
            }
        }
        return bmp;
    }

    static Color Blend(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    static void FillBar(Graphics g, Brush b, float S, float x, float y, float w, float h)
    {
        using (GraphicsPath p = RoundRect(S * x, S * y, S * w, S * h, S * h * 0.5f))
            g.FillPath(b, p);
    }

    // ---------- 缩放到指定尺寸（高质量）----------
    static Bitmap Scale(Bitmap src, int size)
    {
        Bitmap dst = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(dst))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, size, size));
        }
        return dst;
    }

    // ---------- 写多尺寸 ICO ----------
    static void WriteIco(Bitmap master, int[] sizes, string path)
    {
        List<byte[]> images = new List<byte[]>();
        List<int> dims = new List<int>();
        foreach (int s in sizes)
        {
            using (Bitmap b = Scale(master, s))
                images.Add(DibBytes(b));
            dims.Add(s);
        }

        using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; ++i)
            {
                int s = dims[i];
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((ushort)1); w.Write((ushort)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (byte[] img in images) w.Write(img);
        }
    }

    // 32bpp BGRA DIB（含 BITMAPINFOHEADER + AND 掩码）
    static byte[] DibBytes(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;
        int xorSize = w * h * 4;
        int andSize = maskStride * h;
        int total = 40 + xorSize + andSize;
        byte[] outBytes = new byte[total];

        BitArraySet(outBytes, 0, (uint)40);            // biSize
        BitArraySet(outBytes, 4, (uint)w);             // biWidth
        BitArraySet(outBytes, 8, (uint)(h * 2));       // biHeight（XOR+AND）
        BitArraySet(outBytes, 12, (ushort)1);          // biPlanes
        BitArraySet(outBytes, 14, (ushort)32);         // biBitCount
        BitArraySet(outBytes, 20, (uint)xorSize);      // biSizeImage

        BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[w * 4];
            for (int y = 0; y < h; ++y)
            {
                IntPtr src = new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride);
                System.Runtime.InteropServices.Marshal.Copy(src, row, 0, w * 4);
                int destRow = h - 1 - y;               // 自下而上
                Buffer.BlockCopy(row, 0, outBytes, 40 + destRow * w * 4, w * 4);
            }
        }
        finally { bmp.UnlockBits(d); }
        // AND 掩码全 0：由 alpha 决定透明
        return outBytes;
    }

    static void BitArraySet(byte[] buf, int off, uint value)
    {
        buf[off] = (byte)(value & 0xFF);
        buf[off + 1] = (byte)((value >> 8) & 0xFF);
        buf[off + 2] = (byte)((value >> 16) & 0xFF);
        buf[off + 3] = (byte)((value >> 24) & 0xFF);
    }

    static void BitArraySet(byte[] buf, int off, ushort value)
    {
        buf[off] = (byte)(value & 0xFF);
        buf[off + 1] = (byte)((value >> 8) & 0xFF);
    }

    // ---------- 预览条：浅色/深色背景下的小尺寸 ----------
    static void WritePreview(Bitmap master, string path)
    {
        int badge = 256;
        int rowH = 96;
        int W = 256 + 32 + 96 * 5 + 32;
        int H = badge + rowH * 2 + 48;
        using (Bitmap canvas = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(0xF3, 0xF3, 0xF3));
            using (Bitmap big = Scale(master, badge))
                g.DrawImage(big, 16, 16);
            int[] small = new int[] { 16, 20, 24, 32, 48 };
            Color[] bgs = new Color[] { Color.FromArgb(0xF9, 0xF9, 0xF9), Color.FromArgb(0x20, 0x20, 0x20) };
            for (int r = 0; r < 2; ++r)
            {
                int y = 16 + badge + 24 + r * rowH;
                using (SolidBrush bg = new SolidBrush(bgs[r]))
                    g.FillRectangle(bg, 0, y - 8, W, rowH - 8);
                int x = 24;
                foreach (int s in small)
                {
                    using (Bitmap sb = Scale(master, s))
                        g.DrawImage(sb, x, y + (rowH - 8 - s) / 2);
                    x += 72;
                }
            }
            canvas.Save(path, ImageFormat.Png);
        }
    }

    static void Main(string[] args)
    {
        // 用法: MakeIcon.exe <输出目录>
        string outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        int[] sizes = new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        string[] variants = new string[] { "main", "quick", "undo" };
        string[] names = new string[] { "shell.ico", "shell-quick.ico", "shell-undo.ico" };
        for (int i = 0; i < variants.Length; ++i)
        {
            using (Bitmap master = Render(variants[i], 1024))
            {
                WriteIco(master, sizes, Path.Combine(outDir, names[i]));
                using (Bitmap p = Scale(master, 256))
                    p.Save(Path.Combine(outDir, "preview-" + variants[i] + ".png"), ImageFormat.Png);
                if (variants[i] == "main") WritePreview(master, Path.Combine(outDir, "preview-strip.png"));
            }
        }
        Console.WriteLine("icons written to " + outDir);
    }
}
