using System;
using System.Collections.Generic;
using System.IO;
using RythmRPG.Rhythm.Audio;
using RythmRPG.Rhythm.Editing;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RythmRPG.Rhythm.Editor.Composer
{
    /// <summary>UI Toolkit composer on top of ChartSession. The IMGUI window stays available until this reaches parity.</summary>
    public sealed class ComposerWindow : EditorWindow
    {
        [SerializeField] private RhythmChart chart;

        private ChartSession session;
        private TimelineElement timeline;
        private ObjectField chartField;
        private PopupField<int> laneCountField;
        private Label statusLabel;
        private Label inspectorHeader;
        private DoubleField beatField;
        private DoubleField holdField;
        private IntegerField damageField;
        private IntegerField pressesField;
        private Label rateLabel;
        private DoubleField travelField;
        private Label travelInfo;
        private VisualElement travelPresets;

        // pattern inspector
        private VisualElement patternBody;
        private Label patternTitle;
        private DoubleField patternStart;
        private DoubleField patternLength;
        private IntegerField patternSeed;
        private Toggle patternReserve;
        private VisualElement patternLaneRow;
        private VisualElement inspectorBody;
        private List<NoteCatalogEntry> entries = new List<NoteCatalogEntry>();
        private readonly Dictionary<string, VisualElement> libraryButtons = new Dictionary<string, VisualElement>();
        private bool refreshingInspector;

        // audio authoring (Phase 4)
        private ComposerAudio audio;
        private PlaybackTransport transport;
        private WaveformPeaks waveform;
        private AudioClip audioClip;
        private bool metronomeOn;
        private CombatSong song;
        private int previewBar;
        private ObjectField songField;
        private IntegerField previewBarField;
        private Scroller hScroller;
        private bool syncingScroller;
        private bool refreshingTransport;
        private string audioMessage = "";
        private Button playButton;
        private ObjectField clipField;
        private DoubleField bpmField;
        private DoubleField offsetField;
        private IntegerField meterField;

        // validation strip
        private List<ValidationIssue> issues = new List<ValidationIssue>();
        private int issueCursor = -1;
        private Label issueSummary;
        private Label issueMessage;

        [MenuItem("Tools/Rhythm/Composer (New)")]
        public static void Open()
        {
            GetWindow<ComposerWindow>("Rhythm Composer");
        }

        public void CreateGUI()
        {
            entries = NoteCatalog.Build();
            VisualElement root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            audio = new ComposerAudio();
            // The playhead follows the audio (dsp) clock, so it stays on the music and the metronome.
            transport = new PlaybackTransport(() => audio != null ? audio.Clock() : EditorApplication.timeSinceStartup);
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            root.Add(BuildToolbar());
            root.Add(BuildTransportBar());

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;
            root.Add(body);

            body.Add(BuildLibrary());

            timeline = new TimelineElement();
            timeline.SaveRequested += Save;
            timeline.ArmedPlaced += OnArmedPlaced;
            timeline.PlayheadScrubbed += OnPlayheadScrubbed;
            timeline.TogglePlayRequested += TogglePlay;
            timeline.ViewChanged += SyncScroller;

            var timelineColumn = new VisualElement();
            timelineColumn.style.flexGrow = 1f;
            timelineColumn.style.flexDirection = FlexDirection.Column;
            timelineColumn.Add(timeline);
            hScroller = new Scroller(0f, 100f, OnScrollerMoved, SliderDirection.Horizontal);
            hScroller.style.height = 14f;
            timelineColumn.Add(hScroller);
            body.Add(timelineColumn);

            body.Add(BuildInspector());

            root.Add(BuildValidationStrip());

            statusLabel = new Label();
            statusLabel.style.paddingLeft = 6f;
            statusLabel.style.paddingTop = 2f;
            statusLabel.style.paddingBottom = 2f;
            root.Add(statusLabel);

            if (chart != null) LoadChart(chart); else UpdateStatus();
        }

        private void OnDisable()
        {
            if (session != null) session.Changed -= OnSessionChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (audio != null)
            {
                audio.Dispose();
                audio = null;
            }
        }

        // ---------------- validation strip ----------------

        private VisualElement BuildValidationStrip()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6f;
            row.style.borderTopWidth = 1f;
            row.style.borderTopColor = new Color(0f, 0f, 0f, 0.5f);

            issueSummary = new Label("Validation");
            issueSummary.style.minWidth = 150f;
            issueSummary.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(issueSummary);
            row.Add(new Button(() => GoToIssue(-1)) { text = "<" });
            row.Add(new Button(() => GoToIssue(1)) { text = ">" });

            issueMessage = new Label();
            issueMessage.style.marginLeft = 6f;
            issueMessage.style.flexGrow = 1f;
            issueMessage.style.overflow = Overflow.Hidden;
            row.Add(issueMessage);
            return row;
        }

        private void RunValidation()
        {
            issues = session != null
                ? ChartValidator.Validate(session, song != null ? 0d : audioClip != null ? audioClip.length : 0d, DefaultsFor(NoteMigration.DefinitionIdMash))
                : new List<ValidationIssue>();
            if (timeline != null) timeline.Issues = ChartValidator.WorstByNote(issues);
            issueCursor = Mathf.Min(issueCursor, issues.Count - 1);
            if (issueSummary == null) return;

            int errors = 0, warnings = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == IssueSeverity.Error) errors++;
                else if (issues[i].Severity == IssueSeverity.Warning) warnings++;
            }

            if (session == null)
            {
                issueSummary.text = "Validation";
                issueSummary.style.color = new Color(0.7f, 0.7f, 0.7f);
            }
            else if (errors + warnings == 0)
            {
                issueSummary.text = "No issues";
                issueSummary.style.color = new Color(0.6f, 0.9f, 0.6f);
            }
            else
            {
                issueSummary.text = errors + (errors == 1 ? " error, " : " errors, ") + warnings + (warnings == 1 ? " warning" : " warnings");
                issueSummary.style.color = errors > 0 ? new Color(1f, 0.45f, 0.45f) : new Color(1f, 0.85f, 0.3f);
            }

            ShowIssueMessage();
        }

        private void ShowIssueMessage()
        {
            if (issueMessage == null) return;
            issueMessage.text = issueCursor >= 0 && issueCursor < issues.Count
                ? "(" + (issueCursor + 1) + "/" + issues.Count + ") " + issues[issueCursor].Message
                : (issues.Count > 0 ? "Use < > to step through issues." : "");
        }

        private void GoToIssue(int direction)
        {
            if (session == null || issues.Count == 0) return;
            issueCursor = issueCursor < 0
                ? (direction > 0 ? 0 : issues.Count - 1)
                : (issueCursor + direction + issues.Count) % issues.Count;
            ValidationIssue issue = issues[issueCursor];
            string target = issue.NoteId ?? issue.PatternId;
            if (target != null) session.SelectOnly(target);
            timeline.RevealBeat(issue.Beat);
            ShowIssueMessage();
        }

        // ---------------- audio authoring ----------------

        private VisualElement BuildTransportBar()
        {
            var column = new VisualElement();

            // Row 1: playback and the two music layers.
            var bar = new Toolbar();
            playButton = new ToolbarButton(TogglePlay) { text = "Play" };
            playButton.style.minWidth = 48f;
            bar.Add(playButton);
            bar.Add(new ToolbarButton(StopPlayback) { text = "Stop" });

            var metronome = new ToolbarToggle();
            metronome.text = "Metronome";
            metronome.RegisterValueChangedCallback(e =>
            {
                metronomeOn = e.newValue;
                if (audio != null) audio.SetMetronome(metronomeOn);
            });
            bar.Add(metronome);

            songField = new ObjectField("Song");
            songField.objectType = typeof(CombatSong);
            songField.allowSceneObjects = false;
            songField.style.minWidth = 220f;
            songField.labelElement.style.minWidth = 36f;
            songField.tooltip = "Combat Song this chart is written for. Its BPM, beats per bar and offset rule the grid (edits below change the song). In combat the song loops and this chart starts on its next bar line.";
            songField.RegisterValueChangedCallback(e => OnSongPicked(e.newValue as CombatSong));
            bar.Add(songField);
            previewBarField = new IntegerField("Preview bar");
            previewBarField.style.width = 120f;
            previewBarField.labelElement.style.minWidth = 70f;
            previewBarField.tooltip = "Composer preview only: which bar of the song the chart's beat 0 is heard against. In combat the chart starts on whatever bar comes next.";
            previewBarField.RegisterValueChangedCallback(e =>
            {
                if (refreshingTransport || session == null) return;
                previewBar = Math.Max(0, e.newValue);
                session.Dirty = true;
                ApplySongTempo();
            });
            bar.Add(previewBarField);

            bar.Add(new ToolbarSpacer());

            clipField = new ObjectField("Audio");
            clipField.objectType = typeof(AudioClip);
            clipField.allowSceneObjects = false;
            clipField.style.minWidth = 240f;
            clipField.labelElement.style.minWidth = 40f;
            clipField.tooltip = "Chart music. The waveform and beat grid follow this clip. In combat it loops for the fight and is muffled on the player turn.";
            clipField.RegisterValueChangedCallback(e => OnAudioClipPicked(e.newValue as AudioClip));
            bar.Add(clipField);
            column.Add(bar);

            // Row 2: grid alignment. Everything here can be changed while playing; the music keeps going.
            var align = new Toolbar();
            bpmField = MakeTempoField("BPM", 100f);
            align.Add(bpmField);
            align.Add(NudgeButton("-0.1", "BPM -0.1", () => NudgeBpm(-0.1d)));
            align.Add(NudgeButton("+0.1", "BPM +0.1", () => NudgeBpm(0.1d)));
            meterField = new IntegerField("Beats/bar");
            meterField.style.width = 100f;
            meterField.labelElement.style.minWidth = 52f;
            meterField.RegisterValueChangedCallback(e => OnTempoFieldsChanged());
            align.Add(meterField);
            align.Add(new ToolbarSpacer());
            offsetField = MakeTempoField("Offset (s)", 140f);
            offsetField.tooltip = "Audio seconds at which beat 0 falls. Nudge while playing with the metronome on until the clicks sit on the music.";
            align.Add(offsetField);
            align.Add(NudgeButton("-10ms", "Offset -10 ms (grid earlier)", () => NudgeOffset(-0.010d)));
            align.Add(NudgeButton("-1ms", "Offset -1 ms", () => NudgeOffset(-0.001d)));
            align.Add(NudgeButton("+1ms", "Offset +1 ms", () => NudgeOffset(0.001d)));
            align.Add(NudgeButton("+10ms", "Offset +10 ms (grid later)", () => NudgeOffset(0.010d)));
            align.Add(new ToolbarButton(SetBeatZeroAtPlayhead) { text = "Beat 0 = playhead" });

            var hint = new Label("  Space = play/pause | click ruler to seek | tune BPM/offset while playing");
            hint.style.color = new Color(0.65f, 0.65f, 0.65f);
            hint.style.unityTextAlign = TextAnchor.MiddleLeft;
            align.Add(hint);
            column.Add(align);
            return column;
        }

        private static ToolbarButton NudgeButton(string text, string tooltip, Action onClick)
        {
            var button = new ToolbarButton(onClick) { text = text, tooltip = tooltip };
            button.style.minWidth = 34f;
            return button;
        }

        private void NudgeOffset(double delta)
        {
            if (session == null) return;
            offsetField.SetValueWithoutNotify(Math.Max(0d, Math.Round((offsetField.value + delta) * 1000d) / 1000d));
            OnTempoFieldsChanged();
        }

        private void NudgeBpm(double delta)
        {
            if (session == null) return;
            bpmField.SetValueWithoutNotify(Math.Max(1d, Math.Round((bpmField.value + delta) * 100d) / 100d));
            OnTempoFieldsChanged();
        }


        private DoubleField MakeTempoField(string label, float width)
        {
            var field = new DoubleField(label);
            field.style.width = width;
            field.labelElement.style.minWidth = 52f;
            field.RegisterValueChangedCallback(e => OnTempoFieldsChanged());
            return field;
        }

        private void RefreshTransportFields()
        {
            if (bpmField == null) return;
            refreshingTransport = true;
            try
            {
                bpmField.SetValueWithoutNotify(session != null ? session.Tempo.Segments[0].Bpm : 120d);
                meterField.SetValueWithoutNotify(session != null ? session.Tempo.BeatsPerMeasure : 4);
                offsetField.SetValueWithoutNotify(session != null ? session.Tempo.AudioOffsetSeconds : 0d);
                clipField.SetValueWithoutNotify(audioClip);
                clipField.SetEnabled(song == null);
                if (songField != null) songField.SetValueWithoutNotify(song);
                if (previewBarField != null)
                {
                    previewBarField.SetValueWithoutNotify(previewBar);
                    previewBarField.SetEnabled(song != null);
                }
                if (song != null)
                {
                    // With a song the fields show (and edit) the song's own rules.
                    bpmField.SetValueWithoutNotify(song.Bpm);
                    meterField.SetValueWithoutNotify(song.BeatsPerMeasure);
                    offsetField.SetValueWithoutNotify(song.AudioOffsetSeconds);
                }
            }
            finally
            {
                refreshingTransport = false;
            }
        }

        private void OnTempoFieldsChanged()
        {
            if (refreshingTransport || session == null) return;
            double bpm = Math.Max(1d, bpmField.value);
            int meter = Math.Max(1, meterField.value);
            double offset = Math.Max(0d, offsetField.value);
            if (song != null)
            {
                // The song owns the beat rules: every chart written for it follows the change.
                Undo.RecordObject(song, "Combat Song Tempo");
                song.Bpm = (float)bpm;
                song.BeatsPerMeasure = meter;
                song.AudioOffsetSeconds = offset;
                EditorUtility.SetDirty(song);
                ApplySongTempo();
                return;
            }

            session.Tempo = new TempoMap(bpm, meter, offset);
            ApplyTempoChanged();
        }

        private void ApplyTempoChanged()
        {
            session.NotifyTempoChanged();
            RefreshTransportFields();
            SyncPlayhead(false);
            SyncScroller();
            // The music is not restarted: only the grid (and the metronome ticks on it) moves.
            if (audio != null) audio.Retime(session.Tempo);
        }

        // Chart beat 0 = the song's first downbeat + Preview bar bars. Stored as the chart's offset (combat puts chart
        // beat 0 on a bar line of the running song, so the preview bar only affects what you hear while charting).
        private void ApplySongTempo()
        {
            if (session == null || song == null) return;
            session.Tempo = new TempoMap(song.Bpm, song.BeatsPerMeasure, song.AudioOffsetSeconds + previewBar * song.BarSeconds);
            ApplyTempoChanged();
        }

        private void OnSongPicked(CombatSong picked)
        {
            if (refreshingTransport || session == null) return;
            bool wasPlaying = transport.IsPlaying;
            StopPlayback();
            song = picked;
            session.Dirty = true;
            if (song != null) ApplySongTempo();
            else RefreshTransportFields();
            RebuildWaveform();
            RunValidation();
            timeline.Refresh();
            UpdateStatus();
            if (wasPlaying) TogglePlay();
        }

        private bool MatchesSongTempo(TempoMap tempo)
        {
            if (song == null) return true;
            return Math.Abs(tempo.Segments[0].Bpm - song.Bpm) < 1e-4
                   && tempo.BeatsPerMeasure == song.BeatsPerMeasure
                   && Math.Abs(tempo.AudioOffsetSeconds - (song.AudioOffsetSeconds + previewBar * song.BarSeconds)) < 1e-6;
        }

        /// <summary>The clip heard and drawn: the song's clip when a song is set, otherwise the chart's own clip.</summary>
        private AudioClip PreviewClip => song != null && song.Clip != null ? song.Clip : audioClip;

        private void SetBeatZeroAtPlayhead()
        {
            if (session == null) return;
            if (song != null)
            {
                // Put a downbeat on the playhead and keep the chart's beat 0 there.
                double bar = song.BarSeconds;
                double position = transport.Position;
                double offset = ((position % bar) + bar) % bar;
                previewBar = Math.Max(0, (int)Math.Round((position - offset) / bar));
                offsetField.SetValueWithoutNotify(offset);
                OnTempoFieldsChanged();
                return;
            }

            offsetField.SetValueWithoutNotify(transport.Position);
            OnTempoFieldsChanged();
        }

        private void OnScrollerMoved(float value)
        {
            if (syncingScroller || timeline == null) return;
            timeline.ScrollBeat = value;
        }

        private void SyncScroller()
        {
            if (hScroller == null || timeline == null) return;
            double span = timeline.VisibleBeatSpan;
            double total = session != null ? session.Tempo.SecondsToBeat(ComputeDuration()) : 0d;
            double max = Math.Max(0d, Math.Max(total, timeline.ScrollBeat + span) - span * 0.5d);
            syncingScroller = true;
            try
            {
                hScroller.lowValue = 0f;
                hScroller.highValue = (float)Math.Max(0.01d, max);
                hScroller.value = (float)Math.Min(max, timeline.ScrollBeat);
                hScroller.Adjust((float)Math.Min(1d, span / Math.Max(span, max + span)));
            }
            finally
            {
                syncingScroller = false;
            }
        }

        private void OnAudioClipPicked(AudioClip clip)
        {
            if (refreshingTransport || session == null) return;
            StopPlayback();
            audioClip = clip;
            session.Dirty = true;
            RebuildWaveform();
            RunValidation();
            timeline.Refresh();
            UpdateStatus();
        }

        private void RebuildWaveform()
        {
            string error;
            waveform = ComposerAudio.BuildPeaks(PreviewClip, out error);
            audioMessage = error ?? "";

            if (timeline != null) timeline.Waveform = waveform;
        }

        private double ComputeDuration()
        {
            double d = chart != null ? chart.EffectiveDuration : 30d;
            if (audioClip != null) d = Math.Max(d, audioClip.length);
            if (PreviewClip != null) d = Math.Max(d, PreviewClip.length);
            if (session != null)
            {
                IList<NoteInstance> notes = session.Notes;
                for (int i = 0; i < notes.Count; i++)
                {
                    d = Math.Max(d, session.Tempo.BeatToSeconds(notes[i].HitBeat + notes[i].HoldBeats) + 1d);
                }
            }

            return d;
        }

        private void TogglePlay()
        {
            if (session == null || transport == null) return;
            if (transport.IsPlaying)
            {
                transport.Pause();
                audio.Stop();
            }
            else
            {
                transport.Duration = ComputeDuration();
                transport.Play();
                StartAudio();
                if (EditorUtility.audioMasterMute)
                    audioMessage = "Editor audio is muted (Game view 'Mute Audio'): unmute to hear music and clicks.";
                UpdateStatus();
            }

            UpdatePlayButton();
        }

        private void StopPlayback()
        {
            if (transport == null) return;
            // Return to one bar before the chart's beat 0 (a count-in), or 0.
            transport.Stop(session != null ? Math.Max(0d, session.Tempo.BeatToSeconds(-session.Tempo.BeatsPerMeasure)) : 0d);
            if (audio != null) audio.Stop();
            SyncPlayhead(true);
            UpdatePlayButton();
        }

        private void StartAudio()
        {
            transport.Duration = ComputeDuration();
            double position = transport.Position;
            double startsAt = audio.Play(position, PreviewClip, session.Tempo, metronomeOn);
            transport.SyncTo(position, startsAt);
        }

        private void OnPlayheadScrubbed(double beat)
        {
            if (session == null) return;
            transport.Duration = ComputeDuration();
            transport.Seek(session.Tempo.BeatToSeconds(beat));
            if (transport.IsPlaying) StartAudio();
        }

        private void SyncPlayhead(bool follow)
        {
            if (session == null) return;
            timeline.PlayheadBeat = session.Tempo.SecondsToBeat(transport.Position);
            if (follow) timeline.FollowPlayhead();
            timeline.MarkDirtyRepaint();
        }

        private void OnEditorUpdate()
        {
            if (transport == null || session == null || !transport.IsPlaying) return;
            audio?.Update();
            bool ended = transport.Update();
            SyncPlayhead(true);
            if (ended)
            {
                audio.Stop();
                UpdatePlayButton();
            }
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (transport != null && transport.IsPlaying) TogglePlay();
        }

        private void UpdatePlayButton()
        {
            if (playButton != null) playButton.text = transport != null && transport.IsPlaying ? "Pause" : "Play";
        }

        // ---------------- UI construction ----------------

        private VisualElement BuildToolbar()
        {
            var bar = new Toolbar();

            chartField = new ObjectField();
            chartField.objectType = typeof(RhythmChart);
            chartField.allowSceneObjects = false;
            chartField.style.minWidth = 240f;
            chartField.RegisterValueChangedCallback(e =>
            {
                var picked = e.newValue as RhythmChart;
                if (picked == chart) return;
                if (session != null && session.Dirty &&
                    !EditorUtility.DisplayDialog("Unsaved changes", "Discard unsaved changes to the current chart?", "Discard", "Cancel"))
                {
                    chartField.SetValueWithoutNotify(chart);
                    return;
                }

                LoadChart(picked);
            });
            bar.Add(chartField);

            bar.Add(new ToolbarButton(Save) { text = "Save" });
            bar.Add(new ToolbarButton(() => { if (chart != null) LoadChart(chart); }) { text = "Reload" });
            bar.Add(new ToolbarSpacer());
            bar.Add(new ToolbarButton(() => { if (session != null) session.Undo(); }) { text = "Undo" });
            bar.Add(new ToolbarButton(() => { if (session != null) session.Redo(); }) { text = "Redo" });
            bar.Add(new ToolbarSpacer());

            var snap = new ToolbarToggle();
            snap.text = "Snap";
            snap.value = true;
            snap.RegisterValueChangedCallback(e =>
            {
                if (session == null) return;
                session.SnapEnabled = e.newValue;
                UpdateStatus();
            });
            bar.Add(snap);

            var division = new PopupField<int>(new List<int> { 1, 2, 4, 8, 16 }, 2);
            division.formatSelectedValueCallback = v => "1/" + v;
            division.formatListItemCallback = v => "1/" + v;
            division.RegisterValueChangedCallback(e =>
            {
                if (session == null) return;
                session.SnapDivision = e.newValue;
                timeline.Refresh();
                UpdateStatus();
            });
            bar.Add(division);

            // Lanes this chart plays with (1-4). Combat shows only these lanes, on the matching keys.
            var laneCounts = new List<int>();
            for (int i = 1; i <= RhythmChart.MaxLanes; i++) laneCounts.Add(i);
            laneCountField = new PopupField<int>(laneCounts, laneCounts.Count - 1);
            laneCountField.formatSelectedValueCallback = v => v == 1 ? "1 lane" : v + " lanes";
            laneCountField.formatListItemCallback = v => v == 1 ? "1 lane" : v + " lanes";
            laneCountField.tooltip = "How many lanes this chart uses (1-" + RhythmChart.MaxLanes + "). In combat only these " +
                                     "lanes are shown, on the matching keys (1: J, 2: S J, 3: S J K, 4: A S J K).";
            laneCountField.RegisterValueChangedCallback(e => OnLaneCountPicked(e.newValue));
            bar.Add(laneCountField);

            var hint = new Label("  Ctrl+wheel zoom | wheel scroll | Ctrl+C/V/D/A/Z/Y | Del | Alt = no snap");
            hint.style.color = new Color(0.65f, 0.65f, 0.65f);
            hint.style.unityTextAlign = TextAnchor.MiddleLeft;
            bar.Add(hint);
            return bar;
        }

        private VisualElement BuildLibrary()
        {
            var lib = new ScrollView();
            lib.style.width = 140f;
            lib.style.minWidth = 140f;
            lib.style.borderRightWidth = 1f;
            lib.style.borderRightColor = new Color(0f, 0f, 0f, 0.5f);
            lib.style.paddingLeft = 4f;
            lib.style.paddingRight = 4f;

            var title = new Label("Library");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 4f;
            lib.Add(title);
            var hint = new Label("Click to arm, then click a lane. Or drag onto the timeline.");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.fontSize = 10f;
            hint.style.color = new Color(0.65f, 0.65f, 0.65f);
            lib.Add(hint);

            string group = null;
            for (int i = 0; i < entries.Count; i++)
            {
                NoteCatalogEntry e = entries[i];
                if (e.Group != group)
                {
                    group = e.Group;
                    var g = new Label(group);
                    g.style.marginTop = 8f;
                    g.style.color = new Color(0.75f, 0.75f, 0.75f);
                    lib.Add(g);
                }

                lib.Add(MakeLibraryButton(e));
            }

            return lib;
        }

        private VisualElement MakeLibraryButton(NoteCatalogEntry entry)
        {
            var btn = new Label(entry.Name);
            btn.style.marginTop = 2f;
            btn.style.paddingLeft = 6f;
            btn.style.paddingTop = 4f;
            btn.style.paddingBottom = 4f;
            btn.style.borderLeftWidth = 4f;
            btn.style.borderLeftColor = entry.Color;
            btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            libraryButtons[entry.Id] = btn;

            btn.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                btn.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            btn.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!btn.HasPointerCapture(e.pointerId)) return;
                btn.ReleasePointer(e.pointerId);
                Vector2 world = btn.LocalToWorld(new Vector2(e.localPosition.x, e.localPosition.y));
                if (session != null && timeline != null && timeline.worldBound.Contains(world))
                {
                    timeline.PlaceAtWorld(entry.Id, world);
                }
                else if (btn.worldBound.Contains(world))
                {
                    Arm(timeline.ArmedDefinitionId == entry.Id ? null : entry.Id);
                }

                e.StopPropagation();
            });
            return btn;
        }

        private void Arm(string definitionId)
        {
            timeline.ArmedDefinitionId = definitionId;
            foreach (KeyValuePair<string, VisualElement> kv in libraryButtons)
            {
                kv.Value.style.backgroundColor = kv.Key == definitionId
                    ? new Color(1f, 0.92f, 0.3f, 0.35f)
                    : new Color(1f, 1f, 1f, 0.06f);
            }
        }

        private void OnArmedPlaced(string definitionId)
        {
            // Stay armed so several notes can be dropped quickly; click the button again or the empty area to disarm.
        }

        private VisualElement BuildInspector()
        {
            var panel = new VisualElement();
            panel.style.width = 210f;
            panel.style.minWidth = 210f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderLeftColor = new Color(0f, 0f, 0f, 0.5f);
            panel.style.paddingLeft = 6f;
            panel.style.paddingRight = 6f;

            inspectorHeader = new Label("Inspector");
            inspectorHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            inspectorHeader.style.marginTop = 4f;
            panel.Add(inspectorHeader);

            inspectorBody = new VisualElement();
            panel.Add(inspectorBody);

            beatField = new DoubleField("Beat");
            beatField.RegisterValueChangedCallback(e =>
            {
                if (refreshingInspector || session == null) return;
                double v = Math.Max(0d, session.Snap(e.newValue));
                session.ModifySelected("Set beat", n => n.HitBeat = v);
            });
            inspectorBody.Add(beatField);

            holdField = new DoubleField("Hold (beats)");
            holdField.RegisterValueChangedCallback(e =>
            {
                if (refreshingInspector || session == null) return;
                double v = Math.Max(0d, e.newValue);
                session.ModifySelected("Set hold", n => n.HoldBeats = v);
            });
            inspectorBody.Add(holdField);

            // Travel time (spawn -> hit line) in beats; applies to every selected note. Shorter = faster notes.
            travelField = new DoubleField("Travel (beats)");
            travelField.tooltip = "How long the note travels to the hit line. Shorter = faster. Applies to all selected notes; " +
                                  "hit times stay on their beats.";
            travelField.RegisterValueChangedCallback(e =>
            {
                if (refreshingInspector || session == null) return;
                SetSelectedTravel(e.newValue);
            });
            inspectorBody.Add(travelField);

            travelInfo = new Label();
            travelInfo.style.whiteSpace = WhiteSpace.Normal;
            travelInfo.style.fontSize = 10;
            travelInfo.style.color = new Color(0.75f, 0.75f, 0.75f);
            inspectorBody.Add(travelInfo);

            travelPresets = new VisualElement();
            travelPresets.style.flexDirection = FlexDirection.Row;
            travelPresets.style.flexWrap = Wrap.Wrap;
            AddTravelPreset("\u00BD bar", 0.5d);
            AddTravelPreset("1 bar", 1d);
            AddTravelPreset("1\u00BD", 1.5d);
            AddTravelPreset("2 bars", 2d);
            var faster = new Button(() => ScaleSelectedTravel(0.9d)) { text = "Faster" };
            faster.tooltip = "Travel time x0.9 on every selected note (keeps their differences).";
            travelPresets.Add(faster);
            var slower = new Button(() => ScaleSelectedTravel(1d / 0.9d)) { text = "Slower" };
            slower.tooltip = "Travel time /0.9 on every selected note.";
            travelPresets.Add(slower);
            var reset = new Button(() =>
            {
                if (session != null) session.ModifySelected("Reset travel", n => n.TravelBeats = default);
            }) { text = "Default" };
            reset.tooltip = "Back to the note type's default travel time.";
            travelPresets.Add(reset);
            inspectorBody.Add(travelPresets);

            damageField = new IntegerField("Damage");
            damageField.RegisterValueChangedCallback(e =>
            {
                if (refreshingInspector || session == null) return;
                int v = e.newValue;
                session.ModifySelected("Set damage", n => n.Damage = new Overridable<int>(v));
            });
            inspectorBody.Add(damageField);

            pressesField = new IntegerField("Presses");
            pressesField.RegisterValueChangedCallback(e =>
            {
                if (refreshingInspector || session == null) return;
                int v = Math.Max(1, e.newValue);
                session.ModifySelected("Set presses", n => n.MashRequiredPresses = new Overridable<int>(v));
            });
            inspectorBody.Add(pressesField);

            rateLabel = new Label();
            rateLabel.style.whiteSpace = WhiteSpace.Normal;
            inspectorBody.Add(rateLabel);

            panel.Add(BuildPatternInspector());
            return panel;
        }

        private void AddTravelPreset(string label, double bars)
        {
            var button = new Button(() =>
            {
                if (session == null) return;
                SetSelectedTravel(bars * Math.Max(1, session.Tempo.BeatsPerMeasure));
            }) { text = label };
            button.tooltip = "Travel time = " + bars + " bar(s) on every selected note.";
            travelPresets.Add(button);
        }

        private void SetSelectedTravel(double beats)
        {
            if (session == null) return;
            double v = Math.Max(NoteHandles.MinTravelBeats, beats);
            session.ModifySelected("Set travel", n => n.TravelBeats = new Overridable<double>(v));
        }

        private void ScaleSelectedTravel(double factor)
        {
            if (session == null) return;
            session.ModifySelected("Scale travel", n =>
            {
                double current = NoteHandles.TravelBeats(n, DefaultsFor(n.DefinitionId), session.Tempo);
                n.TravelBeats = new Overridable<double>(Math.Max(NoteHandles.MinTravelBeats, current * factor));
            });
        }

        // Travel field for the selection: the shared value, or "mixed" when the selected notes differ.
        private void RefreshTravelField(List<NoteInstance> selected)
        {
            double min = double.MaxValue, max = double.MinValue, minSec = double.MaxValue, maxSec = double.MinValue;
            foreach (NoteInstance n in selected)
            {
                NoteHandleDefaults d = DefaultsFor(n.DefinitionId);
                double beats = NoteHandles.TravelBeats(n, d, session.Tempo);
                double seconds = NoteHandles.TravelSeconds(n, d, session.Tempo);
                min = Math.Min(min, beats);
                max = Math.Max(max, beats);
                minSec = Math.Min(minSec, seconds);
                maxSec = Math.Max(maxSec, seconds);
            }

            if (selected.Count == 0) return;
            bool mixed = max - min > 1e-6;
            travelField.showMixedValue = mixed;
            if (!mixed) travelField.SetValueWithoutNotify(Math.Round(min, 4));
            double bar = Math.Max(1, session.Tempo.BeatsPerMeasure);
            travelInfo.text = mixed
                ? $"Mixed: {min:0.##}-{max:0.##} beats ({minSec:0.###}-{maxSec:0.###} s). Type a value to set all."
                : $"= {minSec:0.###} s, {min / bar:0.##} bar(s). Shorter = faster.";
        }

        private int ResolveDamage(NoteInstance n)
        {
            if (n.Damage.HasValue) return n.Damage.Value;
            RhythmNoteDefinition def = chart != null ? chart.FindDefinition(NoteMigration.LegacyTypeFor(n.DefinitionId)) : null;
            return def != null ? def.DefaultDamage : 0;
        }

        private NoteHandleDefaults DefaultsFor(string definitionId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Id == definitionId && entries[i].HandleDefaults != null) return entries[i].HandleDefaults;
            }

            return new NoteHandleDefaults();
        }

        private VisualElement BuildPatternInspector()
        {
            patternBody = new VisualElement();
            patternBody.style.display = DisplayStyle.None;

            patternTitle = new Label();
            patternTitle.style.whiteSpace = WhiteSpace.Normal;
            patternTitle.style.marginBottom = 4f;
            patternBody.Add(patternTitle);

            patternStart = new DoubleField("Start (beat)");
            patternStart.RegisterValueChangedCallback(e =>
            {
                PatternInstance pat = refreshingInspector || session == null ? null : session.SelectedPattern;
                if (pat == null) return;
                double v = session.Snap(e.newValue);
                session.ModifyPattern(pat.Id, "Set pattern start", c => c.StartBeat = v);
            });
            patternBody.Add(patternStart);

            patternLength = new DoubleField("Length (beats)");
            patternLength.RegisterValueChangedCallback(e =>
            {
                PatternInstance pat = refreshingInspector || session == null ? null : session.SelectedPattern;
                if (pat == null) return;
                double v = session.Snap(e.newValue);
                session.ModifyPattern(pat.Id, "Set pattern length", c => c.DurationBeats = v);
            });
            patternBody.Add(patternLength);

            patternSeed = new IntegerField("Seed");
            patternSeed.RegisterValueChangedCallback(e =>
            {
                PatternInstance pat = refreshingInspector || session == null ? null : session.SelectedPattern;
                if (pat == null) return;
                int v = e.newValue;
                session.ModifyPattern(pat.Id, "Set pattern seed", c => c.Seed = v);
            });
            patternBody.Add(patternSeed);
            patternBody.Add(new Button(() =>
            {
                PatternInstance pat = session != null ? session.SelectedPattern : null;
                if (pat == null) return;
                int v = UnityEngine.Random.Range(1, int.MaxValue);
                session.ModifyPattern(pat.Id, "Reroll pattern", c => c.Seed = v);
            }) { text = "Reroll seed" });

            patternReserve = new Toggle("Reserve lanes");
            patternReserve.RegisterValueChangedCallback(e =>
            {
                PatternInstance pat = refreshingInspector || session == null ? null : session.SelectedPattern;
                if (pat == null) return;
                bool on = e.newValue;
                session.ModifyPattern(pat.Id, "Set reservation", c => c.Reservation = on ? ReservationMode.Exclusive : ReservationMode.None);
            });
            patternBody.Add(patternReserve);

            var lanesLabel = new Label("Lanes");
            lanesLabel.style.marginTop = 4f;
            patternBody.Add(lanesLabel);
            patternLaneRow = new VisualElement();
            patternLaneRow.style.flexDirection = FlexDirection.Row;
            patternLaneRow.style.flexWrap = Wrap.Wrap;
            patternBody.Add(patternLaneRow);

            var bake = new Button(() =>
            {
                PatternInstance pat = session != null ? session.SelectedPattern : null;
                if (pat != null) session.BakePattern(pat.Id);
            }) { text = "Bake to notes" };
            bake.style.marginTop = 6f;
            bake.tooltip = "Replace this pattern with ordinary, editable notes.";
            patternBody.Add(bake);
            return patternBody;
        }

        private void RefreshPatternInspector(PatternInstance pat)
        {
            PatternTemplate template = PatternLibrary.Find(pat.TemplateId);
            inspectorHeader.text = "Pattern";
            patternTitle.text = template != null ? template.Name + ": " + template.Description : pat.TemplateId;
            patternStart.SetValueWithoutNotify(pat.StartBeat);
            patternLength.SetValueWithoutNotify(pat.DurationBeats);
            patternSeed.SetValueWithoutNotify(pat.Seed);
            patternReserve.SetValueWithoutNotify(pat.Reservation == ReservationMode.Exclusive);

            patternLaneRow.Clear();
            string patternId = pat.Id;
            int laneCount = session.LaneIds.Count;
            for (int lane = 0; lane < laneCount; lane++)
            {
                int laneIndex = lane;
                var toggle = new Toggle("L" + (lane + 1));
                toggle.SetValueWithoutNotify(pat.UsesLane(lane));
                toggle.style.marginRight = 4f;
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (refreshingInspector || session == null) return;
                    session.ModifyPattern(patternId, "Set pattern lanes", c =>
                    {
                        int mask = c.LaneMask == 0 ? (1 << laneCount) - 1 : c.LaneMask;
                        if (e.newValue) mask |= 1 << laneIndex; else mask &= ~(1 << laneIndex);
                        if (mask == 0) mask = 1 << laneIndex; // at least one lane
                        c.LaneMask = mask == (1 << laneCount) - 1 ? 0 : mask;
                    });
                });
                patternLaneRow.Add(toggle);
            }
        }

        // ---------------- chart lifecycle ----------------

        private void LoadChart(RhythmChart target)
        {
            if (session != null) session.Changed -= OnSessionChanged;
            StopPlayback();
            chart = target;
            chartField.SetValueWithoutNotify(target);
            if (target == null)
            {
                session = null;
                audioClip = null;
                song = null;
                previewBar = 0;
                waveform = null;
                timeline.Waveform = null;
                RefreshTransportFields();
                timeline.Bind(null, entries);
                RunValidation();
                UpdateInspector();
                UpdateStatus();
                return;
            }

            OfferFitToMaxLanes(target);
            laneCountField?.SetValueWithoutNotify(Mathf.Clamp(target.Lanes.Count, 1, RhythmChart.MaxLanes));
            session = ChartSessionBridge.Load(target, msg => Debug.LogWarning("[Composer] " + msg));
            session.Changed += OnSessionChanged;
            timeline.PlayheadBeat = 0d;
            audioClip = target.AudioClip;
            song = target.Song;
            previewBar = target.SongPreviewBar;
            // Notes are beats in the session, so a chart whose song changed tempo follows the song (save to keep it).
            if (song != null && !MatchesSongTempo(session.Tempo)) ApplySongTempo();
            RebuildWaveform();
            RefreshTransportFields();
            timeline.Bind(session, entries);
            SyncScroller();
            issueCursor = -1;
            RunValidation();
            UpdateInspector();
            UpdateStatus();
        }

        // Charts from the old 5-lane layout: offer to fold the extra lanes into lane 4.
        private static void OfferFitToMaxLanes(RhythmChart target)
        {
            if (!ChartLaneConverter.HasTooManyLanes(target)) return;
            if (!EditorUtility.DisplayDialog("Too many lanes",
                    $"'{target.name}' has {target.Lanes.Count} lanes; the game plays at most {RhythmChart.MaxLanes}.\n\n" +
                    $"Fit it to {RhythmChart.MaxLanes} lanes now? Notes on the extra lanes move to lane {RhythmChart.MaxLanes} " +
                    "(a note that would overlap one there is removed).", "Fit to " + RhythmChart.MaxLanes + " lanes", "Keep as is"))
                return;
            ApplyLaneCount(target, RhythmChart.MaxLanes);
        }

        private static void ApplyLaneCount(RhythmChart target, int count)
        {
            Undo.RecordObject(target, "Change Lane Count");
            ChartLaneConverter.Result result = ChartLaneConverter.SetLaneCount(target, count);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            ChartLaneConverter.UpdateGoldenBaseline(new Dictionary<string, ChartLaneConverter.Result>
            {
                [AssetDatabase.GetAssetPath(target)] = result
            });
            Debug.Log($"[Composer] {target.name}: {count} lane(s). {ChartLaneConverter.Describe(result)}");
        }

        private void OnLaneCountPicked(int count)
        {
            if (chart == null || session == null)
            {
                laneCountField.SetValueWithoutNotify(RhythmChart.MaxLanes);
                return;
            }
            int current = chart.Lanes.Count;
            if (count == current) return;
            string message = count < current
                ? $"Remove lane(s) {count + 1}-{current}? Their notes move to lane {count} (a note that would overlap one " +
                  "there is removed).\n\nThe chart is saved first."
                : $"Add {count - current} empty lane(s)?\n\nThe chart is saved first.";
            if (!EditorUtility.DisplayDialog("Lanes", message, count < current ? "Remove" : "Add", "Cancel"))
            {
                laneCountField.SetValueWithoutNotify(Mathf.Clamp(current, 1, RhythmChart.MaxLanes));
                return;
            }
            Save();
            ApplyLaneCount(chart, count);
            LoadChart(chart);
        }

        private void Save()
        {
            if (chart == null || session == null) return;
            BackupAsset(chart);
            Undo.RecordObject(chart, "Rhythm Composer Save");
            chart.AudioClip = audioClip;
            chart.Song = song;
            chart.SongPreviewBar = previewBar;
            chart.Bpm = (float)session.Tempo.Segments[0].Bpm;
            chart.BeatsPerMeasure = session.Tempo.BeatsPerMeasure;
            chart.AudioOffsetSeconds = session.Tempo.AudioOffsetSeconds;
            ChartSessionBridge.Apply(session, chart);
            EnsureMashDefinition(chart);
            chart.SnapEnabled = session.SnapEnabled;
            chart.SnapDivision = (RhythmSnapDivision)session.SnapDivision;
            EditorUtility.SetDirty(chart);
            AssetDatabase.SaveAssets();
            session.Dirty = false;
            UpdateStatus();
        }

        // Charts created before Mash existed have no definition for it, so the runner could not find a prefab.
        private static void EnsureMashDefinition(RhythmChart target)
        {
            bool usesMash = false;
            for (int i = 0; i < target.Notes.Count; i++)
            {
                if (target.Notes[i] != null && target.Notes[i].NoteType == RhythmNoteType.Mash) usesMash = true;
            }

            if (!usesMash) return;
            RhythmNoteDefinition def = target.FindDefinition(RhythmNoteType.Mash);
            if (def == null)
            {
                def = new RhythmNoteDefinition(RhythmNoteType.Mash, new Color(1f, 0.85f, 0.2f));
                target.NoteDefinitions.Add(def);
            }

            if (def.DefaultPrefab == null)
            {
                def.DefaultPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Mash.prefab");
            }
        }

        // Backups live under Library/ (outside Assets) so they can never be picked up as content.
        private static void BackupAsset(RhythmChart target)
        {
            try
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(path)) return;
                string dir = Path.Combine("Library", "RhythmComposerBackups");
                Directory.CreateDirectory(dir);
                string name = Path.GetFileNameWithoutExtension(path) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".asset";
                File.Copy(path, Path.Combine(dir, name), true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Composer] Backup skipped: " + ex.Message);
            }
        }

        // ---------------- refresh ----------------

        private void OnSessionChanged()
        {
            RunValidation();
            UpdateInspector();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (statusLabel == null) return;
            titleContent = new GUIContent(session != null && session.Dirty ? "Rhythm Composer *" : "Rhythm Composer");
            if (session == null)
            {
                statusLabel.text = "Pick a Rhythm Chart to start.";
                return;
            }

            statusLabel.text = session.Notes.Count + " notes | " + session.SelectionCount + " selected | snap " +
                               (session.SnapEnabled ? "1/" + session.SnapDivision : "off") +
                               (session.Dirty ? " | unsaved changes" : "") +
                               (string.IsNullOrEmpty(audioMessage) ? "" : " | " + audioMessage);
        }

        private void UpdateInspector()
        {
            if (inspectorHeader == null) return;
            refreshingInspector = true;
            try
            {
                PatternInstance selectedPattern = session != null ? session.SelectedPattern : null;
                patternBody.style.display = selectedPattern != null ? DisplayStyle.Flex : DisplayStyle.None;
                inspectorBody.style.display = selectedPattern != null ? DisplayStyle.None : DisplayStyle.Flex;
                if (selectedPattern != null)
                {
                    RefreshPatternInspector(selectedPattern);
                    return;
                }

                int count = session != null ? session.SelectionCount : 0;
                inspectorBody.SetEnabled(count >= 1);
                pressesField.style.display = DisplayStyle.None;
                rateLabel.style.display = DisplayStyle.None;
                // Beat and hold are per note; travel (and damage) also edit a multi-selection.
                beatField.style.display = count > 1 ? DisplayStyle.None : DisplayStyle.Flex;
                holdField.style.display = count > 1 ? DisplayStyle.None : DisplayStyle.Flex;
                travelField.showMixedValue = false;
                travelInfo.text = string.Empty;
                if (count == 0)
                {
                    inspectorHeader.text = "Inspector (nothing selected)";
                    return;
                }

                var selectedNotes = new List<NoteInstance>();
                foreach (string id in session.Selection)
                {
                    NoteInstance found = session.Find(id);
                    if (found != null) selectedNotes.Add(found);
                }
                RefreshTravelField(selectedNotes);

                if (count > 1)
                {
                    inspectorHeader.text = count + " notes selected";
                    int firstDamage = ResolveDamage(selectedNotes[0]);
                    bool mixedDamage = selectedNotes.Exists(note => ResolveDamage(note) != firstDamage);
                    damageField.showMixedValue = mixedDamage;
                    if (!mixedDamage) damageField.SetValueWithoutNotify(firstDamage);
                    return;
                }

                damageField.showMixedValue = false;
                NoteInstance n = selectedNotes.Count > 0 ? selectedNotes[0] : null;
                if (n == null) return;

                string label = n.DefinitionId;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Id == n.DefinitionId) label = entries[i].Group + " " + entries[i].Name;
                }

                inspectorHeader.text = label;
                beatField.SetValueWithoutNotify(n.HitBeat);
                holdField.SetValueWithoutNotify(n.HoldBeats);
                holdField.SetEnabled(RhythmTimingUtility.IsHoldType(NoteMigration.LegacyTypeFor(n.DefinitionId)));
                damageField.SetValueWithoutNotify(ResolveDamage(n));

                bool isMash = n.DefinitionId == NoteMigration.DefinitionIdMash;
                pressesField.style.display = isMash ? DisplayStyle.Flex : DisplayStyle.None;
                rateLabel.style.display = isMash ? DisplayStyle.Flex : DisplayStyle.None;
                if (isMash)
                {
                    int presses = n.MashRequiredPresses.HasValue ? n.MashRequiredPresses.Value : MashRules.DefaultRequiredPresses;
                    pressesField.SetValueWithoutNotify(presses);
                    double seconds = NoteHandles.TravelSeconds(n, DefaultsFor(n.DefinitionId), session.Tempo);
                    double rate = MashRules.PressesPerSecond(presses, seconds);
                    double perfectRate = MashRules.PressesPerSecond(presses, MashRules.PerfectDeadlineSeconds(seconds));
                    MashRateSeverity severity = MashRules.Severity(presses, seconds);
                    string text = rate.ToString("0.0") + " presses/s to clear within " + seconds.ToString("0.00") + " s of travel; "
                        + perfectRate.ToString("0.0") + "/s for a Perfect (fast) clear. Change it with Travel above or the travel handle.";
                    if (severity == MashRateSeverity.Error) text += " TOO FAST, not reliably achievable.";
                    else if (severity == MashRateSeverity.Warning) text += seconds < MashRules.ShortWindowSeconds ? " Very short window." : " Fast.";
                    rateLabel.text = text;
                    rateLabel.style.color = severity == MashRateSeverity.Error ? new Color(1f, 0.4f, 0.4f)
                        : severity == MashRateSeverity.Warning ? new Color(1f, 0.85f, 0.3f) : new Color(0.7f, 0.9f, 0.7f);
                }
            }
            finally
            {
                refreshingInspector = false;
            }
        }
    }
}
