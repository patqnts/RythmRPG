using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class AbilitySlotView : MonoBehaviour
    {
        private const int Segments = 40;
        private SpriteRenderer iconRenderer;
        private UnityEngine.UI.Image uiIcon;
        private UnityEngine.UI.Image uiRadialFill;
        private Transform iconTransform;
        private LineRenderer radialFill;
        private Material radialMaterial;
        private Vector3 restingScale;
        private Vector3 iconRestingLocalPosition;
        private bool visible;

        private void Awake()
        {
            restingScale = transform.localScale;
            EnsureVisuals();
            SetVisible(false, false);
        }

        public void Configure(AbilityRuntimeInstance ability)
        {
            EnsureVisuals();
            Sprite icon = ability?.Definition?.Icon;
            if (uiIcon != null)
            {
                uiIcon.sprite = icon;
                uiIcon.enabled = visible && icon != null;
                Color uiColor = uiIcon.color;
                uiColor.a = ability == null ? 0.25f : 1f;
                uiIcon.color = uiColor;
                SetProgress(0f);
                return;
            }
            if (iconRenderer == null) return;
            iconRenderer.sprite = icon;
            iconRenderer.enabled = visible && icon != null;
            Color color = iconRenderer.color;
            color.a = ability == null ? 0.25f : 1f;
            iconRenderer.color = color;
            SetProgress(0f);
        }

        public void SetVisible(bool isVisible, bool animate)
        {
            EnsureVisuals();
            visible = isVisible;
            Tween.StopAll(iconTransform);
            if (iconTransform != null)
            {
                iconTransform.localPosition = iconRestingLocalPosition;
                iconTransform.localScale = Vector3.one;
            }
            if (iconRenderer != null)
            {
                iconRenderer.enabled = visible && iconRenderer.sprite != null;
                Color color = iconRenderer.color;
                color.a = visible ? Mathf.Max(color.a, 0.85f) : 0f;
                iconRenderer.color = color;
            }
            if (uiIcon != null)
            {
                uiIcon.enabled = visible && uiIcon.sprite != null;
                Color color = uiIcon.color;
                color.a = visible ? Mathf.Max(color.a, 0.85f) : 0f;
                uiIcon.color = color;
            }
            if (uiRadialFill != null)
            {
                uiRadialFill.enabled = visible;
                if (!visible) uiRadialFill.fillAmount = 0f;
            }
            if (radialFill != null)
            {
                radialFill.enabled = visible;
                if (!visible) radialFill.positionCount = 0;
            }
            if (!visible || iconTransform == null) return;

            if (animate)
            {
                iconTransform.localScale = Vector3.zero;
                Tween.Scale(iconTransform, Vector3.one, 0.18f, Ease.OutBack);
            }
            Tween.LocalPositionY(iconTransform, iconRestingLocalPosition.y + 0.14f, 0.85f,
                Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo);
        }

        public void SetProgress(float progress)
        {
            EnsureVisuals();
            if (uiRadialFill != null)
            {
                uiRadialFill.fillAmount = visible ? Mathf.Clamp01(progress) : 0f;
                return;
            }
            if (radialFill == null || !visible) return;
            float clamped = Mathf.Clamp01(progress);
            int count = Mathf.Max(0, Mathf.RoundToInt(Segments * clamped));
            radialFill.positionCount = count + (count > 0 ? 1 : 0);
            for (int index = 0; index <= count && radialFill.positionCount > 0; index++)
            {
                float angle = Mathf.Lerp(90f, -270f, (float)index / Segments) * Mathf.Deg2Rad;
                radialFill.SetPosition(index, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.58f);
            }
        }

        public void SetHighlighted(bool highlighted)
        {
            Tween.StopAll(transform);
            transform.localScale = restingScale;
            if (highlighted)
                Tween.Scale(transform, restingScale * 1.12f, 0.18f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo);
        }

        private void EnsureVisuals()
        {
            if (transform is RectTransform)
            {
                EnsureUIVisuals();
                return;
            }
            if (iconRenderer == null)
            {
                Transform icon = transform.Find("Ability Icon");
                if (icon == null)
                {
                    icon = new GameObject("Ability Icon").transform;
                    icon.SetParent(transform, false);
                    icon.localPosition = Vector3.up * 1.05f;
                }
                iconTransform = icon;
                iconRestingLocalPosition = icon.localPosition;
                iconRenderer = icon.GetComponent<SpriteRenderer>();
                if (iconRenderer == null) iconRenderer = icon.gameObject.AddComponent<SpriteRenderer>();
                SpriteRenderer owner = GetComponent<SpriteRenderer>();
                if (owner != null && iconRenderer != null)
                {
                    iconRenderer.sortingLayerID = owner.sortingLayerID;
                    iconRenderer.sortingOrder = owner.sortingOrder + 5;
                }
            }
            if (radialFill == null)
            {
                Transform radial = transform.Find("Selection Radial Fill");
                if (radial == null)
                {
                    radial = new GameObject("Selection Radial Fill").transform;
                    radial.SetParent(transform, false);
                    radial.localPosition = Vector3.up * 1.05f;
                }
                radialFill = radial.GetComponent<LineRenderer>();
                if (radialFill == null) radialFill = radial.gameObject.AddComponent<LineRenderer>();
                if (radialFill == null) return;
                radialFill.useWorldSpace = false;
                radialFill.loop = false;
                radialFill.widthMultiplier = 0.06f;
                Shader spriteShader = Shader.Find("Sprites/Default");
                if (spriteShader != null)
                {
                    radialMaterial = new Material(spriteShader);
                    radialFill.sharedMaterial = radialMaterial;
                }
                radialFill.startColor = radialFill.endColor = Color.white;
                radialFill.sortingOrder = iconRenderer != null ? iconRenderer.sortingOrder + 1 : 1;
            }
        }

        private void EnsureUIVisuals()
        {
            if (uiIcon == null)
            {
                Transform icon = transform.Find("Ability Icon");
                if (icon == null)
                {
                    GameObject iconObject = new("Ability Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                    icon = iconObject.transform;
                    icon.SetParent(transform, false);
                }
                RectTransform rect = icon as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 10f);
                rect.sizeDelta = new Vector2(54f, 54f);
                iconTransform = icon;
                iconRestingLocalPosition = icon.localPosition;
                uiIcon = icon.GetComponent<UnityEngine.UI.Image>();
                if (uiIcon == null)
                    uiIcon = icon.gameObject.AddComponent<UnityEngine.UI.Image>();
                uiIcon.preserveAspect = true;
                uiIcon.raycastTarget = false;
            }
            if (uiRadialFill == null)
            {
                Transform radial = transform.Find("Selection Radial Fill");
                if (radial == null)
                {
                    GameObject radialObject = new("Selection Radial Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
                    radial = radialObject.transform;
                    radial.SetParent(transform, false);
                }
                RectTransform rect = radial as RectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 6f);
                rect.sizeDelta = new Vector2(62f, 62f);
                uiRadialFill = radial.GetComponent<UnityEngine.UI.Image>();
                if (uiRadialFill == null)
                    uiRadialFill = radial.gameObject.AddComponent<UnityEngine.UI.Image>();
                uiRadialFill.type = UnityEngine.UI.Image.Type.Filled;
                uiRadialFill.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
                uiRadialFill.fillOrigin = 2;
                uiRadialFill.fillClockwise = true;
                uiRadialFill.color = new Color(1f, 1f, 1f, 0.45f);
                uiRadialFill.raycastTarget = false;
            }
        }

        private void OnDisable()
        {
            Tween.StopAll(transform);
            Tween.StopAll(iconTransform);
            if (restingScale != Vector3.zero) transform.localScale = restingScale;
            if (iconTransform != null) iconTransform.localPosition = iconRestingLocalPosition;
        }

        private void OnDestroy()
        {
            if (radialMaterial != null) Destroy(radialMaterial);
        }
    }
}
