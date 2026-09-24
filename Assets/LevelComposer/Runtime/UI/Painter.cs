using RythmRPG.LevelComposer.Types;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>Painter2D shortcuts for the custom-drawn views.</summary>
    public static class Painter
    {
        public static void Rect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }

        public static void RoundRect(Painter2D p, float x, float y, float w, float h, float r, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            if (r < 0.5f) { Rect(p, x, y, w, h, c); return; }
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x + r, y));
            p.LineTo(new Vector2(x + w - r, y));
            p.ArcTo(new Vector2(x + w, y), new Vector2(x + w, y + r), r);
            p.LineTo(new Vector2(x + w, y + h - r));
            p.ArcTo(new Vector2(x + w, y + h), new Vector2(x + w - r, y + h), r);
            p.LineTo(new Vector2(x + r, y + h));
            p.ArcTo(new Vector2(x, y + h), new Vector2(x, y + h - r), r);
            p.LineTo(new Vector2(x, y + r));
            p.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            p.ClosePath();
            p.Fill();
        }

        public static void RectOutline(Painter2D p, float x, float y, float w, float h, Color c, float width = 1f)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Stroke();
        }

        public static void Line(Painter2D p, float x0, float y0, float x1, float y1, Color c, float width = 1f)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, y1));
            p.Stroke();
        }

        public static void DashedLine(Painter2D p, float x0, float y, float x1, Color c, float width = 1f, float dash = 5f, float gap = 4f)
        {
            p.strokeColor = c;
            p.lineWidth = width;
            p.BeginPath();
            for (float x = x0; x < x1; x += dash + gap)
            {
                p.MoveTo(new Vector2(x, y));
                p.LineTo(new Vector2(Mathf.Min(x1, x + dash), y));
            }

            p.Stroke();
        }

        public static void Circle(Painter2D p, Vector2 c, float r, Color fill)
        {
            p.fillColor = fill;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        public static void Ring(Painter2D p, Vector2 c, float r, Color stroke, float width)
        {
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Stroke();
        }

        /// <summary>Arc from 12 o'clock clockwise, <paramref name="fraction"/> of a full turn (charge meters).</summary>
        public static void ArcProgress(Painter2D p, Vector2 c, float r, float fraction, Color stroke, float width)
        {
            if (fraction <= 0f) return;
            p.strokeColor = stroke;
            p.lineWidth = width;
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * Mathf.Clamp01(fraction)));
            p.Stroke();
        }

        /// <summary>Fills (and optionally outlines) a note glyph.</summary>
        public static void Shape(Painter2D p, NoteShape shape, Vector2 c, float r, Color fill, Color outline, float outlineWidth)
        {
            BuildShape(p, shape, c, r);
            p.fillColor = fill;
            p.Fill();
            if (outlineWidth > 0f)
            {
                BuildShape(p, shape, c, r);
                p.strokeColor = outline;
                p.lineWidth = outlineWidth;
                p.Stroke();
            }

            if (shape == NoteShape.Ring)
            {
                Circle(p, c, r * 0.42f, Palette.Bg0);
            }
        }

        private static void BuildShape(Painter2D p, NoteShape shape, Vector2 c, float r)
        {
            p.BeginPath();
            switch (shape)
            {
                case NoteShape.Diamond:
                    p.MoveTo(new Vector2(c.x, c.y - r * 1.15f));
                    p.LineTo(new Vector2(c.x + r * 1.15f, c.y));
                    p.LineTo(new Vector2(c.x, c.y + r * 1.15f));
                    p.LineTo(new Vector2(c.x - r * 1.15f, c.y));
                    break;
                case NoteShape.Square:
                    p.MoveTo(new Vector2(c.x - r * 0.9f, c.y - r * 0.9f));
                    p.LineTo(new Vector2(c.x + r * 0.9f, c.y - r * 0.9f));
                    p.LineTo(new Vector2(c.x + r * 0.9f, c.y + r * 0.9f));
                    p.LineTo(new Vector2(c.x - r * 0.9f, c.y + r * 0.9f));
                    break;
                case NoteShape.Triangle:
                    p.MoveTo(new Vector2(c.x, c.y - r * 1.1f));
                    p.LineTo(new Vector2(c.x + r * 1.05f, c.y + r * 0.8f));
                    p.LineTo(new Vector2(c.x - r * 1.05f, c.y + r * 0.8f));
                    break;
                case NoteShape.Hexagon:
                    for (int i = 0; i < 6; i++)
                    {
                        float a = Mathf.Deg2Rad * (60f * i);
                        var v = new Vector2(c.x + Mathf.Cos(a) * r * 1.05f, c.y + Mathf.Sin(a) * r * 1.05f);
                        if (i == 0) p.MoveTo(v); else p.LineTo(v);
                    }

                    break;
                case NoteShape.Star:
                    for (int i = 0; i < 10; i++)
                    {
                        float a = Mathf.Deg2Rad * (36f * i - 90f);
                        float rr = i % 2 == 0 ? r * 1.2f : r * 0.55f;
                        var v = new Vector2(c.x + Mathf.Cos(a) * rr, c.y + Mathf.Sin(a) * rr);
                        if (i == 0) p.MoveTo(v); else p.LineTo(v);
                    }

                    break;
                default:
                    p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
                    break;
            }

            p.ClosePath();
        }

        public static Color ColorOf(NoteTypeDef def)
        {
            if (def == null) return Palette.TextDim;
            float r, g, b;
            def.GetRgb(out r, out g, out b);
            return new Color(r, g, b, 1f);
        }
    }
}
