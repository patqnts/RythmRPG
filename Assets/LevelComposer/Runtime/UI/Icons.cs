using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    public enum IconKind
    {
        Play,
        Pause,
        Stop,
        ToStart,
        Plus,
        Minus,
        Up,
        Down,
        Duplicate,
        Trash,
        Gear,
        Help,
        Folder,
        Save,
        File,
        Speaker,
        Reload,
        Cross,
        Fit
    }

    /// <summary>Flat vector icons drawn with Painter2D (no textures or icon fonts needed).</summary>
    public sealed class Icon : VisualElement
    {
        private IconKind kind;
        public Color Tint = Palette.Text;

        public Icon(IconKind kind, float size = 14f)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("icon");
            style.width = size;
            style.height = size;
            generateVisualContent += Draw;
        }

        public IconKind Kind
        {
            get { return kind; }
            set { kind = value; MarkDirtyRepaint(); }
        }

        private void Draw(MeshGenerationContext mgc)
        {
            Painter2D p = mgc.painter2D;
            float s = Mathf.Min(contentRect.width, contentRect.height);
            if (s <= 0f || float.IsNaN(s)) return;
            float ox = (contentRect.width - s) * 0.5f, oy = (contentRect.height - s) * 0.5f;
            Color c = Tint;
            Func<float, float, Vector2> P = (x, y) => new Vector2(ox + x * s, oy + y * s);
            p.fillColor = c;
            p.strokeColor = c;
            p.lineWidth = Mathf.Max(1.5f, s * 0.12f);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            switch (kind)
            {
                case IconKind.Play:
                    Poly(p, P(0.22f, 0.12f), P(0.86f, 0.5f), P(0.22f, 0.88f));
                    break;
                case IconKind.Pause:
                    Poly(p, P(0.2f, 0.14f), P(0.4f, 0.14f), P(0.4f, 0.86f), P(0.2f, 0.86f));
                    Poly(p, P(0.6f, 0.14f), P(0.8f, 0.14f), P(0.8f, 0.86f), P(0.6f, 0.86f));
                    break;
                case IconKind.Stop:
                    Poly(p, P(0.18f, 0.18f), P(0.82f, 0.18f), P(0.82f, 0.82f), P(0.18f, 0.82f));
                    break;
                case IconKind.ToStart:
                    Poly(p, P(0.14f, 0.14f), P(0.28f, 0.14f), P(0.28f, 0.86f), P(0.14f, 0.86f));
                    Poly(p, P(0.86f, 0.14f), P(0.34f, 0.5f), P(0.86f, 0.86f));
                    break;
                case IconKind.Plus:
                    Lines(p, P(0.5f, 0.15f), P(0.5f, 0.85f), P(0.15f, 0.5f), P(0.85f, 0.5f));
                    break;
                case IconKind.Minus:
                    Lines(p, P(0.15f, 0.5f), P(0.85f, 0.5f));
                    break;
                case IconKind.Up:
                    Stroke(p, P(0.2f, 0.65f), P(0.5f, 0.3f), P(0.8f, 0.65f));
                    break;
                case IconKind.Down:
                    Stroke(p, P(0.2f, 0.35f), P(0.5f, 0.7f), P(0.8f, 0.35f));
                    break;
                case IconKind.Duplicate:
                    Stroke(p, P(0.32f, 0.12f), P(0.88f, 0.12f), P(0.88f, 0.68f), P(0.32f, 0.68f), P(0.32f, 0.12f));
                    Stroke(p, P(0.12f, 0.32f), P(0.12f, 0.88f), P(0.68f, 0.88f));
                    break;
                case IconKind.Trash:
                    Lines(p, P(0.15f, 0.25f), P(0.85f, 0.25f), P(0.4f, 0.12f), P(0.6f, 0.12f));
                    Stroke(p, P(0.25f, 0.3f), P(0.3f, 0.88f), P(0.7f, 0.88f), P(0.75f, 0.3f));
                    break;
                case IconKind.Gear:
                    p.BeginPath();
                    for (int i = 0; i < 16; i++)
                    {
                        float a = Mathf.PI * 2f * i / 16f;
                        float rr = i % 2 == 0 ? 0.46f : 0.34f;
                        Vector2 v = P(0.5f + Mathf.Cos(a) * rr, 0.5f + Mathf.Sin(a) * rr);
                        if (i == 0) p.MoveTo(v); else p.LineTo(v);
                    }

                    p.ClosePath();
                    p.Fill();
                    p.fillColor = Palette.Bg2;
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.5f), s * 0.14f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;
                case IconKind.Help:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.36f), s * 0.2f, Angle.Degrees(180f), Angle.Degrees(400f));
                    p.LineTo(P(0.5f, 0.62f));
                    p.Stroke();
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.84f), s * 0.07f, Angle.Degrees(0f), Angle.Degrees(360f));
                    p.Fill();
                    break;
                case IconKind.Folder:
                    Poly(p, P(0.1f, 0.25f), P(0.4f, 0.25f), P(0.48f, 0.34f), P(0.9f, 0.34f), P(0.9f, 0.82f), P(0.1f, 0.82f));
                    break;
                case IconKind.Save:
                    Poly(p, P(0.14f, 0.14f), P(0.72f, 0.14f), P(0.86f, 0.28f), P(0.86f, 0.86f), P(0.14f, 0.86f));
                    p.fillColor = Palette.Bg2;
                    Poly(p, P(0.3f, 0.14f), P(0.66f, 0.14f), P(0.66f, 0.36f), P(0.3f, 0.36f));
                    Poly(p, P(0.28f, 0.56f), P(0.72f, 0.56f), P(0.72f, 0.86f), P(0.28f, 0.86f));
                    break;
                case IconKind.File:
                    Stroke(p, P(0.22f, 0.1f), P(0.6f, 0.1f), P(0.8f, 0.3f), P(0.8f, 0.9f), P(0.22f, 0.9f), P(0.22f, 0.1f));
                    Lines(p, P(0.6f, 0.1f), P(0.6f, 0.3f), P(0.6f, 0.3f), P(0.8f, 0.3f));
                    break;
                case IconKind.Speaker:
                    Poly(p, P(0.1f, 0.38f), P(0.3f, 0.38f), P(0.55f, 0.15f), P(0.55f, 0.85f), P(0.3f, 0.62f), P(0.1f, 0.62f));
                    p.BeginPath();
                    p.Arc(P(0.55f, 0.5f), s * 0.28f, Angle.Degrees(-50f), Angle.Degrees(50f));
                    p.Stroke();
                    break;
                case IconKind.Reload:
                    p.BeginPath();
                    p.Arc(P(0.5f, 0.52f), s * 0.32f, Angle.Degrees(-60f), Angle.Degrees(230f));
                    p.Stroke();
                    Poly(p, P(0.62f, 0.08f), P(0.9f, 0.2f), P(0.66f, 0.38f));
                    break;
                case IconKind.Cross:
                    Lines(p, P(0.2f, 0.2f), P(0.8f, 0.8f), P(0.8f, 0.2f), P(0.2f, 0.8f));
                    break;
                case IconKind.Fit:
                    Stroke(p, P(0.1f, 0.35f), P(0.1f, 0.1f), P(0.35f, 0.1f));
                    Stroke(p, P(0.65f, 0.1f), P(0.9f, 0.1f), P(0.9f, 0.35f));
                    Stroke(p, P(0.9f, 0.65f), P(0.9f, 0.9f), P(0.65f, 0.9f));
                    Stroke(p, P(0.35f, 0.9f), P(0.1f, 0.9f), P(0.1f, 0.65f));
                    break;
            }
        }

        private static void Poly(Painter2D p, params Vector2[] pts)
        {
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.Fill();
        }

        private static void Stroke(Painter2D p, params Vector2[] pts)
        {
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.Stroke();
        }

        /// <summary>Separate segments: pairs of points.</summary>
        private static void Lines(Painter2D p, params Vector2[] pts)
        {
            p.BeginPath();
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                p.MoveTo(pts[i]);
                p.LineTo(pts[i + 1]);
            }

            p.Stroke();
        }

        /// <summary>A flat button showing an icon (and optional text).</summary>
        public static Button Button(IconKind kind, Action onClick, string tip, string text = null, string cls = null)
        {
            Button b = Ui.Button("", onClick, "btn--icon" + (cls != null ? " " + cls : ""), tip);
            var icon = new Icon(kind);
            b.Add(icon);
            if (!string.IsNullOrEmpty(text))
            {
                b.AddToClassList("btn--icon-text");
                Label l = Ui.Text(text, "btn__text");
                l.pickingMode = PickingMode.Ignore;
                b.Add(l);
            }

            return b;
        }
    }
}
