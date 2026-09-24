using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.LevelComposer.App
{
    /// <summary>Shows one modal dialog at a time over the app, plus short toast messages.</summary>
    public sealed class ModalHost
    {
        private readonly VisualElement root;
        private readonly VisualElement backdrop;
        private readonly VisualElement toastLayer;
        private VisualElement current;
        private Action onEscape;
        private Action onEnter;

        public ModalHost(VisualElement root)
        {
            this.root = root;
            backdrop = Ui.Div("modal-backdrop");
            backdrop.style.display = DisplayStyle.None;
            backdrop.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == backdrop) { Escape(); evt.StopPropagation(); }
            });
            root.Add(backdrop);
            toastLayer = Ui.Div("toast-layer");
            toastLayer.pickingMode = PickingMode.Ignore;
            root.Add(toastLayer);
        }

        public bool IsOpen { get { return current != null; } }

        /// <summary>Opens <paramref name="panel"/> as the modal. Esc runs <paramref name="escape"/>, Enter runs <paramref name="enter"/>.</summary>
        public void Open(VisualElement panel, Action escape = null, Action enter = null)
        {
            Close();
            current = panel;
            onEscape = escape;
            onEnter = enter;
            panel.AddToClassList("modal");
            backdrop.Add(panel);
            backdrop.style.display = DisplayStyle.Flex;
            backdrop.BringToFront();
            toastLayer.BringToFront();
        }

        public void Close()
        {
            if (current != null) current.RemoveFromHierarchy();
            current = null;
            onEscape = null;
            onEnter = null;
            backdrop.style.display = DisplayStyle.None;
        }

        public void Escape()
        {
            Action a = onEscape;
            if (a != null) a(); else Close();
        }

        public void Enter()
        {
            Action a = onEnter;
            if (a != null) a();
        }

        /// <summary>Yes / No (/ Cancel) question. <paramref name="choices"/> are button labels; the first is the default (Enter).</summary>
        public void Ask(string title, string message, string[] choices, Action<int> answer, int cancelIndex = -1)
        {
            VisualElement panel = Ui.Col("dialog");
            panel.Add(Ui.Text(title, "dialog__title"));
            panel.Add(Ui.Text(message, "dialog__message"));
            VisualElement buttons = Ui.Row("dialog__buttons");
            buttons.Add(Ui.Spacer());
            for (int i = choices.Length - 1; i >= 0; i--)
            {
                int index = i;
                Button b = Ui.Button(choices[i], () => { Close(); if (answer != null) answer(index); }, i == 0 ? "btn--accent" : null);
                buttons.Add(b);
            }

            panel.Add(buttons);
            int esc = cancelIndex >= 0 ? cancelIndex : choices.Length - 1;
            Open(panel, () => { Close(); if (answer != null) answer(esc); }, () => { Close(); if (answer != null) answer(0); });
        }

        public void Info(string title, string message)
        {
            Ask(title, message, new[] { "OK" }, null);
        }

        /// <summary>Single text question.</summary>
        public void Prompt(string title, string label, string initial, Action<string> ok)
        {
            VisualElement panel = Ui.Col("dialog");
            panel.Add(Ui.Text(title, "dialog__title"));
            var entry = new TextField(label) { value = initial ?? "" };
            entry.AddToClassList("dialog__input");
            panel.Add(entry);
            VisualElement buttons = Ui.Row("dialog__buttons");
            buttons.Add(Ui.Spacer());
            Action confirm = () => { string v = entry.value; Close(); if (ok != null) ok(v); };
            buttons.Add(Ui.Button("Cancel", Close));
            buttons.Add(Ui.Button("OK", confirm, "btn--accent"));
            panel.Add(buttons);
            Open(panel, Close, confirm);
            entry.schedule.Execute(() => entry.Focus()).StartingIn(30);
        }

        /// <summary>A short message at the bottom of the window that fades after a few seconds.</summary>
        public void Toast(string message, ToastKind kind = ToastKind.Info)
        {
            Label t = Ui.Text(message, "toast");
            t.AddToClassList("toast--" + kind.ToString().ToLowerInvariant());
            toastLayer.Add(t);
            while (toastLayer.childCount > 4) toastLayer.RemoveAt(0);
            t.schedule.Execute(() => t.AddToClassList("toast--fade")).StartingIn(kind == ToastKind.Error ? 5000 : 2600);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(kind == ToastKind.Error ? 5600 : 3200);
        }
    }

    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }
}
