using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>Colours shared by the USS theme and the custom-painted views (timeline, simulator).</summary>
    public static class Palette
    {
        public static readonly Color Bg0 = Hex("#121419");
        public static readonly Color Bg1 = Hex("#191C23");
        public static readonly Color Bg2 = Hex("#21252E");
        public static readonly Color Bg3 = Hex("#2A2F3A");
        public static readonly Color Line = Hex("#303644");
        public static readonly Color LineStrong = Hex("#454D60");
        public static readonly Color Text = Hex("#E6E9F0");
        public static readonly Color TextDim = Hex("#8E96A8");
        public static readonly Color Accent = Hex("#5B8CFF");
        public static readonly Color Perfect = Hex("#FFD84A");
        public static readonly Color Good = Hex("#3DDC97");
        public static readonly Color Bad = Hex("#FF9F43");
        public static readonly Color Miss = Hex("#FF5C6C");
        public static readonly Color Warning = Hex("#FFB547");
        public static readonly Color Error = Hex("#FF5C6C");
        public static readonly Color PlayerTurn = Hex("#3DDC97");
        public static readonly Color Intro = Hex("#8E7CFF");
        public static readonly Color End = Hex("#FF7AA8");

        public static Color Hex(string hex)
        {
            float r, g, b;
            RythmRPG.LevelComposer.Types.NoteTypeDef.ParseHex(hex, out r, out g, out b);
            return new Color(r, g, b, 1f);
        }

        public static Color WithAlpha(Color c, float a) { return new Color(c.r, c.g, c.b, a); }

        public static Color Lighten(Color c, float amount)
        {
            return new Color(Mathf.Lerp(c.r, 1f, amount), Mathf.Lerp(c.g, 1f, amount), Mathf.Lerp(c.b, 1f, amount), c.a);
        }

        public static Color Darken(Color c, float amount)
        {
            return new Color(c.r * (1f - amount), c.g * (1f - amount), c.b * (1f - amount), c.a);
        }
    }

    /// <summary>Small builders so view code stays readable. Every control built here is non-focusable (except text inputs)
    /// so Space and the arrow keys keep driving the composer instead of "clicking" the last pressed button.</summary>
    public static class Ui
    {
        public static VisualElement Row(string cls = null)
        {
            var e = new VisualElement();
            e.AddToClassList("row");
            if (!string.IsNullOrEmpty(cls)) AddClasses(e, cls);
            return e;
        }

        public static VisualElement Col(string cls = null)
        {
            var e = new VisualElement();
            e.AddToClassList("col");
            if (!string.IsNullOrEmpty(cls)) AddClasses(e, cls);
            return e;
        }

        public static VisualElement Div(string cls)
        {
            var e = new VisualElement();
            AddClasses(e, cls);
            return e;
        }

        public static Label Text(string text, string cls = null)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) AddClasses(l, cls);
            return l;
        }

        public static Button Button(string text, Action onClick, string cls = null, string tip = null)
        {
            var b = new Button(onClick) { text = text };
            b.focusable = false;
            b.AddToClassList("btn");
            if (!string.IsNullOrEmpty(cls)) AddClasses(b, cls);
            if (!string.IsNullOrEmpty(tip)) Tooltips.Set(b, tip);
            return b;
        }

        public static VisualElement Spacer()
        {
            var e = new VisualElement();
            e.AddToClassList("spacer");
            return e;
        }

        public static VisualElement Separator()
        {
            var e = new VisualElement();
            e.AddToClassList("vsep");
            return e;
        }

        public static Label SectionTitle(string text)
        {
            return Text(text, "section-title");
        }

        public static void AddClasses(VisualElement e, string classes)
        {
            foreach (string c in classes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) e.AddToClassList(c);
        }

        public static void Show(VisualElement e, bool visible)
        {
            e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>True when a text input has keyboard focus (global shortcuts are ignored then).</summary>
        public static bool IsTyping(VisualElement root)
        {
            if (root == null || root.panel == null) return false;
            var focused = root.panel.focusController.focusedElement as VisualElement;
            if (focused == null) return false;
            if (focused is TextField) return true;
            return focused.GetFirstAncestorOfType<TextField>() != null;
        }

        public static string Num(double v, string format = "0.###")
        {
            return v.ToString(format, CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string s, out double v)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }

    /// <summary>A button that stays pressed ("on") until clicked again.</summary>
    public sealed class ToggleButton : Button
    {
        private bool on;
        public event Action<bool> Toggled;

        public ToggleButton(string text, bool initial, string tip = null)
        {
            this.text = text;
            focusable = false;
            AddToClassList("btn");
            AddToClassList("btn--toggle");
            if (!string.IsNullOrEmpty(tip)) Tooltips.Set(this, tip);
            clicked += () => { SetOn(!on, true); };
            SetOn(initial, false);
        }

        public bool On { get { return on; } }

        public void SetOn(bool value, bool notify)
        {
            on = value;
            EnableInClassList("on", value);
            if (notify && Toggled != null) Toggled(value);
        }
    }

    /// <summary>Label + text box for a number. Commits on Enter / focus loss, Up/Down arrows step, dragging the label scrubs.
    /// Shows a dash for "mixed" (several selected notes with different values).</summary>
    public sealed class NumberField : VisualElement
    {
        private readonly Label label;
        private readonly TextField input;
        private readonly Label suffix;
        private double value;
        private bool mixed;
        private bool dragging;
        private float dragStartX;
        private double dragStartValue;

        public double Min = double.NegativeInfinity;
        public double Max = double.PositiveInfinity;
        public double Step = 1d;
        public string Format = "0.###";
        public bool Integer;
        public event Action<double> Committed;
        /// <summary>Called while scrubbing (for live previews); <see cref="Committed"/> fires once at the end.</summary>
        public event Action<double> Scrubbing;

        public NumberField(string labelText, string suffixText = null, string tip = null)
        {
            AddToClassList("field");
            AddToClassList("number-field");
            label = new Label(labelText);
            label.AddToClassList("field__label");
            label.AddToClassList("field__label--scrub");
            Add(label);
            input = new TextField();
            input.isDelayed = true;
            input.AddToClassList("field__input");
            Add(input);
            suffix = new Label(suffixText ?? "");
            suffix.AddToClassList("field__suffix");
            if (!string.IsNullOrEmpty(suffixText)) Add(suffix);
            if (!string.IsNullOrEmpty(tip)) Tooltips.Set(this, tip);

            input.RegisterValueChangedCallback(evt => OnTextCommitted(evt.newValue));
            input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            label.RegisterCallback<PointerDownEvent>(OnLabelDown);
            label.RegisterCallback<PointerMoveEvent>(OnLabelMove);
            label.RegisterCallback<PointerUpEvent>(OnLabelUp);
        }

        public double Value { get { return value; } }
        public string LabelText { get { return label.text; } set { label.text = value; } }
        public string SuffixText { set { suffix.text = value ?? ""; } }

        public void SetValue(double v, bool isMixed = false)
        {
            value = v;
            mixed = isMixed;
            input.SetValueWithoutNotify(isMixed ? "—" : Ui.Num(v, Format));
        }

        public void SetEnabledState(bool enabled)
        {
            SetEnabled(enabled);
        }

        private double Clamp(double v)
        {
            if (Integer) v = Math.Round(v);
            if (v < Min) v = Min;
            if (v > Max) v = Max;
            return v;
        }

        private void OnTextCommitted(string text)
        {
            double v;
            if (!Ui.TryParse(text, out v))
            {
                SetValue(value, mixed);
                return;
            }

            Commit(Clamp(v));
        }

        private void Commit(double v)
        {
            value = v;
            mixed = false;
            input.SetValueWithoutNotify(Ui.Num(v, Format));
            if (Committed != null) Committed(v);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
            {
                double step = Step * (evt.shiftKey ? 10d : 1d);
                double current = value;
                double parsed;
                if (Ui.TryParse(input.value, out parsed)) current = parsed;
                Commit(Clamp(current + (evt.keyCode == KeyCode.UpArrow ? step : -step)));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                SetValue(value, mixed);
                input.Blur();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                // isDelayed commits on Enter; drop focus so global shortcuts work again.
                input.schedule.Execute(() => input.Blur());
            }
        }

        private void OnLabelDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || !enabledInHierarchy) return;
            dragging = true;
            dragStartX = evt.position.x;
            dragStartValue = value;
            label.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnLabelMove(PointerMoveEvent evt)
        {
            if (!dragging) return;
            float dx = evt.position.x - dragStartX;
            double v = Clamp(dragStartValue + Math.Round(dx / 6f) * Step);
            if (Math.Abs(v - value) < 1e-12) return;
            value = v;
            mixed = false;
            input.SetValueWithoutNotify(Ui.Num(v, Format));
            if (Scrubbing != null) Scrubbing(v);
        }

        private void OnLabelUp(PointerUpEvent evt)
        {
            if (!dragging) return;
            dragging = false;
            label.ReleasePointer(evt.pointerId);
            if (Math.Abs(value - dragStartValue) > 1e-12 && Committed != null) Committed(value);
        }
    }

    /// <summary>Label + single-line text box that commits on Enter / focus loss.</summary>
    public sealed class TextEntry : VisualElement
    {
        private readonly TextField input;
        public event Action<string> Committed;

        public TextEntry(string labelText, string tip = null)
        {
            AddToClassList("field");
            if (!string.IsNullOrEmpty(labelText))
            {
                var label = new Label(labelText);
                label.AddToClassList("field__label");
                Add(label);
            }

            input = new TextField();
            input.isDelayed = true;
            input.AddToClassList("field__input");
            Add(input);
            if (!string.IsNullOrEmpty(tip)) Tooltips.Set(this, tip);
            input.RegisterValueChangedCallback(evt => { if (Committed != null) Committed(evt.newValue); });
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) input.schedule.Execute(() => input.Blur());
            }, TrickleDown.TrickleDown);
        }

        public string Value { get { return input.value; } }
        public void SetValue(string v) { input.SetValueWithoutNotify(v ?? ""); }
        public TextField Input { get { return input; } }
    }

    /// <summary>Label + drop-down list.</summary>
    public sealed class ChoiceField : VisualElement
    {
        private readonly DropdownField dropdown;
        private List<string> choices;
        public event Action<int, string> Changed;

        public ChoiceField(string labelText, List<string> options, int index, string tip = null)
        {
            AddToClassList("field");
            var label = new Label(labelText);
            label.AddToClassList("field__label");
            Add(label);
            choices = options ?? new List<string>();
            dropdown = new DropdownField(choices, Mathf.Clamp(index, 0, Math.Max(0, choices.Count - 1)));
            dropdown.focusable = false;
            dropdown.AddToClassList("field__input");
            Add(dropdown);
            if (!string.IsNullOrEmpty(tip)) Tooltips.Set(this, tip);
            dropdown.RegisterValueChangedCallback(evt =>
            {
                int i = choices.IndexOf(evt.newValue);
                if (i >= 0 && Changed != null) Changed(i, evt.newValue);
            });
        }

        public void SetChoices(List<string> options, int index)
        {
            choices = options ?? new List<string>();
            dropdown.choices = choices;
            SetIndex(index);
        }

        public void SetIndex(int index)
        {
            if (index < 0 || index >= choices.Count) { dropdown.SetValueWithoutNotify("—"); return; }
            dropdown.SetValueWithoutNotify(choices[index]);
        }

        public int Index { get { return choices.IndexOf(dropdown.value); } }
    }

    /// <summary>Label + checkbox.</summary>
    public sealed class CheckField : VisualElement
    {
        private readonly Toggle toggle;
        public event Action<bool> Changed;

        public CheckField(string labelText, bool value, string tip = null)
        {
            AddToClassList("field");
            var label = new Label(labelText);
            label.AddToClassList("field__label");
            Add(label);
            toggle = new Toggle();
            toggle.focusable = false;
            toggle.value = value;
            toggle.AddToClassList("field__check");
            Add(toggle);
            if (!string.IsNullOrEmpty(tip)) Tooltips.Set(this, tip);
            toggle.RegisterValueChangedCallback(evt => { if (Changed != null) Changed(evt.newValue); });
        }

        public void SetValue(bool v, bool mixed = false)
        {
            toggle.SetValueWithoutNotify(v);
            toggle.showMixedValue = mixed;
        }
    }

    /// <summary>
    /// Runtime tooltips (UI Toolkit only shows the built-in <c>tooltip</c> in the Editor). Hover an element for half a
    /// second to see its hint in a floating label.
    /// </summary>
    public static class Tooltips
    {
        private static readonly Dictionary<VisualElement, string> texts = new Dictionary<VisualElement, string>();
        private static Label bubble;
        private static VisualElement layer;
        private static IVisualElementScheduledItem pending;
        private static VisualElement hovered;

        public static void Install(VisualElement root)
        {
            layer = root;
            bubble = new Label();
            bubble.AddToClassList("tooltip");
            bubble.pickingMode = PickingMode.Ignore;
            bubble.style.display = DisplayStyle.None;
            root.Add(bubble);
        }

        private static readonly HashSet<VisualElement> hooked = new HashSet<VisualElement>();

        public static void Set(VisualElement e, string text)
        {
            texts[e] = text;
            if (!hooked.Add(e)) return;
            e.RegisterCallback<PointerEnterEvent>(evt => Enter(e));
            e.RegisterCallback<PointerLeaveEvent>(evt => Leave(e));
            e.RegisterCallback<PointerDownEvent>(evt => Hide(), TrickleDown.TrickleDown);
            e.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (hovered == e) Hide();
                texts.Remove(e); // rebuilt views (inspector, step list) would otherwise pile up here
                hooked.Remove(e);
            });
        }

        private static void Enter(VisualElement e)
        {
            if (bubble == null) return;
            hovered = e;
            if (pending != null) pending.Pause();
            pending = e.schedule.Execute(() => Show(e)).StartingIn(550);
        }

        private static void Leave(VisualElement e)
        {
            if (hovered == e) Hide();
        }

        private static void Hide()
        {
            hovered = null;
            if (pending != null) pending.Pause();
            if (bubble != null) bubble.style.display = DisplayStyle.None;
        }

        private static void Show(VisualElement e)
        {
            string text;
            if (hovered != e || bubble == null || !texts.TryGetValue(e, out text) || string.IsNullOrEmpty(text)) return;
            bubble.text = text;
            bubble.style.display = DisplayStyle.Flex;
            bubble.BringToFront();
            Rect r = e.worldBound;
            Rect root = layer.worldBound;
            float x = Mathf.Clamp(r.x, 4f, Mathf.Max(4f, root.width - 320f));
            float y = r.yMax + 6f;
            if (y > root.height - 60f) y = r.y - 34f;
            bubble.style.left = x - root.x;
            bubble.style.top = y - root.y;
        }
    }
}
