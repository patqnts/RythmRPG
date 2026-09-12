using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

namespace RythmRPG.Combat
{
    public sealed class AbilitySlotView : MonoBehaviour
    {
        private const int Segments = 40;
        private SpriteRenderer iconRenderer;
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
            if (iconRenderer == null) return;
            iconRenderer.sprite = ability?.Definition?.Icon;
            iconRenderer.enabled = visible && iconRenderer.sprite != null;
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
