using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using RythmRPG.Core;

namespace RythmRPG.Combat
{
    /// <summary>Choices belong to the selected ability; they do not occupy extra ability slots.</summary>
    public sealed class AbilityChoicePanel : MonoBehaviour
    {
        private GameObject canvasRoot;
        private RectTransform panel;
        private CombatController owner;
        private IReadOnlyList<CastChoice> choices;
        private Action<CastChoice> confirm;
        private Action cancel;
        private int selected;
        private float openedAt;
        private string title;
        private readonly List<GameObject> rows = new();

        public void Show(CombatController controller, AbilityRuntimeInstance ability, IReadOnlyList<CastChoice> options,
            Action<CastChoice> onConfirm, Action onCancel)
        {
            Close();
            owner = controller;
            choices = options;
            confirm = onConfirm;
            cancel = onCancel;
            selected = 0;
            openedAt = Time.unscaledTime;
            title = ability.Definition.DisplayName + "  •  " + ability.EffectiveManaCost + " MP";
            canvasRoot = new GameObject("Ability choice", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            Canvas canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = canvasRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var backdrop = Rect("Backdrop", (RectTransform)canvasRoot.transform, Vector2.zero, Vector2.zero);
            backdrop.anchorMin = Vector2.zero;
            backdrop.anchorMax = Vector2.one;
            backdrop.sizeDelta = Vector2.zero;
            backdrop.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, .7f);
            panel = Rect("Choices", (RectTransform)canvasRoot.transform, Vector2.zero, new Vector2(760, 650));
            panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.07f, .09f, .15f, .97f);
            ArtifactInterfaceView.Ensure(canvasRoot).FitProjection(panel);
            if (EventSystem.current == null)
            {
                var events = new GameObject("Ability choice events", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(canvasRoot.transform, false);
            }
            Render();
        }

        private void Update()
        {
            if (canvasRoot == null) return;
            if (owner == null || !owner.IsBattleActive || owner.CurrentState != CombatState.PlayerAbilitySelection) { Close(); return; }
            if (GamePause.IsPaused || Time.unscaledTime - openedAt < .2f) return;
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
            bool up = keyboard?.upArrowKey.wasPressedThisFrame == true || pad?.dpad.up.wasPressedThisFrame == true;
            bool down = keyboard?.downArrowKey.wasPressedThisFrame == true || pad?.dpad.down.wasPressedThisFrame == true;
            if (up || down) { selected = (selected + (up ? -1 : 1) + choices.Count) % choices.Count; Render(); }
            if (keyboard?.enterKey.wasPressedThisFrame == true || pad?.buttonSouth.wasPressedThisFrame == true) Choose(selected);
            else if (keyboard?.backspaceKey.wasPressedThisFrame == true || pad?.buttonEast.wasPressedThisFrame == true) Cancel();
        }

        private void Render()
        {
            foreach (GameObject row in rows) { row.SetActive(false); DestroyUi(row); }
            rows.Clear();
            Text(title, new Vector2(0, 275), new Vector2(700, 64), 30);
            Text("Choose how to use this ability", new Vector2(0, 226), new Vector2(700, 36), 23);
            int page = selected / 4;
            for (int index = page * 4; index < Math.Min(choices.Count, page * 4 + 4); index++)
            {
                int choiceIndex = index;
                CastChoice choice = choices[index];
                var row = Rect("Choice " + index, panel, new Vector2(0, 145 - (index % 4) * 100), new Vector2(700, 88));
                rows.Add(row.gameObject);
                var image = row.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.color = index == selected ? new Color(.2f, .35f, .44f) : new Color(.12f, .16f, .24f);
                var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => Choose(choiceIndex));
                Label(row, choice.Label + "\n<size=80%>" + choice.Description + "</size>", 24);
            }
            Text("↑ / ↓ choose   •   Enter / A confirm   •   Backspace / B cancel", new Vector2(0, -220), new Vector2(700, 36), 20);
            NavButton("Previous", -240, () => { selected = ((selected / 4 - 1 + (choices.Count + 3) / 4) % ((choices.Count + 3) / 4)) * 4; Render(); });
            NavButton("Cancel", 0, Cancel);
            NavButton("Next", 240, () => { selected = ((selected / 4 + 1) % ((choices.Count + 3) / 4)) * 4; Render(); });
            Text($"Page {page + 1} / {(choices.Count + 3) / 4}   •   Cancel costs nothing", new Vector2(0, -302), new Vector2(700, 26), 18);
        }

        private void NavButton(string title, float x, Action action)
        {
            var row = Rect(title, panel, new Vector2(x, -265), new Vector2(210, 45));
            rows.Add(row.gameObject);
            var image = row.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(.16f, .22f, .3f);
            var button = row.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { if (!GamePause.IsPaused) action(); });
            Label(row, title, 21);
        }

        private void Choose(int index)
        {
            if (canvasRoot == null || GamePause.IsPaused || Time.unscaledTime - openedAt < .2f || owner.CurrentState != CombatState.PlayerAbilitySelection) return;
            CastChoice choice = choices[index];
            Action<CastChoice> callback = confirm;
            Close();
            callback?.Invoke(choice);
        }
        private void Cancel() { Action callback = cancel; Close(); callback?.Invoke(); }
        private void Close()
        {
            if (canvasRoot != null) { canvasRoot.SetActive(false); DestroyUi(canvasRoot); }
            canvasRoot = null;
            rows.Clear();
            choices = null;
            confirm = null;
            cancel = null;
        }
        private void OnDisable() => Close();
        private static void DestroyUi(GameObject value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
        private void Text(string value, Vector2 position, Vector2 size, int fontSize)
        {
            var row = Rect("Text", panel, position, size);
            rows.Add(row.gameObject);
            Label(row, value, fontSize);
        }
        private static void Label(RectTransform parent, string value, int size)
        {
            var rect = Rect("Label", parent, Vector2.zero, parent.sizeDelta - new Vector2(24, 4));
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            ArtifactInterfaceStyle artifactStyle = ArtifactInterfaceStyle.Load();
            text.font = artifactStyle.bodyFont ?? BuildHudStyle.LoadOrDefault().FontAsset;
            text.text = value;
            text.fontSize = artifactStyle.BodyFontSize(size);
            text.color = artifactStyle.pearl;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
        }
        private static RectTransform Rect(string name, RectTransform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
