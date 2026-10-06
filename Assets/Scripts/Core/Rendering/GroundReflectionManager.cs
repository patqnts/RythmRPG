using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RythmRPG.Core
{
    /// <summary>Maintains mirrored renderers for explicitly paired casters and receivers.</summary>
    [AddComponentMenu("")]
    public sealed class GroundReflectionManager : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int PlaneYId = Shader.PropertyToID("_GroundReflectionPlaneY");
        private static readonly int SourceColorId = Shader.PropertyToID("_GroundReflectionSourceColor");
        private static readonly int StrengthId = Shader.PropertyToID("_GroundReflectionStrength");
        private static readonly int MaxLengthId = Shader.PropertyToID("_GroundReflectionMaxLength");
        private static readonly int TintId = Shader.PropertyToID("_GroundReflectionTint");
        private static readonly int DitherId = Shader.PropertyToID("_GroundReflectionDither");

        private sealed class Entry
        {
            public GroundReflectionCaster caster;
            public Renderer source;
            public Renderer proxy;
            public bool isSprite;
            public readonly MaterialPropertyBlock properties = new();
        }

        private readonly Dictionary<GroundReflectionCaster, Entry> entries = new();
        private readonly List<GroundReflectionCaster> dead = new();
        private readonly HashSet<GroundReflectionCaster> warnedUnsupported = new();
        private Material material;
        private float nextScan;

        private void OnEnable()
        {
            material = Resources.Load<Material>(GroundReflectionRuntime.MaterialResource);
            RenderPipelineManager.beginCameraRendering += BeforeCameraRendering;
            ApplySettings();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRendering;
            Clear();
        }

        private void LateUpdate()
        {
            GroundReflectionSettings settings = GroundReflectionRuntime.Settings;
            if (!settings.enabled || material == null)
            {
                SetAllEnabled(false);
                return;
            }

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + settings.rescanInterval;
                Discover();
                RemoveDeadEntries();
            }

            foreach (Entry entry in entries.Values)
                Sync(entry);
        }

        private void BeforeCameraRendering(ScriptableRenderContext _, Camera camera)
        {
            GroundReflectionSettings settings = GroundReflectionRuntime.Settings;
            if (!settings.enabled || camera == null || material == null) return;
            if (camera.cameraType is CameraType.Preview or CameraType.Reflection) return;

            foreach (Entry entry in entries.Values)
                SyncTransform(entry);
        }

        internal void ApplySettings()
        {
            GroundReflectionSettings settings = GroundReflectionRuntime.Settings;
            if (material == null)
                material = Resources.Load<Material>(GroundReflectionRuntime.MaterialResource);
            if (material == null) return;
            material.SetColor(TintId, settings.tint);
            material.SetFloat(StrengthId, settings.strength);
            material.SetFloat(MaxLengthId, settings.maxLength);
            material.SetFloat(DitherId, settings.dither);
            SetAllEnabled(settings.enabled);
        }

        private void Discover()
        {
            GroundReflectionCaster[] casters = FindObjectsByType<GroundReflectionCaster>(FindObjectsInactive.Exclude);
            foreach (GroundReflectionCaster caster in casters)
            {
                if (caster == null || entries.ContainsKey(caster) || warnedUnsupported.Contains(caster)) continue;
                Renderer source = caster.SourceRenderer;
                if (source is not SpriteRenderer && source is not MeshRenderer)
                {
                    Debug.LogWarning(
                        "Ground Reflection Caster needs a SpriteRenderer or MeshRenderer assigned as Source Renderer.",
                        caster);
                    warnedUnsupported.Add(caster);
                    continue;
                }

                Entry entry = CreateEntry(caster, source);
                if (entry != null) entries.Add(caster, entry);
                else warnedUnsupported.Add(caster);
            }
        }

        private Entry CreateEntry(GroundReflectionCaster caster, Renderer source)
        {
            GameObject go = new($"{source.name} Ground Reflection") { hideFlags = HideFlags.DontSave };
            go.AddComponent<GroundReflectionProxy>();

            Renderer proxy;
            bool isSprite = source is SpriteRenderer;
            if (source is SpriteRenderer)
            {
                proxy = go.AddComponent<SpriteRenderer>();
            }
            else
            {
                MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null)
                {
                    Debug.LogWarning("Ground Reflection Caster MeshRenderer requires a MeshFilter with a mesh.", caster);
                    Destroy(go);
                    return null;
                }

                MeshFilter proxyFilter = go.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = sourceFilter.sharedMesh;
                proxy = go.AddComponent<MeshRenderer>();
            }

            int materialCount = 1;
            if (source is MeshRenderer && source.TryGetComponent(out MeshFilter meshFilter) && meshFilter.sharedMesh != null)
                materialCount = Mathf.Max(1, meshFilter.sharedMesh.subMeshCount);
            Material[] reflectionMaterials = new Material[materialCount];
            for (int i = 0; i < reflectionMaterials.Length; i++) reflectionMaterials[i] = material;
            proxy.sharedMaterials = reflectionMaterials;
            proxy.shadowCastingMode = ShadowCastingMode.Off;
            proxy.receiveShadows = false;
            proxy.lightProbeUsage = LightProbeUsage.Off;
            proxy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            proxy.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Entry entry = new()
            {
                caster = caster,
                source = source,
                proxy = proxy,
                isSprite = isSprite
            };
            Sync(entry);
            return entry;
        }

        private void Sync(Entry entry)
        {
            GroundReflectionSettings settings = GroundReflectionRuntime.Settings;
            GroundReflectionCaster caster = entry.caster;
            Renderer source = entry.source;
            Renderer proxy = entry.proxy;
            GroundReflectionReceiver receiver = caster != null ? caster.Receiver : null;
            bool visible = settings.enabled && caster != null && caster.enabled && receiver != null && receiver.enabled &&
                           receiver.TargetRenderer != null && receiver.TargetRenderer.enabled && source != null &&
                           source.enabled && source.gameObject.activeInHierarchy && proxy != null;
            if (proxy != null) proxy.enabled = visible;
            if (!visible) return;

            proxy.sortingLayerID = source.sortingLayerID;
            proxy.sortingOrder = source.sortingOrder - 1;

            if (entry.isSprite)
            {
                SpriteRenderer sourceSprite = (SpriteRenderer)source;
                SpriteRenderer proxySprite = (SpriteRenderer)proxy;
                proxySprite.sprite = sourceSprite.sprite;
                proxySprite.color = sourceSprite.color;
                proxySprite.flipX = sourceSprite.flipX;
                proxySprite.flipY = sourceSprite.flipY;
                proxySprite.drawMode = sourceSprite.drawMode;
                proxySprite.size = sourceSprite.size;
                proxySprite.tileMode = sourceSprite.tileMode;
                proxySprite.adaptiveModeThreshold = sourceSprite.adaptiveModeThreshold;
                proxySprite.maskInteraction = SpriteMaskInteraction.None;
            }

            float maxLength = caster.maxLengthOverride > 0f ? caster.maxLengthOverride : settings.maxLength;
            entry.properties.Clear();
            entry.properties.SetFloat(PlaneYId, receiver.PlaneHeight);
            entry.properties.SetFloat(StrengthId, settings.strength * Mathf.Max(0f, caster.strength));
            entry.properties.SetFloat(MaxLengthId, Mathf.Max(0.1f, maxLength));
            entry.properties.SetColor(SourceColorId, Color.white);

            if (!entry.isSprite)
                CopyMeshMaterial(entry);

            proxy.SetPropertyBlock(entry.properties);
            SyncTransform(entry);
        }

        private static void CopyMeshMaterial(Entry entry)
        {
            Material sourceMaterial = entry.source.sharedMaterial;
            if (sourceMaterial == null) return;

            Texture texture = sourceMaterial.mainTexture;
            if (sourceMaterial.HasProperty("_BaseMap")) texture = sourceMaterial.GetTexture("_BaseMap");
            if (texture != null) entry.properties.SetTexture(MainTexId, texture);

            Vector2 scale = sourceMaterial.mainTextureScale;
            Vector2 offset = sourceMaterial.mainTextureOffset;
            entry.properties.SetVector(MainTexStId, new Vector4(scale.x, scale.y, offset.x, offset.y));

            Color color = Color.white;
            if (sourceMaterial.HasProperty("_BaseColor")) color = sourceMaterial.GetColor("_BaseColor");
            else if (sourceMaterial.HasProperty("_Color")) color = sourceMaterial.GetColor("_Color");
            entry.properties.SetColor(SourceColorId, color);
        }

        private static void SyncTransform(Entry entry)
        {
            if (entry.caster == null || entry.caster.Receiver == null || entry.source == null || entry.proxy == null)
                return;
            Transform source = entry.source.transform;
            Transform proxy = entry.proxy.transform;
            float planeY = entry.caster.Receiver.PlaneHeight;
            Vector3 position = source.position;
            position.y = 2f * planeY - position.y;
            proxy.SetPositionAndRotation(position, source.rotation);
            Vector3 scale = source.lossyScale;
            scale.y = -scale.y;
            proxy.localScale = scale;
            proxy.gameObject.layer = source.gameObject.layer;
        }

        private void RemoveDeadEntries()
        {
            dead.Clear();
            foreach (KeyValuePair<GroundReflectionCaster, Entry> pair in entries)
                if (pair.Key == null || pair.Value.source == null || pair.Value.proxy == null) dead.Add(pair.Key);
            foreach (GroundReflectionCaster caster in dead)
            {
                if (entries.TryGetValue(caster, out Entry entry) && entry.proxy != null)
                    Destroy(entry.proxy.gameObject);
                entries.Remove(caster);
            }
        }

        private void SetAllEnabled(bool value)
        {
            foreach (Entry entry in entries.Values)
                if (entry.proxy != null) entry.proxy.enabled = value && entry.source != null && entry.source.enabled;
        }

        private void Clear()
        {
            foreach (Entry entry in entries.Values)
                if (entry.proxy != null) Destroy(entry.proxy.gameObject);
            entries.Clear();
            warnedUnsupported.Clear();
        }
    }
}
