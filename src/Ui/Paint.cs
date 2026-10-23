using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using NetPulse.App;

namespace NetPulse.Ui
{
    /// <summary>当前皮肤（主题 + 字号比例）。所有绘制从这里取色。</summary>
    internal static class Skin
    {
        public static Theme T = Theme.All[0];
        public static float Scale = 1f;

        public static void Apply(Theme theme, int fontPercent)
        {
            T = theme;
            Scale = UiFont.Scale(fontPercent);
        }

        /// <summary>换肤并通知所有监听者（各窗口自己重画）。</summary>
        public static event Action ThemeChanged;

        public static void Set(string key, int fontPercent)
        {
            Apply(Theme.Get(key), fontPercent);
            var h = ThemeChanged;
            if (h != null) { try { h(); } catch { } }
        }

        public static float Px(float value)
        {
            return value * Scale;
        }

        public static int Pxi(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static Font Cute(float px, FontStyle style = FontStyle.Regular)
        {
            return UiFont.Cute(px * Scale, style);
        }

        /// <summary>不乘界面字号的数字字体：任务栏组件有自己的一套尺寸。</summary>
        public static Font NumRaw(float px, FontStyle style = FontStyle.Regular)
        {
            return UiFont.Num(px, style);
        }

        /// <summary>不乘界面字号的正文字体。</summary>
        public static Font UiRaw(float px, FontStyle style = FontStyle.Regular)
        {
            return UiFont.Ui(px, style);
        }

        /// <summary>数字/英文用（Fredoka）。凡是带数字的文案都走这个，别用 Cute。</summary>
        public static Font Num(float px, FontStyle style = FontStyle.Regular)
        {
            return UiFont.Num(px * Scale, style);
        }

        public static Font Ui(float px, FontStyle style = FontStyle.Regular)
        {
            return UiFont.Ui(px * Scale, style);
        }

        public static Color Blend(Color a, Color b, double t)
        {
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c);
        }
    }

    /// <summary>圆角路径 / 渐变 / 柔和阴影。</summary>
    internal static class Round
    {
        public static GraphicsPath Make(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0.5f) { p.AddRectangle(r); return p; }

            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float radius, Color color)
        {
            using (var p = Make(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void Fill(Graphics g, Rectangle r, int radius, Color color)
        {
            Fill(g, new RectangleF(r.X, r.Y, r.Width, r.Height), radius, color);
        }

        public static void Stroke(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
        {
            if (width <= 0) return;
            using (var p = Make(r, radius))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        public static void Gradient(Graphics g, RectangleF r, float radius, Color from, Color to, float angle = 90f)
        {
            using (var p = Make(r, radius))
            using (var b = new LinearGradientBrush(r, from, to, angle))
                g.FillPath(b, p);
        }

        /// <summary>柔和投影：向外叠几层低透明度的圆角矩形。</summary>
        // 阴影：把「同心圆角矩形叠加」预渲染成一张位图缓存起来。
        // 之前每帧现画 14 圈路径，一次约 30-80ms；改用缓存后每帧只 DrawImage 一次。
        static readonly Dictionary<string, Bitmap> ShadowCache = new Dictionary<string, Bitmap>();

        public static void Shadow(Graphics g, RectangleF r, float radius, Color shadowColor, int spread = 14, float strength = 0.16f)
        {
            int w = (int)Math.Ceiling(r.Width), h = (int)Math.Ceiling(r.Height);
            if (w <= 0 || h <= 0) return;

            string key = w + "x" + h + "x" + (int)radius + "x" + shadowColor.ToArgb() + "x" + spread + "x" + strength.ToString("0.###");
            Bitmap bmp;
            if (!ShadowCache.TryGetValue(key, out bmp))
            {
                if (ShadowCache.Count > 16)
                {
                    foreach (var v in ShadowCache.Values) v.Dispose();
                    ShadowCache.Clear();
                }
                bmp = RenderShadow(w, h, radius, shadowColor, spread, strength);
                ShadowCache[key] = bmp;
            }

            g.DrawImage(bmp, r.X - spread, r.Y - spread, bmp.Width, bmp.Height);
        }

        static Bitmap RenderShadow(int w, int h, float radius, Color color, int spread, float strength)
        {
            var bmp = new Bitmap(w + spread * 2, h + spread * 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var bg = Graphics.FromImage(bmp))
            {
                bg.SmoothingMode = SmoothingMode.AntiAlias;
                bg.Clear(Color.Transparent);
                var body = new RectangleF(spread, spread, w, h);
                for (int i = spread; i >= 1; i--)
                {
                    float t = i / (float)spread;
                    int alpha = (int)(strength * 255 * Math.Pow(1 - t, 1.6) / spread * 6);
                    if (alpha <= 0) continue;
                    var rr = RectangleF.Inflate(body, i, i);
                    Fill(bg, rr, radius + i, Color.FromArgb(Math.Min(60, alpha), color));
                }
            }
            return bmp;
        }
    }

    /// <summary>文字排版小工具。</summary>
    internal static class Txt
    {
        public static readonly StringFormat Left = Make(StringAlignment.Near, StringAlignment.Center);
        public static readonly StringFormat Center = Make(StringAlignment.Center, StringAlignment.Center);
        public static readonly StringFormat Right = Make(StringAlignment.Far, StringAlignment.Center);
        public static readonly StringFormat TopLeft = Make(StringAlignment.Near, StringAlignment.Near);

        static StringFormat Make(StringAlignment h, StringAlignment v)
        {
            var f = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = h,
                LineAlignment = v,
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
            };
            return f;
        }

        public static void Draw(Graphics g, string text, Font font, Color color, RectangleF rect, StringFormat format = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            using (var b = new SolidBrush(color))
                g.DrawString(text, font, b, rect, format ?? Left);
        }

        /// <summary>带 1px 描边的文字，压在花哨背景上也看得清。</summary>
        public static void Outline(Graphics g, string text, Font font, Color color, Color outline,
            RectangleF rect, StringFormat format = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var f = format ?? Left;
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, rect, f);
                using (var pen = new Pen(outline, Math.Max(1.4f, font.Size * 0.11f)) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, path);
                using (var b = new SolidBrush(color))
                    g.FillPath(b, path);
            }
        }

        public static SizeF Measure(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            return g.MeasureString(text, font, new PointF(0, 0), StringFormat.GenericTypographic);
        }

        /// <summary>超宽自动截断加省略号。</summary>
        public static string Ellipsis(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (Measure(g, text, font).Width <= width) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string t = text.Substring(0, n) + "…";
                if (Measure(g, t, font).Width <= width) return t;
            }
            return "…";
        }
    }

    internal enum Glyph
    {
        Down, Up, Wifi, Gear, Chart, Clock, Power, Palette, Close, Check,
        ChevronDown, ChevronRight, Minus, Plus, Sparkle, Pin, Info, Trash, Moon, Heart, Arrow, Dot, ArrowUpRight
    }

    /// <summary>矢量小图标。全部用路径画，缩放不糊，不加载任何图片。</summary>
    internal static class Ico
    {
        public static void Draw(Graphics g, Glyph kind, RectangleF r, Color color, float weight = 1.7f)
        {
            float w = weight * Math.Max(0.75f, r.Width / 18f);
            using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
                float s = Math.Min(r.Width, r.Height);
                float h = s / 2f;

                switch (kind)
                {
                    case Glyph.Down:
                        g.DrawLine(pen, cx, r.Y + s * 0.08f, cx, r.Bottom - s * 0.30f);
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx - s * 0.26f, r.Bottom - s * 0.42f),
                            new PointF(cx, r.Bottom - s * 0.18f),
                            new PointF(cx + s * 0.26f, r.Bottom - s * 0.42f)
                        });
                        break;

                    case Glyph.Up:
                        g.DrawLine(pen, cx, r.Bottom - s * 0.08f, cx, r.Y + s * 0.30f);
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx - s * 0.26f, r.Y + s * 0.42f),
                            new PointF(cx, r.Y + s * 0.18f),
                            new PointF(cx + s * 0.26f, r.Y + s * 0.42f)
                        });
                        break;

                    case Glyph.Wifi:
                    {
                        float baseY = r.Bottom - s * 0.20f;
                        for (int i = 0; i < 3; i++)
                        {
                            float rr = s * (0.20f + i * 0.19f);
                            using (var arc = new GraphicsPath())
                            {
                                arc.AddArc(cx - rr, baseY - rr, rr * 2, rr * 2, 205, 130);
                                g.DrawPath(pen, arc);
                            }
                        }
                        using (var b = new SolidBrush(color))
                            g.FillEllipse(b, cx - w * 0.9f, baseY - w * 1.4f, w * 1.8f, w * 1.8f);
                        break;
                    }

                    case Glyph.Gear:
                    {
                        float rr = s * 0.20f;
                        using (var b = new SolidBrush(color)) g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            float x1 = cx + (float)Math.Cos(a) * (s * 0.28f);
                            float y1 = cy + (float)Math.Sin(a) * (s * 0.28f);
                            float x2 = cx + (float)Math.Cos(a) * (s * 0.42f);
                            float y2 = cy + (float)Math.Sin(a) * (s * 0.42f);
                            g.DrawLine(pen, x1, y1, x2, y2);
                        }
                        break;
                    }

                    case Glyph.Chart:
                        g.DrawLine(pen, r.X + s * 0.10f, r.Bottom - s * 0.14f, r.Right - s * 0.10f, r.Bottom - s * 0.14f);
                        g.DrawLine(pen, r.X + s * 0.22f, r.Bottom - s * 0.16f, r.X + s * 0.22f, r.Y + s * 0.44f);
                        g.DrawLine(pen, cx, r.Bottom - s * 0.16f, cx, r.Y + s * 0.22f);
                        g.DrawLine(pen, r.Right - s * 0.24f, r.Bottom - s * 0.16f, r.Right - s * 0.24f, r.Y + s * 0.36f);
                        break;

                    case Glyph.Clock:
                        g.DrawEllipse(pen, cx - h * 0.78f, cy - h * 0.78f, h * 1.56f, h * 1.56f);
                        g.DrawLine(pen, cx, cy - h * 0.42f, cx, cy);
                        g.DrawLine(pen, cx, cy, cx + h * 0.38f, cy + h * 0.18f);
                        break;

                    case Glyph.Power:
                        using (var arc = new GraphicsPath())
                        {
                            arc.AddArc(cx - h * 0.72f, cy - h * 0.66f, h * 1.44f, h * 1.44f, -55, 290);
                            g.DrawPath(pen, arc);
                        }
                        g.DrawLine(pen, cx, r.Y + s * 0.10f, cx, cy + h * 0.05f);
                        break;

                    case Glyph.Palette:
                        g.DrawEllipse(pen, cx - h * 0.80f, cy - h * 0.76f, h * 1.60f, h * 1.52f);
                        using (var b = new SolidBrush(color))
                        {
                            g.FillEllipse(b, cx - h * 0.52f, cy - h * 0.42f, w * 1.7f, w * 1.7f);
                            g.FillEllipse(b, cx + h * 0.10f, cy - h * 0.52f, w * 1.7f, w * 1.7f);
                            g.FillEllipse(b, cx - h * 0.10f, cy + h * 0.22f, w * 1.7f, w * 1.7f);
                        }
                        break;

                    case Glyph.Close:
                        g.DrawLine(pen, r.X + s * 0.28f, r.Y + s * 0.28f, r.Right - s * 0.28f, r.Bottom - s * 0.28f);
                        g.DrawLine(pen, r.Right - s * 0.28f, r.Y + s * 0.28f, r.X + s * 0.28f, r.Bottom - s * 0.28f);
                        break;

                    case Glyph.Check:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(r.X + s * 0.22f, cy + s * 0.02f),
                            new PointF(cx - s * 0.04f, r.Bottom - s * 0.26f),
                            new PointF(r.Right - s * 0.20f, r.Y + s * 0.26f)
                        });
                        break;

                    case Glyph.ChevronDown:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(r.X + s * 0.26f, cy - s * 0.10f),
                            new PointF(cx, cy + s * 0.16f),
                            new PointF(r.Right - s * 0.26f, cy - s * 0.10f)
                        });
                        break;

                    case Glyph.ChevronRight:
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx - s * 0.10f, r.Y + s * 0.26f),
                            new PointF(cx + s * 0.16f, cy),
                            new PointF(cx - s * 0.10f, r.Bottom - s * 0.26f)
                        });
                        break;

                    case Glyph.Minus:
                        g.DrawLine(pen, r.X + s * 0.26f, cy, r.Right - s * 0.26f, cy);
                        break;

                    case Glyph.Plus:
                        g.DrawLine(pen, r.X + s * 0.26f, cy, r.Right - s * 0.26f, cy);
                        g.DrawLine(pen, cx, r.Y + s * 0.26f, cx, r.Bottom - s * 0.26f);
                        break;

                    case Glyph.Sparkle:
                    {
                        var path = new GraphicsPath();
                        path.AddBezier(cx, r.Y + s * 0.12f, cx + s * 0.06f, cy - s * 0.06f, cx + s * 0.06f, cy - s * 0.06f, r.Right - s * 0.12f, cy);
                        path.AddBezier(r.Right - s * 0.12f, cy, cx + s * 0.06f, cy + s * 0.06f, cx + s * 0.06f, cy + s * 0.06f, cx, r.Bottom - s * 0.12f);
                        path.AddBezier(cx, r.Bottom - s * 0.12f, cx - s * 0.06f, cy + s * 0.06f, cx - s * 0.06f, cy + s * 0.06f, r.X + s * 0.12f, cy);
                        path.AddBezier(r.X + s * 0.12f, cy, cx - s * 0.06f, cy - s * 0.06f, cx - s * 0.06f, cy - s * 0.06f, cx, r.Y + s * 0.12f);
                        g.DrawPath(pen, path);
                        break;
                    }

                    case Glyph.Pin:
                        g.DrawLine(pen, cx, r.Y + s * 0.22f, cx, r.Bottom - s * 0.10f);
                        using (var b = new SolidBrush(color))
                            g.FillEllipse(b, cx - w * 0.9f, cy - w * 0.9f, w * 1.8f, w * 1.8f);
                        break;

                    case Glyph.Info:
                        g.DrawEllipse(pen, cx - h * 0.78f, cy - h * 0.78f, h * 1.56f, h * 1.56f);
                        g.DrawLine(pen, cx, cy - h * 0.10f, cx, cy + h * 0.42f);
                        using (var b = new SolidBrush(color))
                            g.FillEllipse(b, cx - w * 0.8f, cy - h * 0.50f, w * 1.6f, w * 1.6f);
                        break;

                    case Glyph.Trash:
                        g.DrawLine(pen, r.X + s * 0.22f, r.Y + s * 0.28f, r.Right - s * 0.22f, r.Y + s * 0.28f);
                        g.DrawLine(pen, cx - s * 0.10f, r.Y + s * 0.14f, cx + s * 0.10f, r.Y + s * 0.14f);
                        g.DrawLines(pen, new[]
                        {
                            new PointF(r.X + s * 0.30f, r.Y + s * 0.30f),
                            new PointF(r.X + s * 0.36f, r.Bottom - s * 0.16f),
                            new PointF(r.Right - s * 0.36f, r.Bottom - s * 0.16f),
                            new PointF(r.Right - s * 0.30f, r.Y + s * 0.30f)
                        });
                        break;

                    case Glyph.Moon:
                        using (var arc = new GraphicsPath())
                        {
                            arc.AddArc(cx - h * 0.72f, cy - h * 0.72f, h * 1.44f, h * 1.44f, 40, 250);
                            arc.AddArc(cx - h * 0.22f, cy - h * 0.62f, h * 1.20f, h * 1.24f, 150, -190);
                            g.DrawPath(pen, arc);
                        }
                        break;

                    case Glyph.Heart:
                    {
                        var path = new GraphicsPath();
                        path.AddBezier(cx, r.Bottom - s * 0.16f, r.X + s * 0.10f, cy + s * 0.06f, r.X + s * 0.16f, r.Y + s * 0.14f, cx, r.Y + s * 0.34f);
                        path.AddBezier(cx, r.Y + s * 0.34f, r.Right - s * 0.16f, r.Y + s * 0.14f, r.Right - s * 0.10f, cy + s * 0.06f, cx, r.Bottom - s * 0.16f);
                        g.DrawPath(pen, path);
                        break;
                    }

                    case Glyph.Arrow:
                        g.DrawLine(pen, r.X + s * 0.26f, cy, r.Right - s * 0.24f, cy);
                        g.DrawLines(pen, new[]
                        {
                            new PointF(r.Right - s * 0.44f, cy - s * 0.16f),
                            new PointF(r.Right - s * 0.22f, cy),
                            new PointF(r.Right - s * 0.44f, cy + s * 0.16f)
                        });
                        break;

                    case Glyph.ArrowUpRight:
                        g.DrawLine(pen, r.X + s * 0.28f, r.Bottom - s * 0.28f, r.Right - s * 0.28f, r.Y + s * 0.28f);
                        g.DrawLines(pen, new[]
                        {
                            new PointF(cx + s * 0.02f, r.Y + s * 0.28f),
                            new PointF(r.Right - s * 0.28f, r.Y + s * 0.28f),
                            new PointF(r.Right - s * 0.28f, cy - s * 0.02f)
                        });
                        break;

                    case Glyph.Dot:
                        using (var b = new SolidBrush(color))
                            g.FillEllipse(b, cx - s * 0.12f, cy - s * 0.12f, s * 0.24f, s * 0.24f);
                        break;
                }
            }
        }
    }
}
