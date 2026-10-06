using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RythmRPG.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RythmRPG.EditorTools
{
    /// <summary>Starting points for a new note: the visual setups the Note Designer creates.</summary>
    public enum NoteRecipe
    {
        /// <summary>A sprite with an Animator (approach / hit / miss animations).</summary>
        SpriteAnimator,
        /// <summary>A spawned effect: a laser from the enemy's socket to the lane, particles riding on the note...</summary>
        Effect,
        /// <summary>A line or chain from a socket on the enemy (a mimic's mouth) to the note.</summary>
        Chain,
        /// <summary>No visual of its own: the enemy steps in and strikes on the beat (melee enemies).</summary>
        EnemyIsTheNote,
        /// <summary>The enemy strikes and an effect plays from its socket (e.g. a slash wave).</summary>
        EnemyAndEffect,
        /// <summary>Just the Combat Note: add views yourself.</summary>
        Empty,
        /// <summary>The attacker throws a weapon at the lane, vanishes, warps to the weapon and slashes on the beat, then jumps back.</summary>
        ThrowAndWarp
    }

    [Serializable]
    public sealed class NoteRecipeOptions
    {
        public string name = "New Note";
        public string folder = "Assets/Prefab/Notes";
        public NoteKind kind = NoteKind.Tap;
        public NoteRecipe recipe = NoteRecipe.SpriteAnimator;

        [Header("Sprite")]
        public Sprite sprite;
        public RuntimeAnimatorController controller;
        public bool buildAnimatorFromFrames = true;
        public float framesPerSecond = 12f;
        public List<SpriteAnimatorBuilder.Phase> phases = SpriteAnimatorBuilder.DefaultPhases();

        [Header("Effect / Chain")]
        public GameObject effectPrefab;
        public string socket = "Mouth";
        public bool aimAtHitPoint = true;
        public Material chainMaterial;
        public GameObject chainSegment;

        [Header("Enemy")]
        public string windUpState = "Attack";
        [Min(0f)] public float windUpSeconds = 0.35f;
        public string strikeState = string.Empty;
        public string idleState = "Idle";
        public bool enemyStepsIn = true;

        [Header("Throw And Warp")]
        [Tooltip("The thrown weapon (art pointing right). Empty = a generated pixel sword.")]
        public Sprite weapon;
        public string throwState = "Throw";
        public string slashState = "Slash";
        public string jumpState = "Jump";
        [Tooltip("Socket the weapon leaves from (Hand). Empty = in front of the attacker's chest.")]
        public string handSocket = string.Empty;
        public VanishStyle vanishStyle = VanishStyle.Fade;
        public Color flakeColor = new(0.55f, 0.8f, 1f, 1f);
    }

    /// <summary>Creates Combat Note prefabs from recipes and converts the old note prefabs.</summary>
    public static class NoteKitAuthoring
    {
        public static string Describe(NoteRecipe recipe) => recipe switch
        {
            NoteRecipe.SpriteAnimator => "A sprite with an Animator: approach, hold, hit and miss animations (built from sprite frames, or your own controller).",
            NoteRecipe.Effect => "A spawned effect. Stationary kinds: from a socket on the enemy, aimed at the lane (lasers). Moving kinds: rides on the note.",
            NoteRecipe.Chain => "A line or chain from a socket on the enemy (Mouth...) to the note; it grows with the approach and pulls back when resolved.",
            NoteRecipe.EnemyIsTheNote => "No visual of its own: the enemy steps up to the lane, winds up and strikes on the beat, then walks back.",
            NoteRecipe.EnemyAndEffect => "The enemy strikes on the beat and an effect plays from its socket toward the lane.",
            NoteRecipe.ThrowAndWarp => "Warp strike: the attacker throws its weapon at the lane and vanishes into flakes, warps to the weapon just before it lands, slashes on the beat, then jumps back home.",
            _ => "Only the Combat Note: add views in the designer."
        };

        public static NoteKind DefaultKind(NoteRecipe recipe) => recipe switch
        {
            NoteRecipe.EnemyIsTheNote or NoteRecipe.EnemyAndEffect => NoteKind.Stationary,
            NoteRecipe.Effect => NoteKind.Stationary,
            NoteRecipe.ThrowAndWarp => NoteKind.Stationary,
            _ => NoteKind.Tap
        };

        // ---------- create ----------

        public static GameObject Create(NoteRecipeOptions options, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(options.name)) { error = "Give the note a name."; return null; }
            if (!options.folder.StartsWith("Assets", StringComparison.Ordinal)) { error = "The folder must be inside Assets."; return null; }
            SpriteAnimatorBuilder.CreateFolders(options.folder);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{options.folder}/{options.name}.prefab");

            var root = new GameObject(options.name);
            try
            {
                CombatNote note = root.AddComponent<CombatNote>();
                note.Kind = options.kind;
                bool stationary = NoteKinds.IsStationary(options.kind);
                switch (options.recipe)
                {
                    case NoteRecipe.SpriteAnimator:
                        AddSprite(root, note, options, Path.GetDirectoryName(path)?.Replace('\\', '/'));
                        break;
                    case NoteRecipe.Effect:
                        note.Views.Add(MakeEffect(options, stationary, NoteMoment.Spawned, 0f));
                        break;
                    case NoteRecipe.Chain:
                        if (options.sprite != null) AddStillSprite(root, options.sprite);
                        note.Views.Add(MakeChain(options, stationary));
                        break;
                    case NoteRecipe.EnemyIsTheNote:
                        AddEnemyStrike(note, options);
                        break;
                    case NoteRecipe.EnemyAndEffect:
                        AddEnemyStrike(note, options);
                        note.Views.Add(MakeEffect(options, true, NoteMoment.ReachedBeat, 0f));
                        break;
                    case NoteRecipe.ThrowAndWarp:
                        AddThrowAndWarp(root, note, options, Path.GetDirectoryName(path)?.Replace('\\', '/'));
                        break;
                }
                AddKindViews(root, note);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                return prefab;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
                return null;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AddSprite(GameObject root, CombatNote note, NoteRecipeOptions options, string prefabFolder)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            Animator animator = visual.AddComponent<Animator>();
            RuntimeAnimatorController controller = options.controller;
            List<SpriteAnimatorBuilder.Phase> phases = options.phases ?? new List<SpriteAnimatorBuilder.Phase>();
            if (controller == null && options.buildAnimatorFromFrames && phases.Any(p => p != null && p.HasFrames))
                controller = SpriteAnimatorBuilder.Build($"{prefabFolder}/{options.name} Animations", options.name, phases, options.framesPerSecond, string.Empty);
            animator.runtimeAnimatorController = controller;
            Sprite first = options.sprite != null ? options.sprite : phases.Where(p => p != null).SelectMany(p => p.frames).FirstOrDefault(f => f != null);
            renderer.sprite = first;

            var view = new AnimatorView { Animate = AnimatorView.Who.Note, NoteAnimator = animator };
            view.States.Clear();
            List<string> states = controller != null ? AnimatorClipLengths.StateNames(controller) : new List<string>();
            bool Has(string state) => states.Count == 0 || states.Contains(state);
            if (Has("Approach")) view.States.Add(new MomentState(NoteMoment.Spawned, "Approach"));
            if (NoteKinds.IsHold(note.Kind) && Has("Hold")) view.States.Add(new MomentState(NoteMoment.HoldStarted, "Hold"));
            if (Has("Hit")) view.States.Add(new MomentState(NoteMoment.Hit, "Hit"));
            if (Has("Miss")) view.States.Add(new MomentState(NoteMoment.Missed, "Miss"));
            note.Views.Add(view);
        }

        private static void AddStillSprite(GameObject root, Sprite sprite)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<SpriteRenderer>().sprite = sprite;
        }

        private static EffectView MakeEffect(NoteRecipeOptions options, bool fromEnemy, NoteMoment spawnMoment, float offset)
        {
            var view = new EffectView
            {
                Prefab = options.effectPrefab,
                SpawnAt = new NoteCue(spawnMoment, offset),
                StopAt = new NoteCue(NoteMoment.Resolved, 0f),
                Stop = EffectView.StopMode.StopEmitting
            };
            if (fromEnemy)
            {
                view.At = NoteAnchor.Source;
                view.Socket = options.socket ?? string.Empty;
                view.FollowMode = EffectView.Follow.Anchor;
                view.AimEnabled = options.aimAtHitPoint;
                view.AimAt = NoteAnchor.HitPoint;
            }
            else
            {
                view.At = NoteAnchor.Note;
                view.FollowMode = EffectView.Follow.Note;
            }
            return view;
        }

        private static LinkView MakeChain(NoteRecipeOptions options, bool stationary)
        {
            return new LinkView
            {
                From = NoteAnchor.Source,
                FromSocket = options.socket ?? string.Empty,
                To = stationary ? NoteAnchor.HitPoint : NoteAnchor.Note,
                ReachMode = stationary ? LinkView.Reach.GrowWithApproach : LinkView.Reach.Full,
                DrawStyle = options.chainSegment != null ? LinkView.Style.Segments : LinkView.Style.Line,
                LineMaterial = options.chainMaterial,
                SegmentPrefab = options.chainSegment,
                ShowAt = new NoteCue(NoteMoment.Spawned),
                RetractAt = new NoteCue(NoteMoment.Resolved)
            };
        }

        private static void AddEnemyStrike(CombatNote note, NoteRecipeOptions options)
        {
            var animator = new AnimatorView { Animate = AnimatorView.Who.Source };
            animator.States.Clear();
            if (!string.IsNullOrWhiteSpace(options.windUpState))
                animator.States.Add(new MomentState(NoteMoment.ReachedBeat, options.windUpState, string.Empty, -options.windUpSeconds));
            if (!string.IsNullOrWhiteSpace(options.strikeState))
                animator.States.Add(new MomentState(NoteMoment.ReachedBeat, options.strikeState));
            animator.AfterState = options.idleState ?? string.Empty;
            animator.AfterAt = new NoteCue(NoteMoment.Resolved, 0.3f);
            note.Views.Add(animator);
            if (!options.enemyStepsIn) return;
            note.Views.Add(new ActorMoveView
            {
                Actor = ActorMoveView.Who.Source,
                Destination = NoteAnchor.HitPoint,
                Offset = new Vector3(0f, 0f, -0.6f),
                MoveSeconds = 0.35f,
                MoveAt = new NoteCue(NoteMoment.ReachedBeat, -(options.windUpSeconds + 0.35f)),
                ReturnAt = new NoteCue(NoteMoment.Resolved, 0.15f)
            });
        }

        /// <summary>
        /// Warp strike, timed from the beat on the chart clock, so it looks the same whatever the travel time (until then the
        /// lane's key marker shows the approach) and plays out even when the player presses early:
        /// beat -0.9 s: throw animation; -0.75 s: the weapon leaves the hand and flies to the lane in an arc;
        /// -0.65 s: the attacker disintegrates into flakes; -0.1 s: the weapon disappears, the attacker reappears on it
        /// (teleport) and starts the slash; beat: the slash lands (shake); +0.3 s: it jumps back home; +0.75 s: idle.
        /// </summary>
        private static void AddThrowAndWarp(GameObject root, CombatNote note, NoteRecipeOptions options, string folder)
        {
            const float throwAt = -0.9f, departAt = -0.75f, vanishAt = -0.65f, warp = -0.1f;
            Sprite weapon = options.weapon != null ? options.weapon : PixelSword(folder);
            var visual = new GameObject("Weapon");
            visual.transform.SetParent(root.transform, false);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = weapon;
            renderer.sortingOrder = 5;

            var animator = new AnimatorView { Animate = AnimatorView.Who.Source };
            animator.States.Clear();
            if (!string.IsNullOrWhiteSpace(options.throwState)) animator.States.Add(new MomentState(NoteMoment.ReachedBeat, options.throwState, string.Empty, throwAt));
            if (!string.IsNullOrWhiteSpace(options.slashState))
                animator.States.Add(new MomentState(NoteMoment.ReachedBeat, options.slashState, string.Empty, warp));
            if (!string.IsNullOrWhiteSpace(options.jumpState))
                animator.States.Add(new MomentState(NoteMoment.ReachedBeat, options.jumpState, string.Empty, 0.3f));
            animator.AfterState = options.idleState ?? string.Empty;
            animator.AfterAt = new NoteCue(NoteMoment.ReachedBeat, 0.75f);
            note.Views.Add(animator);

            note.Views.Add(new TravelView
            {
                Visual = visual.transform,
                From = NoteAnchor.Source,
                FromSocket = options.handSocket ?? string.Empty,
                FromOffset = string.IsNullOrWhiteSpace(options.handSocket) ? new Vector3(0f, 0.6f, 0.25f) : Vector3.zero,
                To = NoteAnchor.HitPoint,
                ToOffset = new Vector3(0f, 0.45f, 0f),
                DepartAt = new NoteCue(NoteMoment.ReachedBeat, departAt),
                ArriveAt = new NoteCue(NoteMoment.ReachedBeat),
                ArcHeight = 0.35f,
                FacingMode = TravelView.Facing.AlongPath,
                HideBeforeDepart = true,
                HideAt = new NoteCue(NoteMoment.ReachedBeat, warp)
            });

            note.Views.Add(new ActorVanishView
            {
                Actor = ActorVanishView.Who.Source,
                Style = options.vanishStyle,
                VanishAt = new NoteCue(NoteMoment.ReachedBeat, vanishAt),
                VanishSeconds = 0.2f,
                AppearAt = new NoteCue(NoteMoment.ReachedBeat, warp),
                AppearSeconds = 0f,
                VanishFlakes = 18,
                AppearFlakes = 10,
                FlakeColor = options.flakeColor
            });

            note.Views.Add(new ActorMoveView
            {
                Actor = ActorMoveView.Who.Source,
                Destination = NoteAnchor.Projectile,
                Offset = new Vector3(0f, 0f, -0.35f),
                KeepHeight = true,
                MoveAt = new NoteCue(NoteMoment.ReachedBeat, warp),
                MoveSeconds = 0f,
                ReturnAt = new NoteCue(NoteMoment.ReachedBeat, 0.3f),
                ReturnSeconds = 0.4f,
                ReturnJumpHeight = 0.8f
            });

            note.Views.Add(new FeedbackView { At = new NoteCue(NoteMoment.ReachedBeat), Shake = 0.12f });
        }

        /// <summary>A 20 x 7 pixel sword pointing right (32 PPU, point filtered), saved next to the note. Reused if it exists.</summary>
        public static Sprite PixelSword(string folder)
        {
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            string path = $"{folder}/Warp Sword.png";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Color clear = new(0f, 0f, 0f, 0f);
            Color outline = new(0.08f, 0.1f, 0.2f, 1f);
            Color gold = new(0.95f, 0.75f, 0.25f, 1f);
            Color grip = new(0.35f, 0.22f, 0.15f, 1f);
            Color edge = new(0.62f, 0.78f, 1f, 1f);
            Color core = new(0.92f, 0.97f, 1f, 1f);
            Color shade = new(0.38f, 0.5f, 0.78f, 1f);
            const int width = 20, height = 7;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            void Set(int x, int y, Color c) => pixels[y * width + x] = c;
            // Pommel and grip (left), cross-guard, blade to the tip (right). Row 3 is the middle.
            Set(0, 3, gold); Set(1, 3, gold);
            for (int x = 2; x <= 5; x++) Set(x, 3, grip);
            for (int y = 1; y <= 5; y++) Set(6, y, gold);
            for (int x = 7; x <= 17; x++)
            {
                Set(x, 4, edge);
                Set(x, 3, core);
                Set(x, 2, shade);
            }
            Set(18, 3, core); Set(18, 4, edge); Set(19, 3, edge);
            // Outline around everything drawn.
            var drawn = (Color[])pixels.Clone();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (drawn[y * width + x].a > 0f) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            if (Mathf.Abs(dx) + Mathf.Abs(dy) != 1) continue;
                            int nx = x + dx, ny = y + dy;
                            near = nx >= 0 && ny >= 0 && nx < width && ny < height && drawn[ny * width + nx].a > 0f;
                        }
                    if (near) pixels[y * width + x] = outline;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            SpriteAnimatorBuilder.CreateFolders(folder);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 32f;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Views every note of a kind needs (hold tail, mash meter) when it has none yet.</summary>
        private static void AddKindViews(GameObject root, CombatNote note)
        {
            if (note.Kind == NoteKind.Hold && !note.Views.OfType<HoldTailView>().Any()) note.Views.Add(new HoldTailView());
            if (note.Kind == NoteKind.Mash && !note.Views.OfType<MashMeterView>().Any()) note.Views.Add(new MashMeterView());
        }

        // ---------- convert old prefabs ----------

        /// <summary>The kind an old note component plays as, or null when it is not a note (or already a Combat Note).</summary>
        public static NoteKind? LegacyKind(GameObject prefab)
        {
            Note note = prefab != null ? prefab.GetComponent<Note>() : null;
            return note switch
            {
                null => null,
                CombatNote => null,
                HoldLaserNote or StationaryHoldNote => NoteKind.StationaryHold,
                LaserNote or StationaryNote => NoteKind.Stationary,
                HoldNoteObject => NoteKind.Hold,
                MashNote => NoteKind.Mash,
                PongNote => NoteKind.Pong,
                _ => NoteKind.Tap
            };
        }

        /// <summary>
        /// Makes "<i>name</i> (Combat Note).prefab" next to an old note prefab: the old note component is replaced by a
        /// Combat Note of the same kind, its settings and visuals carried over as views. The old prefab is not touched.
        /// </summary>
        public static GameObject ConvertLegacy(GameObject prefab, out string error)
        {
            error = null;
            NoteKind? kind = LegacyKind(prefab);
            string source = AssetDatabase.GetAssetPath(prefab);
            if (kind == null || string.IsNullOrEmpty(source))
            {
                error = "Pick an old note prefab from the Project window (Note Object, Hold, Stationary, Laser, Mash, Pong...).";
                return null;
            }
            string folder = Path.GetDirectoryName(source)?.Replace('\\', '/') ?? "Assets";
            string target = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{prefab.name} (Combat Note).prefab");
            if (!AssetDatabase.CopyAsset(source, target))
            {
                error = "Could not copy the prefab.";
                return null;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(target);
            try
            {
                Note legacy = root.GetComponent<Note>();
                var old = new SerializedObject(legacy);
                Animator animator = Get<Animator>(old, "animator") ?? root.GetComponentInChildren<Animator>(true);
                bool laser = legacy is LaserNote || legacy is HoldLaserNote;
                var views = new List<NoteView>();
                var settings = new LegacySettings(old);
                Object.DestroyImmediate(legacy, true);

                CombatNote note = root.AddComponent<CombatNote>();
                note.Kind = kind.Value;
                settings.ApplyTo(note);
                switch (kind.Value)
                {
                    case NoteKind.Stationary:
                    case NoteKind.StationaryHold:
                        views.Add(settings.PhaseObjects());
                        if (animator != null) views.Add(settings.StationaryAnimator(animator, kind.Value == NoteKind.StationaryHold));
                        if (laser) views.Add(new BeamView { From = NoteAnchor.Note, To = NoteAnchor.HitPoint });
                        break;
                    case NoteKind.Hold:
                        views.Add(new HoldTailView { TailMode = settings.TailMode, CollapseSeconds = settings.CollapseSeconds });
                        if (animator != null) views.Add(DefaultAnimator(animator));
                        break;
                    case NoteKind.Mash:
                        views.Add(settings.MashMeter());
                        if (animator != null) views.Add(DefaultAnimator(animator));
                        break;
                    default:
                        if (animator != null) views.Add(DefaultAnimator(animator));
                        break;
                }
                note.Views.AddRange(views.Where(v => v != null));
                GameObject result = PrefabUtility.SaveAsPrefabAsset(root, target);
                return result;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
                return null;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static T Get<T>(SerializedObject from, string field) where T : Object
        {
            SerializedProperty property = from.FindProperty(field);
            return property != null && property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue as T : null;
        }

        /// <summary>Plays Approach / Hit / Miss when the controller has those states.</summary>
        private static AnimatorView DefaultAnimator(Animator animator)
        {
            var view = new AnimatorView { Animate = AnimatorView.Who.Note, NoteAnimator = animator };
            view.States.Clear();
            List<string> states = AnimatorClipLengths.StateNames(animator.runtimeAnimatorController);
            foreach ((NoteMoment moment, string state) in new[] { (NoteMoment.Spawned, "Approach"), (NoteMoment.HoldStarted, "Hold"),
                         (NoteMoment.Hit, "Hit"), (NoteMoment.Missed, "Miss") })
                if (states.Contains(state)) view.States.Add(new MomentState(moment, state));
            return view;
        }

        /// <summary>The old components' settings, read before they are removed.</summary>
        private sealed class LegacySettings
        {
            private readonly Dictionary<string, SerializedProperty> fields = new();
            private readonly SerializedObject source;

            public LegacySettings(SerializedObject old)
            {
                source = old;
                SerializedProperty it = old.GetIterator();
                if (!it.NextVisible(true)) return;
                do fields[it.name] = it.Copy();
                while (it.NextVisible(false));
                // Holds keep their fx in a nested class; read it now.
                holdFx = ReadFx(old.FindProperty("holdFx"));
                values = fields.ToDictionary(p => p.Key, p => Snapshot(p.Value));
            }

            private readonly Dictionary<string, object> values;
            private readonly HoldFxSettings holdFx;

            private static object Snapshot(SerializedProperty p) => p.propertyType switch
            {
                SerializedPropertyType.Boolean => p.boolValue,
                SerializedPropertyType.Float => p.floatValue,
                SerializedPropertyType.Integer => p.intValue,
                SerializedPropertyType.Enum => p.enumValueIndex,
                SerializedPropertyType.String => p.stringValue,
                SerializedPropertyType.Vector3 => p.vector3Value,
                SerializedPropertyType.Color => p.colorValue,
                SerializedPropertyType.ObjectReference => p.objectReferenceValue,
                _ => null
            };

            private static HoldFxSettings ReadFx(SerializedProperty fx)
            {
                if (fx == null) return null;
                var result = new HoldFxSettings();
                result.holdingEffectPrefab = fx.FindPropertyRelative("holdingEffectPrefab")?.objectReferenceValue as GameObject;
                result.builtInHoldingSparks = fx.FindPropertyRelative("builtInHoldingSparks")?.boolValue ?? true;
                result.completeEffectPrefab = fx.FindPropertyRelative("completeEffectPrefab")?.objectReferenceValue as GameObject;
                result.headHitEffectPrefab = fx.FindPropertyRelative("headHitEffectPrefab")?.objectReferenceValue as GameObject;
                SerializedProperty color = fx.FindPropertyRelative("markerFillColor");
                if (color != null) result.markerFillColor = color.colorValue;
                SerializedProperty lifetime = fx.FindPropertyRelative("oneShotLifetime");
                if (lifetime != null) result.oneShotLifetime = lifetime.floatValue;
                return result;
            }

            private T Value<T>(string name, T fallback) => values.TryGetValue(name, out object v) && v is T t ? t : fallback;
            private Transform Ref(string name) => Value<Object>(name, null) as Transform;

            public HoldNoteTailMode TailMode => (HoldNoteTailMode)Value("tailMode", 0);
            public float CollapseSeconds => Value("releaseCollapseSeconds", 0.12f);

            public void ApplyTo(CombatNote note)
            {
                note.Stationary.alignToLane = Value("alignToLaneX", true);
                note.Stationary.lanePositionOffset = Value("lanePositionOffset", Vector3.zero);
                note.Stationary.allowLatePress = Value("allowLatePress", true);
                note.Hold.damageOnEarlyRelease = Value("damagePlayerOnEarlyRelease", true);
                note.Hold.pinToHitLine = Value("pinTailToKeyMarker", true);
                if (holdFx != null) note.Hold.fx = holdFx;
                note.Mash.fallbackPresses = Value("fallbackRequiredPresses", note.Mash.fallbackPresses);
                note.Mash.perfectProgress = Value("perfectProgress", note.Mash.perfectProgress);
                note.Mash.goodProgress = Value("goodProgress", note.Mash.goodProgress);
            }

            public PhaseObjectsView PhaseObjects()
            {
                Transform approach = Ref("anticipationRootVisual");
                Transform fill = Ref("anticipationFillVisual");
                Transform window = Ref("hitWindowVisual");
                Transform hold = Ref("holdVisual");
                Transform end = Ref("endVisual");
                if (approach == null && fill == null && hold == null && end == null && window == null) return null;
                return new PhaseObjectsView
                {
                    Approach = approach != null ? approach.gameObject : null,
                    ApproachFill = fill,
                    FillFrom = Value("anticipationStartScale", Vector3.one * 0.15f),
                    FillTo = Value("anticipationEndScale", Vector3.one),
                    HoldObject = hold != null ? hold.gameObject : null,
                    HitObject = end != null ? end.gameObject : window != null ? window.gameObject : null
                };
            }

            public AnimatorView StationaryAnimator(Animator animator, bool hold)
            {
                var view = new AnimatorView { Animate = AnimatorView.Who.Note, NoteAnimator = animator };
                view.States.Clear();
                view.States.Add(new MomentState(NoteMoment.Spawned, Value("anticipationStateName", "Anticipation"), "Charge"));
                if (hold) view.States.Add(new MomentState(NoteMoment.HoldStarted, Value("holdStateName", "Hold")));
                view.States.Add(new MomentState(NoteMoment.Hit, Value("endStateName", "End")));
                view.States.Add(new MomentState(NoteMoment.Missed, Value("missStateName", "Miss")));
                return view;
            }

            public MashMeterView MashMeter() => new()
            {
                Meter = Ref("meterVisual"),
                PulseScale = Value("pressPulseScale", 0.25f),
                PulseDecay = Value("pressPulseDecay", 6f),
                Tint = Value("mashTint", new Color(1f, 0.85f, 0.2f, 1f))
            };
        }

        // ---------- charts ----------

        /// <summary>Makes <paramref name="prefab"/> the chart's prefab for the note type its kind plays as.</summary>
        public static void AssignToChart(Rhythm.RhythmChart chart, GameObject prefab, NoteKind kind)
        {
            if (chart == null || prefab == null) return;
            Undo.RecordObject(chart, "Assign Note Prefab");
            Rhythm.RhythmNoteType type = NoteKinds.ChartType(kind);
            Rhythm.RhythmNoteDefinition definition = chart.FindDefinition(type);
            if (definition == null)
            {
                definition = new Rhythm.RhythmNoteDefinition(type, Color.white);
                chart.NoteDefinitions.Add(definition);
            }
            definition.DefaultPrefab = prefab;
            EditorUtility.SetDirty(chart);
        }
    }
}
