using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RythmRPG.Combat
{
    /// <summary>
    /// End-of-battle result screen: VICTORY/DEFEAT title, judgement breakdown with count-ups, max combo, accuracy,
    /// score, a grade letter that stamps down, badges (Full Combo, All Perfect, No Damage), combat details and a
    /// continue prompt. The first key press skips the animation, the next one closes it.
    ///
    /// Rows, wording, colors, timings and sounds come from <see cref="CombatResultStyle"/>; the grade ladder and
    /// scoring from <see cref="CombatResultGrading"/>. The parts below can be any uGUI objects, so a hand-made
    /// prefab works as long as its parts are assigned. <see cref="CreateTemplate"/> builds the default layout.
    /// </summary>
    public sealed class CombatResultScreen : MonoBehaviour
    {
        [Tooltip("Empty = Resources/Combat/UI/CombatResultStyle (or built-in defaults).")]
        [SerializeField] private CombatResultStyle style;

        [Header("Parts")]
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Graphic dim;
        [Tooltip("Shakes when the grade lands.")]
        [SerializeField] private RectTransform panel;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text subtitle;
        [SerializeField] private RectTransform mainRowsContainer;
        [SerializeField] private RectTransform detailRowsContainer;
        [Tooltip("Inactive row copied for every stat line.")]
        [SerializeField] private ResultStatRow rowTemplate;
        [SerializeField] private RectTransform gradeRoot;
        [SerializeField] private TMP_Text gradeLetter;
        [SerializeField] private TMP_Text gradeCaption;
        [SerializeField] private TMP_Text gradeComment;
        [SerializeField] private Graphic gradeFlash;
        [SerializeField] private RectTransform badgeContainer;
        [Tooltip("Inactive text copied for every badge.")]
        [SerializeField] private TMP_Text badgeTemplate;
        [SerializeField] private TMP_Text prompt;
        [SerializeField] private AudioSource audioSource;

        public event Action<CombatReport> Opened;
        public event Action<CombatReport> Closed;

        public bool IsOpen { get; private set; }
        public CombatReport Report { get; private set; }

        private sealed class Step
        {
            public float Start;
            public float Duration;
            public Action<float> Apply;
            public Action Begin;
            public bool Begun;
            public bool Done;
        }

        private readonly List<Step> steps = new();
        private readonly List<GameObject> spawned = new();
        private float openedAt;
        private float finishedAt = -1f;
        private bool closing;
        private bool closed;
        private bool stamped;
        private bool promptVisible;
        private float shakeStart = -1f;
        private Vector2 panelRest;
        private bool skipping;

        private CombatResultStyle Style
        {
            get
            {
                if (style == null) style = CombatResultStyle.LoadOrDefault();
                return style;
            }
        }

        private bool Finished
        {
            get
            {
                foreach (Step step in steps)
                    if (!step.Done) return false;
                return true;
            }
        }

        private void Awake()
        {
            if (!IsOpen) HideImmediate();
        }

        /// <summary>Opens the screen for the report and waits until the player closes it.</summary>
        public IEnumerator Show(CombatReport report)
        {
            Open(report);
            if (!isActiveAndEnabled)
            {
                // A parent is switched off, so Update would never run: don't block the battle.
                Debug.LogWarning("CombatResultScreen could not open because a parent object is inactive.", this);
                IsOpen = false;
                yield break;
            }
            while (!closed) yield return null;
        }

        public void Open(CombatReport report)
        {
            if (report == null) return;
            // IsOpen first: activating a screen for the first time runs Awake, which hides it when not open.
            IsOpen = true;
            Report = report;
            gameObject.SetActive(true);
            closing = false;
            closed = false;
            stamped = false;
            promptVisible = false;
            skipping = false;
            shakeStart = -1f;
            finishedAt = -1f;
            steps.Clear();
            ClearSpawned();
            if (panel != null) panelRest = panel.anchoredPosition;
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = true;
            }
            openedAt = Time.unscaledTime;
            Build(report);
            Play(report.Victory ? Style.VictoryJingle : Style.DefeatJingle);
            Opened?.Invoke(report);
        }

        /// <summary>Jumps every animation to its end (the prompt then shows).</summary>
        public void SkipToEnd()
        {
            skipping = true;
            foreach (Step step in steps)
            {
                if (step.Done) continue;
                if (!step.Begun)
                {
                    step.Begun = true;
                    step.Begin?.Invoke();
                }
                step.Apply?.Invoke(1f);
                step.Done = true;
            }
            skipping = false;
            shakeStart = -1f;
        }

        public void Close()
        {
            if (!IsOpen || closing) return;
            SkipToEnd();
            closing = true;
            float from = group != null ? group.alpha : 1f;
            Add(Time.unscaledTime, Style.FadeOutSeconds, t =>
            {
                if (group != null) group.alpha = Mathf.Lerp(from, 0f, t);
            });
        }

        private void HideImmediate()
        {
            if (group != null)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
        }

        // ---------- timeline ----------

        private void Build(CombatReport report)
        {
            CombatResultStyle s = Style;
            float t0 = Time.unscaledTime;

            if (dim != null) dim.color = s.DimColor;
            Add(t0, s.FadeInSeconds, t =>
            {
                if (group != null) group.alpha = t;
            });

            // Title and subtitle.
            Color titleColor = report.Victory ? s.VictoryColor : s.DefeatColor;
            if (title != null)
            {
                title.text = report.Victory ? s.VictoryTitle : s.DefeatTitle;
                title.fontSize = s.TitleSize;
                Add(t0 + s.TitleDelay, s.TitleSeconds, t =>
                {
                    title.color = WithAlpha(titleColor, Mathf.Clamp01(t * 2f));
                    title.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.6f, 1f, EaseOutBack(t));
                });
            }
            if (subtitle != null)
            {
                bool hasSubtitle = !string.IsNullOrEmpty(s.SubtitleFormat) && !string.IsNullOrEmpty(report.EnemyName);
                subtitle.text = hasSubtitle ? string.Format(s.SubtitleFormat, report.EnemyName) : string.Empty;
                subtitle.fontSize = s.SubtitleSize;
                Color color = s.LabelColor;
                Add(t0 + s.TitleDelay + 0.15f, 0.3f, t => subtitle.color = WithAlpha(color, t));
            }

            // Stat rows.
            float rowStart = t0 + s.RowsDelay;
            float lastMain = AddRows(report, s.MainRows, mainRowsContainer, s.RowSize, rowStart);
            float detailStart = lastMain + s.RowStagger + 0.1f;
            float lastDetail = AddRows(report, s.DetailRows, detailRowsContainer, s.DetailSize, detailStart);

            // Grade stamp.
            float gradeStart = Mathf.Max(lastMain + s.CountSeconds, detailStart) + s.GradeDelay;
            CombatGrade grade = report.Grade ?? new CombatGrade { label = "-" };
            if (gradeLetter != null)
            {
                ApplyRankLook(s, gradeLetter, grade.label);
                gradeLetter.text = grade.label;
                gradeLetter.fontSize = s.GradeSize;
                gradeLetter.color = WithAlpha(grade.color, 0f);
            }
            if (gradeRoot != null || gradeLetter != null)
            {
                Add(gradeStart, s.GradeStampSeconds, t =>
                {
                    float scale = Mathf.LerpUnclamped(s.GradeStartScale, 1f, s.EvaluateStamp(t));
                    if (gradeRoot != null) gradeRoot.localScale = Vector3.one * scale;
                    if (gradeLetter != null) gradeLetter.color = WithAlpha(grade.color, Mathf.Clamp01(t * 3f));
                });
            }
            float landed = gradeStart + s.GradeStampSeconds;
            if (gradeFlash != null) gradeFlash.color = WithAlpha(Color.white, 0f);
            Add(landed, 0.3f, t =>
            {
                if (gradeFlash != null) gradeFlash.color = WithAlpha(Color.white, 0.9f * (1f - t));
            }, () =>
            {
                stamped = true;
                if (!skipping)
                {
                    shakeStart = Time.unscaledTime;
                    Play(Style.GradeSlam);
                }
            }, applyStart: false);
            if (gradeCaption != null)
            {
                gradeCaption.text = s.GradeCaption;
                Color color = s.LabelColor;
                Add(gradeStart - 0.2f, 0.3f, t => gradeCaption.color = WithAlpha(color, t));
            }
            if (gradeComment != null)
            {
                gradeComment.text = grade.comment ?? string.Empty;
                Add(landed, 0.35f, t => gradeComment.color = WithAlpha(grade.color, t));
            }

            // Badges.
            var badges = new List<string>();
            if (report.AllPerfect && !string.IsNullOrEmpty(s.AllPerfectBadge)) badges.Add(s.AllPerfectBadge);
            else if (report.FullCombo && !string.IsNullOrEmpty(s.FullComboBadge)) badges.Add(s.FullComboBadge);
            if (report.Victory && report.DamageTaken == 0 && !string.IsNullOrEmpty(s.NoDamageBadge)) badges.Add(s.NoDamageBadge);
            float badgeStart = landed + 0.15f;
            for (int i = 0; i < badges.Count; i++) AddBadge(badges[i], badgeStart + i * s.BadgeStagger);

            // Continue prompt.
            float promptStart = Mathf.Max(lastDetail + s.RowSlideSeconds, badgeStart + badges.Count * s.BadgeStagger) + s.PromptDelay;
            if (prompt != null)
            {
                prompt.text = s.ContinuePrompt;
                prompt.fontSize = s.PromptSize;
                prompt.color = WithAlpha(s.PromptColor, 0f);
            }
            Add(promptStart, 0f, t => promptVisible = t >= 1f);
        }

        /// <summary>Adds rows starting at <paramref name="start"/>; returns when the last row starts.</summary>
        private float AddRows(CombatReport report, IReadOnlyList<ResultRowDefinition> rows, RectTransform container,
            int fontSize, float start)
        {
            CombatResultStyle s = Style;
            float last = start;
            if (rows == null || container == null || rowTemplate == null) return last;
            int index = 0;
            foreach (ResultRowDefinition definition in rows)
            {
                if (definition == null) continue;
                ResultStatRow row = Instantiate(rowTemplate, container, false);
                row.gameObject.SetActive(true);
                row.name = definition.label;
                spawned.Add(row.gameObject);
                Color labelColor = definition.color.a > 0f ? definition.color : s.LabelColor;
                row.Setup(definition.label, labelColor, s.ValueColor, fontSize);

                float at = start + index * s.RowStagger;
                last = at;
                RectTransform content = row.Content;
                Vector2 rest = content.anchoredPosition;
                CanvasGroup rowGroup = row.Group;
                Add(at, s.RowSlideSeconds, t =>
                {
                    content.anchoredPosition = rest + new Vector2(-s.RowSlideDistance * (1f - EaseOutCubic(t)), 0f);
                    rowGroup.alpha = t;
                }, () =>
                {
                    if (!skipping) Play(s.RowTick);
                });

                CombatStat stat = definition.stat;
                if (CombatReport.IsNumeric(stat))
                    Add(at, s.CountSeconds, t => row.SetValue(report.Format(stat, EaseOutCubic(t))));
                else
                    row.SetValue(report.Format(stat));
                index++;
            }
            return last;
        }

        private void AddBadge(string text, float at)
        {
            if (badgeTemplate == null || badgeContainer == null) return;
            TMP_Text badge = Instantiate(badgeTemplate, badgeContainer, false);
            badge.gameObject.SetActive(true);
            spawned.Add(badge.gameObject);
            badge.text = text;
            badge.fontSize = Style.BadgeSize;
            Color color = Style.BadgeColor;
            RectTransform rect = badge.rectTransform;
            Add(at, Style.BadgeSeconds, t =>
            {
                rect.localScale = Vector3.one * Mathf.Max(0f, EaseOutBack(t));
                badge.color = WithAlpha(color, Mathf.Clamp01(t * 2f));
            }, () =>
            {
                if (!skipping) Play(Style.BadgePop);
            });
        }

        /// <summary>
        /// Rank letter look: the Combat Rank SDF font with the grade's material (SS iridescent, S gold, A emerald...),
        /// paragraph texture mapping so SS shares one finish, and the TexCoord1 canvas channel the rank shader needs.
        /// </summary>
        private static void ApplyRankLook(CombatResultStyle s, TMP_Text letter, string label)
        {
            if (s == null || letter == null) return;
            TMP_FontAsset rankFont = s.RankFont;
            if (rankFont == null) return;
            if (letter.font != rankFont) letter.font = rankFont;
            Material material = s.GetRankMaterial(label);
            letter.fontSharedMaterial = material != null ? material : rankFont.material;
            if (s.RankParagraphMapping)
            {
                letter.horizontalMapping = TextureMappingOptions.Paragraph;
                letter.verticalMapping = TextureMappingOptions.Paragraph;
            }
            letter.extraPadding = true;
            Canvas canvas = letter.canvas;
            if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        }

        /// <summary>Schedules an animation. applyStart puts it in its t=0 state right away (hidden, zero...).</summary>
        private void Add(float start, float duration, Action<float> apply, Action begin = null, bool applyStart = true)
        {
            var step = new Step { Start = start, Duration = duration, Apply = apply, Begin = begin };
            if (applyStart) apply?.Invoke(0f);
            steps.Add(step);
        }

        private void Update()
        {
            if (!IsOpen) return;
            CombatResultStyle s = Style;
            float now = Time.unscaledTime;

            foreach (Step step in steps.ToArray())
            {
                if (step.Done || now < step.Start) continue;
                if (!step.Begun)
                {
                    step.Begun = true;
                    step.Begin?.Invoke();
                }
                float t = step.Duration <= 0f ? 1f : Mathf.Clamp01((now - step.Start) / step.Duration);
                step.Apply?.Invoke(t);
                if (t >= 1f) step.Done = true;
            }

            if (panel != null)
            {
                Vector2 offset = Vector2.zero;
                if (shakeStart >= 0f && s.StampShakeSeconds > 0f)
                {
                    float t = (now - shakeStart) / s.StampShakeSeconds;
                    if (t >= 1f) shakeStart = -1f;
                    else
                    {
                        float amplitude = s.StampShakePixels * (1f - t) * (1f - t);
                        offset = new Vector2(Mathf.Sin(now * 90f), Mathf.Cos(now * 67f)) * amplitude;
                    }
                }
                panel.anchoredPosition = panelRest + new Vector2(Mathf.Round(offset.x), Mathf.Round(offset.y));
            }

            if (stamped && gradeRoot != null && !closing)
            {
                float wave = Mathf.Sin((now / s.GradePulsePeriod) * Mathf.PI * 2f);
                gradeRoot.localScale = Vector3.one * (1f + wave * s.GradePulse);
            }

            if (prompt != null)
            {
                float blink = promptVisible && !closing
                    ? 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(now / s.PromptBlinkPeriod * Mathf.PI * 2f))
                    : 0f;
                prompt.color = WithAlpha(s.PromptColor, s.PromptColor.a * blink);
            }

            bool finished = Finished;
            if (closing)
            {
                if (finished) FinishClose();
                return;
            }
            if (finished && finishedAt < 0f) finishedAt = now;

            if (now >= openedAt + s.InputDelay && Input.anyKeyDown)
            {
                if (!finished && s.PressToSkip) SkipToEnd();
                else if (finished) Close();
            }
            else if (finished && s.AutoCloseSeconds > 0f && now >= finishedAt + s.AutoCloseSeconds)
            {
                Close();
            }
        }

        private void FinishClose()
        {
            IsOpen = false;
            closing = false;
            closed = true;
            ClearSpawned();
            if (panel != null) panel.anchoredPosition = panelRest;
            CombatReport report = Report;
            HideImmediate();
            Closed?.Invoke(report);
        }

        private void ClearSpawned()
        {
            foreach (GameObject item in spawned)
                if (item != null) Destroy(item);
            spawned.Clear();
        }

        private void OnDisable()
        {
            // Switched off mid-show (scene change, battle cancelled): let a waiting Show() finish.
            if (IsOpen)
            {
                IsOpen = false;
                closed = true;
            }
        }

        private void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
            audioSource.PlayOneShot(clip, Style.Volume);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        private static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        // ---------- template ----------

        /// <summary>
        /// Builds the default screen: its own overlay canvas (drawn over everything), a framed panel, title,
        /// judgement rows on the left, a diamond grade badge on the right, details in a grid along the bottom.
        /// </summary>
        public static CombatResultScreen CreateTemplate(CombatResultStyle resultStyle)
        {
            CombatResultStyle s = resultStyle != null ? resultStyle : CombatResultStyle.LoadOrDefault();
            var root = new GameObject("Combat Result Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AudioSource));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            AudioSource audio = root.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            TMP_FontAsset font = s.FontAsset;
            Color outline = s.OutlineColor;
            // The rank shader reads TexCoord1 (Paragraph mapping); TMP needs Normal/Tangent for its SDF shading.
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1
                | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var rootRect = (RectTransform)root.transform;

            Image dimImage = NewImage("Dim", rootRect, s.DimColor);
            Stretch(dimImage.rectTransform, 0f);
            dimImage.raycastTarget = true; // blocks clicks on the game UI behind

            RectTransform panelRect = NewRect("Panel", rootRect);
            Place(panelRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1320f, 800f));
            Image frame = NewImage("Frame", panelRect, s.PanelFrameColor);
            Stretch(frame.rectTransform, 0f);
            Image inner = NewImage("Background", panelRect, s.PanelColor);
            Stretch(inner.rectTransform, 4f);

            TMP_Text titleText = NewText("Title", panelRect, font, s.TitleSize, s.VictoryColor, outline, TextAlignmentOptions.Center);
            Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(1200f, 130f));
            TMP_Text subtitleText = NewText("Subtitle", panelRect, font, s.SubtitleSize, s.LabelColor, outline, TextAlignmentOptions.Center);
            Place(subtitleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -175f), new Vector2(1200f, 44f));

            RectTransform mainRows = NewRect("Main Rows", panelRect);
            mainRows.anchorMin = mainRows.anchorMax = mainRows.pivot = new Vector2(0f, 1f);
            mainRows.anchoredPosition = new Vector2(80f, -220f);
            mainRows.sizeDelta = new Vector2(620f, 400f);
            VerticalLayoutGroup vertical = mainRows.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 2f;
            vertical.childControlHeight = true;
            vertical.childControlWidth = true;
            vertical.childForceExpandHeight = false;
            vertical.childForceExpandWidth = true;

            RectTransform detailRows = NewRect("Detail Rows", panelRect);
            detailRows.anchorMin = detailRows.anchorMax = detailRows.pivot = new Vector2(0.5f, 0f);
            detailRows.anchoredPosition = new Vector2(0f, 95f);
            detailRows.sizeDelta = new Vector2(1180f, 110f);
            GridLayoutGroup grid = detailRows.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(380f, 48f);
            grid.spacing = new Vector2(20f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;

            Image divider = NewImage("Divider", panelRect, WithAlpha(s.PanelFrameColor, 0.35f));
            Place(divider.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 222f), new Vector2(1180f, 3f));

            // Grade: a diamond frame with the letter on top.
            RectTransform grade = NewRect("Grade", panelRect);
            Place(grade, new Vector2(1f, 1f), new Vector2(-320f, -405f), new Vector2(330f, 330f));
            Image diamondFrame = NewImage("Diamond Frame", grade, s.PanelFrameColor);
            Place(diamondFrame.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230f, 230f));
            diamondFrame.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image diamondInner = NewImage("Diamond", diamondFrame.rectTransform, s.PanelColor);
            Stretch(diamondInner.rectTransform, 6f);
            Image flash = NewImage("Flash", diamondFrame.rectTransform, new Color(1f, 1f, 1f, 0f));
            Stretch(flash.rectTransform, 0f);
            TMP_Text letter = NewText("Letter", grade, font, s.GradeSize, Color.white, outline, TextAlignmentOptions.Center);
            Stretch(letter.rectTransform, -60f);
            ApplyRankLook(s, letter, "S");

            TMP_Text caption = NewText("Grade Caption", panelRect, font, s.SubtitleSize, s.LabelColor, outline, TextAlignmentOptions.Center);
            Place(caption.rectTransform, new Vector2(1f, 1f), new Vector2(-320f, -222f), new Vector2(400f, 40f));
            TMP_Text comment = NewText("Grade Comment", panelRect, font, s.SubtitleSize, Color.white, outline, TextAlignmentOptions.Center);
            Place(comment.rectTransform, new Vector2(1f, 1f), new Vector2(-320f, -590f), new Vector2(520f, 40f));

            RectTransform badges = NewRect("Badges", panelRect);
            Place(badges, new Vector2(1f, 1f), new Vector2(-320f, -640f), new Vector2(560f, 44f));
            HorizontalLayoutGroup horizontal = badges.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 24f;
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = true;
            TMP_Text badge = NewText("Badge Template", badges, font, s.BadgeSize, s.BadgeColor, outline, TextAlignmentOptions.Center);
            badge.gameObject.SetActive(false);

            TMP_Text promptText = NewText("Prompt", rootRect, font, s.PromptSize, s.PromptColor, outline, TextAlignmentOptions.Center);
            Place(promptText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1200f, 40f));

            // Row template: the row sits in the layout, its Content child is what animates.
            RectTransform row = NewRect("Row Template", rootRect);
            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = 52f;
            RectTransform content = NewRect("Content", row);
            Stretch(content, 0f);
            TMP_Text rowLabel = NewText("Label", content, font, s.RowSize, s.LabelColor, outline, TextAlignmentOptions.Left);
            Stretch(rowLabel.rectTransform, 0f);
            TMP_Text rowValue = NewText("Value", content, font, s.RowSize, s.ValueColor, outline, TextAlignmentOptions.Right);
            Stretch(rowValue.rectTransform, 0f);
            ResultStatRow rowView = row.gameObject.AddComponent<ResultStatRow>();
            rowView.Assign(content, rowLabel, rowValue);
            row.gameObject.SetActive(false);

            CombatResultScreen screen = root.AddComponent<CombatResultScreen>();
            screen.style = resultStyle;
            screen.group = root.GetComponent<CanvasGroup>();
            screen.dim = dimImage;
            screen.panel = panelRect;
            screen.title = titleText;
            screen.subtitle = subtitleText;
            screen.mainRowsContainer = mainRows;
            screen.detailRowsContainer = detailRows;
            screen.rowTemplate = rowView;
            screen.gradeRoot = grade;
            screen.gradeLetter = letter;
            screen.gradeCaption = caption;
            screen.gradeComment = comment;
            screen.gradeFlash = flash;
            screen.badgeContainer = badges;
            screen.badgeTemplate = badge;
            screen.prompt = promptText;
            screen.audioSource = audio;
            if (Application.isPlaying) screen.HideImmediate();
            return screen;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            Image image = NewRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text NewText(string name, Transform parent, TMP_FontAsset font, int size, Color color, Color outline,
            TextAlignmentOptions alignment) =>
            CombatText.CreateUGUI(name, parent, font, size, color, alignment, outline, CombatText.OutlineWidthFromPixels(3f, size) + 0.1f);

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
