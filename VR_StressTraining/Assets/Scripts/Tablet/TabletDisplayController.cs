using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.Tablet
{
    /// <summary>
    /// Display-only tablet. PHASE 8.5 uses independent layout rows for block,
    /// round, global time and task/level, preventing RectTransform overlap.
    /// </summary>
    public sealed class TabletDisplayController : MonoBehaviour
    {
        public TabletViewModel Model { get; } = new TabletViewModel();

        private Text _blockText, _roundText, _globalTimeText, _taskAndLevelText;
        private Text _trialProgressText, _activeControlsText, _zoneText;
        private Image _zoneDot;
        private GameObject _instructionGroup, _stimulusGroup, _countdownGroup, _pausedGroup;
        private Text _title, _body, _stimulusText, _countdownText, _feedbackText;
        private Image _stimulusPanel;
        private Image[] _grid;
        private Image[] _corsiCells;
        private Text _corsiHint;

        public const string TabletRootName = "TaskTablet";
        public static readonly Vector3 FixedCorridorPosition = new Vector3(0f, 1.32f, -4.52f);
        private static readonly Vector2 DefaultStimulusPanelSize = new Vector2(210, 190);
        private static readonly Vector2 LargeStimulusPanelSize = new Vector2(486, 210);
        private static readonly Color CorsiDim = new Color(0.30f, 0.30f, 0.38f);
        private static readonly Color CorsiLit = new Color(1f, 0.85f, 0.25f);
        private const float CorsiMapWidth = 400f;
        private const float CorsiMapHeight = 130f;

        // ── Flanker presentation (spec §5) ───────────────────────────────
        // The arrow row is the whole point of the trial, so it uses the ENTIRE
        // central StimulusArea instead of the small shared card. The panel behind
        // it is fully transparent (no card/frame), and the glyph row is centred and
        // sized to FlankerWidthRatio of the available width. Scoring and the
        // LEFT/RIGHT response are untouched.
        public static readonly Vector2 StimulusAreaSize = new Vector2(524f, 210f);
        public const float FlankerWidthRatio = 0.80f;   // within the required 70–85 %
        private static readonly Color TransparentPanel = new Color(0f, 0f, 0f, 0f);

        // Flanker is drawn as clean two-stroke geometric chevrons (‹ / ›), NOT font
        // glyphs — five identical, evenly-spaced marks that sit fully inside the panel.
        // Keeping every mark identical (no size/spacing cue for the centre) is what
        // makes the incongruent trials actually interfere, so it stays a real test of
        // selective attention instead of something trivially easy. Response is still
        // the centre chevron's direction (Left/Right); scoring is untouched.
        private const int FlankerCount = 5;
        private const float ChevronSpacing = 74f;    // centre-to-centre; tight ⇒ more interference
        private const float ChevronHalfWidth = 20f;  // horizontal reach of a chevron
        private const float ChevronHalfHeight = 33f; // vertical reach of a chevron
        private const float ChevronThickness = 11f;  // stroke width
        private static readonly Color ChevronColor = new Color(0.91f, 0.94f, 0.99f);

        private RectTransform _flankerRow;
        private ChevronView[] _chevrons;

        private sealed class ChevronView
        {
            public RectTransform Root;
            public Image ArmUpper;
            public Image ArmLower;
        }

        /// <summary>Exposed for layout tests: the flanker chevron row rect.</summary>
        public RectTransform FlankerRowRect => _flankerRow;

        /// <summary>Exposed for layout tests: the stimulus card/frame rect.</summary>
        public RectTransform StimulusPanelRect => _stimulusPanel != null ? _stimulusPanel.rectTransform : null;
        /// <summary>Exposed for layout tests: the glyph rect inside the card.</summary>
        public RectTransform StimulusTextRect => _stimulusText != null ? _stimulusText.rectTransform : null;
        /// <summary>Exposed for layout tests: current card colour (alpha 0 ⇒ no visible card).</summary>
        public Color StimulusPanelColor => _stimulusPanel != null ? _stimulusPanel.color : Color.clear;

        public static TabletDisplayController CreateOrFind(Transform parent)
        {
            var existing = GameObject.Find(TabletRootName);
            if (existing != null)
            {
                var found = existing.GetComponent<TabletDisplayController>();
                if (found != null) return found;
            }

            var root = new GameObject(TabletRootName);
            if (parent != null) root.transform.SetParent(parent, true);
            root.transform.position = FixedCorridorPosition;
            root.transform.rotation = Quaternion.Euler(-8f, 180f, 0f);
            var controller = root.AddComponent<TabletDisplayController>();
            controller.BuildHierarchy();
            return controller;
        }

        public void AlignReadableFaceToViewer(Vector3 viewerEyePosition)
        {
            Vector3 direction = transform.position - viewerEyePosition;
            if (direction.sqrMagnitude >= 0.001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>
        /// Places the tablet at one stable corridor-space pose. It deliberately does
        /// not inherit a robot/claw transform, so tracking and animation cannot make
        /// the standalone build move or flip the readable face.
        /// </summary>
        public void PlaceFixedInCorridor(Transform corridor, Vector3 viewerEyePosition)
        {
            if (corridor == null) return;
            transform.SetParent(corridor, false);
            transform.localPosition = FixedCorridorPosition;
            transform.localScale = Vector3.one;
            AlignReadableFaceToViewer(viewerEyePosition);
        }

        /// <summary>
        /// Snaps the tablet onto a scene anchor. The tablet itself is created at
        /// runtime, so it cannot be positioned in the editor — an anchor object can.
        /// Put a <see cref="TabletAnchorGizmo"/> object where the tablet should be
        /// (e.g. under the robot arm's claw so the arm appears to hold it) and the
        /// tablet takes that exact pose on Play, following the anchor's parent.
        /// </summary>
        public void AttachToAnchor(Transform anchor)
        {
            if (anchor == null) return;
            transform.SetParent(anchor, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        private void Awake()
        {
            if (_blockText == null && transform.childCount == 0) BuildHierarchy();
        }

        private void BuildHierarchy()
        {
            var slabMat = RuntimeVisualUtil.Lit(new Color(0.07f, 0.07f, 0.09f), 0.2f, 0.6f);
            RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "Slab", transform,
                new Vector3(0, 0, 0.012f), new Vector3(0.58f, 0.42f, 0.02f), slabMat);

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            // Readable face is local -Z. Keep the canvas clearly in front of the
            // slab's -Z surface (z=0.002) to avoid mobile depth precision/z-fighting.
            canvasGo.transform.localPosition = new Vector3(0f, 0f, -0.004f);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(540, 380);
            canvasRect.localScale = Vector3.one * 0.001f;

            var bg = MakeImage(canvasGo.transform, "BG", new Color(0.10f, 0.11f, 0.14f));
            Stretch(bg.rectTransform, 0, 0, 0, 0);

            var content = MakeRect(canvasGo.transform, "TabletRoot");
            Stretch(content, 8, 8, -8, -8);
            var vertical = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(0, 0, 0, 0);
            vertical.spacing = 5;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;

            BuildSessionHeader(content);
            BuildStimulusArea(content);
            BuildLocalProgressArea(content);
            BuildFeedbackArea(content);
            BuildOverlays(canvasGo.transform);

            _instructionGroup.SetActive(false);
            _stimulusGroup.SetActive(false);
            _countdownGroup.SetActive(false);
            _pausedGroup.SetActive(false);
            SetSessionProgress(0, 9, 0, 3, "", 0);
        }

        private void BuildSessionHeader(Transform parent)
        {
            var header = MakeImage(parent, "SessionHeader", new Color(0.16f, 0.17f, 0.22f));
            Layout(header.gameObject, 86, 86, 0);
            var v = header.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(10, 10, 6, 6);
            v.spacing = 4;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var progressRow = MakeRect(header.transform, "ProgressRow");
            Layout(progressRow.gameObject, 34, 34, 0);
            var h = progressRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;

            _blockText = MakeText(progressRow, "BlockText", "Blok 0 od 9", 20, TextAnchor.MiddleLeft);
            FlexibleWidth(_blockText.gameObject, 120, 155, 1);
            ConfigureBestFit(_blockText, 15, 20);
            _roundText = MakeText(progressRow, "RoundText", "Runda 0 od 3", 20, TextAnchor.MiddleCenter);
            FlexibleWidth(_roundText.gameObject, 110, 145, 1);
            ConfigureBestFit(_roundText, 15, 20);
            _globalTimeText = MakeText(progressRow, "GlobalTimeText", "", 22, TextAnchor.MiddleRight);
            FlexibleWidth(_globalTimeText.gameObject, 82, 92, 0);
            ConfigureBestFit(_globalTimeText, 17, 22);

            var taskRow = MakeRect(header.transform, "TaskRow");
            Layout(taskRow.gameObject, 32, 32, 0);
            _taskAndLevelText = MakeText(taskRow, "TaskAndLevelText", "", 21, TextAnchor.MiddleCenter);
            Layout(_taskAndLevelText.gameObject, 32, 32, 1);
            Stretch(_taskAndLevelText.rectTransform, 4, 0, -4, 0);
            ConfigureBestFit(_taskAndLevelText, 16, 22);
        }

        private void BuildStimulusArea(Transform parent)
        {
            var area = MakeRect(parent, "StimulusArea");
            Layout(area.gameObject, 210, 210, 0);

            _instructionGroup = MakeRect(area, "Instruction").gameObject;
            Stretch((RectTransform)_instructionGroup.transform, 8, 4, -8, -4);
            _title = MakeText(_instructionGroup.transform, "Title", "", 27, TextAnchor.UpperCenter);
            SetTopHeight(_title.rectTransform, 42);
            _body = MakeText(_instructionGroup.transform, "Body", "", 19, TextAnchor.UpperLeft);
            Stretch(_body.rectTransform, 8, 0, -8, -46);

            _stimulusGroup = MakeRect(area, "Stimulus").gameObject;
            Stretch((RectTransform)_stimulusGroup.transform, 0, 0, 0, 0);
            _stimulusPanel = MakeImage(_stimulusGroup.transform, "Panel", new Color(0.2f, 0.2f, 0.25f));
            var panelRect = _stimulusPanel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = DefaultStimulusPanelSize;
            _stimulusText = MakeText(_stimulusPanel.transform, "Glyph", "", 120, TextAnchor.MiddleCenter);
            Stretch(_stimulusText.rectTransform, 4, 4, -4, -4);
            ConfigureBestFit(_stimulusText, 30, 130);
            BuildPositionGrid();
            BuildCorsiMap();
            BuildFlankerRow(_stimulusGroup.transform);
        }

        // ── Flanker chevron row ──────────────────────────────────────────
        private void BuildFlankerRow(Transform parent)
        {
            _flankerRow = MakeRect(parent, "FlankerRow");
            _flankerRow.anchorMin = _flankerRow.anchorMax = new Vector2(0.5f, 0.5f);
            _flankerRow.pivot = new Vector2(0.5f, 0.5f);
            _flankerRow.anchoredPosition = Vector2.zero;
            _flankerRow.sizeDelta = new Vector2(
                StimulusAreaSize.x * FlankerWidthRatio, StimulusAreaSize.y);

            _chevrons = new ChevronView[FlankerCount];
            for (int i = 0; i < FlankerCount; i++)
            {
                float x = (i - (FlankerCount - 1) * 0.5f) * ChevronSpacing;
                var root = MakeRect(_flankerRow, "Chevron" + i);
                root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
                root.pivot = new Vector2(0.5f, 0.5f);
                root.sizeDelta = new Vector2(ChevronHalfWidth * 2f, ChevronHalfHeight * 2f);
                root.anchoredPosition = new Vector2(x, 0f);

                _chevrons[i] = new ChevronView
                {
                    Root = root,
                    ArmUpper = MakeChevronArm(root, "ArmUpper"),
                    ArmLower = MakeChevronArm(root, "ArmLower")
                };
            }
            _flankerRow.gameObject.SetActive(false);
        }

        private static Image MakeChevronArm(Transform parent, string name)
        {
            var image = MakeImage(parent, name, ChevronColor);
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        private void SetFlankerVisible(bool visible)
        {
            if (_flankerRow != null) _flankerRow.gameObject.SetActive(visible);
        }

        /// <summary>Draws the five chevrons from the decoded glyph row (◄ ► ▬).</summary>
        private void RenderFlanker(string glyphRow)
        {
            for (int i = 0; i < FlankerCount; i++)
            {
                int dir = 0; // neutral
                if (glyphRow != null && i < glyphRow.Length)
                {
                    char ch = glyphRow[i];
                    if (ch == '◄' || ch == '<') dir = -1;
                    else if (ch == '►' || ch == '>') dir = 1;
                }
                SetChevron(_chevrons[i], dir);
            }
            SetFlankerVisible(true);
        }

        /// <summary>dir: -1 left ‹, +1 right ›, 0 neutral —. Two strokes meet at the point.</summary>
        private static void SetChevron(ChevronView c, int dir)
        {
            float a = ChevronHalfWidth, b = ChevronHalfHeight, t = ChevronThickness;

            if (dir == 0)
            {
                // Neutral flanker: a single horizontal dash, non-directional.
                c.ArmUpper.gameObject.SetActive(true);
                c.ArmLower.gameObject.SetActive(false);
                c.ArmUpper.rectTransform.sizeDelta = new Vector2(a * 1.9f, t);
                c.ArmUpper.rectTransform.anchoredPosition = Vector2.zero;
                c.ArmUpper.rectTransform.localRotation = Quaternion.identity;
                return;
            }

            // Right (›): point at (+a,0), arms back to (-a,±b). Left (‹): mirror in x.
            float armLength = Mathf.Sqrt(4f * a * a + b * b);
            float dx = (dir > 0 ? -1f : 1f) * 2f * a;   // horizontal run of each stroke
            float upperAngle = Mathf.Atan2(b, dx) * Mathf.Rad2Deg;
            float lowerAngle = Mathf.Atan2(-b, dx) * Mathf.Rad2Deg;

            c.ArmUpper.gameObject.SetActive(true);
            c.ArmLower.gameObject.SetActive(true);
            c.ArmUpper.rectTransform.sizeDelta = new Vector2(armLength, t);
            c.ArmUpper.rectTransform.anchoredPosition = new Vector2(0f, b * 0.5f);
            c.ArmUpper.rectTransform.localRotation = Quaternion.Euler(0f, 0f, upperAngle);
            c.ArmLower.rectTransform.sizeDelta = new Vector2(armLength, t);
            c.ArmLower.rectTransform.anchoredPosition = new Vector2(0f, -b * 0.5f);
            c.ArmLower.rectTransform.localRotation = Quaternion.Euler(0f, 0f, lowerAngle);
        }

        private void BuildLocalProgressArea(Transform parent)
        {
            var area = MakeImage(parent, "LocalProgressArea", new Color(0.13f, 0.14f, 0.18f));
            Layout(area.gameObject, 28, 28, 0);
            var h = area.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(8, 8, 2, 2);
            h.spacing = 6;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;
            h.childForceExpandWidth = false;

            _trialProgressText = MakeText(area.transform, "TrialProgressText", "", 16, TextAnchor.MiddleLeft);
            FlexibleWidth(_trialProgressText.gameObject, 82, 100, 0);
            _activeControlsText = MakeText(area.transform, "ActiveControlsText", "", 15, TextAnchor.MiddleCenter);
            FlexibleWidth(_activeControlsText.gameObject, 120, 190, 1);
            ConfigureBestFit(_activeControlsText, 12, 16);
            _zoneText = MakeText(area.transform, "HrRecordingStatusText", "", 14, TextAnchor.MiddleRight);
            FlexibleWidth(_zoneText.gameObject, 145, 170, 0);
            ConfigureBestFit(_zoneText, 11, 14);
            _zoneDot = MakeImage(area.transform, "HrRecordingStatusDot", Color.gray);
            Layout(_zoneDot.gameObject, 12, 12, 0);
            FlexibleWidth(_zoneDot.gameObject, 12, 12, 0);
            _zoneText.gameObject.SetActive(false);
            _zoneDot.gameObject.SetActive(false);
        }

        private void BuildFeedbackArea(Transform parent)
        {
            var area = MakeRect(parent, "FeedbackArea");
            Layout(area.gameObject, 25, 25, 0);
            _feedbackText = MakeText(area, "FeedbackText", "", 19, TextAnchor.MiddleCenter);
            Stretch(_feedbackText.rectTransform, 4, 0, -4, 0);
        }

        private void BuildOverlays(Transform parent)
        {
            _countdownGroup = MakeRect(parent, "Countdown").gameObject;
            Stretch((RectTransform)_countdownGroup.transform, 0, 0, 0, 0);
            var countdownBg = MakeImage(_countdownGroup.transform, "Dim", new Color(0.05f, 0.05f, 0.07f, 0.92f));
            Stretch(countdownBg.rectTransform, 0, 0, 0, 0);
            _countdownText = MakeText(_countdownGroup.transform, "Num", "", 140, TextAnchor.MiddleCenter);
            Stretch(_countdownText.rectTransform, 0, 0, 0, 0);

            // The dim layer blanks the task so nothing can be read or answered while
            // paused; the hint line tells the participant where the controls are,
            // because the actual Continue / End buttons live on the menu panel in
            // front of them, not on the tablet.
            _pausedGroup = MakeRect(parent, "PausedOverlay").gameObject;
            Stretch((RectTransform)_pausedGroup.transform, 0, 0, 0, 0);
            var pausedBg = MakeImage(_pausedGroup.transform, "Dim", new Color(0.05f, 0.05f, 0.07f, 0.95f));
            Stretch(pausedBg.rectTransform, 0, 0, 0, 0);
            var pausedText = MakeText(_pausedGroup.transform, "Txt", "PAUZA", 60, TextAnchor.MiddleCenter);
            Stretch(pausedText.rectTransform, 0, 60, 0, 0);
            var pausedHint = MakeText(_pausedGroup.transform, "Hint",
                "Meni je ispred tebe: „Nastavi“ ili „Završi sesiju“.\n" +
                "Y na lijevom kontroleru takođe nastavlja.", 20, TextAnchor.UpperCenter);
            pausedHint.color = new Color(0.72f, 0.75f, 0.82f);
            SetBottomHeight(pausedHint.rectTransform, 62);
        }

        private void BuildPositionGrid()
        {
            _grid = new Image[9];
            for (int i = 0; i < _grid.Length; i++)
            {
                var cell = MakeImage(_stimulusPanel.transform, "Cell" + i, new Color(0.28f, 0.28f, 0.34f));
                var rect = cell.rectTransform;
                int column = i % 3, row = i / 3;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(56, 56);
                rect.anchoredPosition = new Vector2((column - 1) * 64, (1 - row) * 58);
                cell.gameObject.SetActive(false);
                _grid[i] = cell;
            }
        }

        private void BuildCorsiMap()
        {
            _corsiCells = new Image[CorsiLayout.PositionCount];
            for (int i = 0; i < _corsiCells.Length; i++)
            {
                var cell = MakeImage(_stimulusPanel.transform, "Corsi" + i, CorsiDim);
                var rect = cell.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(42, 42);
                rect.anchoredPosition = CorsiLayout.GetTabletPosition(i, CorsiMapWidth, CorsiMapHeight);
                cell.gameObject.SetActive(false);
                _corsiCells[i] = cell;
            }
            _corsiHint = MakeText(_stimulusPanel.transform, "CorsiHint", "", 18, TextAnchor.LowerCenter);
            var hintRect = _corsiHint.rectTransform;
            hintRect.anchorMin = new Vector2(0, 0);
            hintRect.anchorMax = new Vector2(1, 0);
            hintRect.pivot = new Vector2(0.5f, 0);
            hintRect.sizeDelta = new Vector2(0, 28);
            hintRect.anchoredPosition = new Vector2(0, 1);
            _corsiHint.gameObject.SetActive(false);
        }

        public void ShowInstruction(string title, string body)
        {
            ClearFeedback();
            Model.Screen = TabletScreenState.Instruction;
            Model.Title = title ?? "";
            Model.Body = body ?? "";
            _title.text = Model.Title;
            _body.text = Model.Body;
            _instructionGroup.SetActive(true);
            _stimulusGroup.SetActive(false);
            _countdownGroup.SetActive(false);
        }

        public void ShowStimulus(string encoded)
        {
            ClearFeedback();
            Model.Screen = TabletScreenState.Stimulus;
            Model.Stimulus = TabletViewModel.Decode(encoded);
            RenderStimulus(Model.Stimulus);
            _instructionGroup.SetActive(false);
            _stimulusGroup.SetActive(true);
            _countdownGroup.SetActive(false);
        }

        public void HideStimulus()
        {
            ApplyStimulusFrame(StimulusVisualKind.None);
            _stimulusPanel.color = new Color(0.2f, 0.2f, 0.25f);
            _stimulusText.text = "";
            foreach (var cell in _grid) cell.gameObject.SetActive(false);
            SetCorsiMapVisible(false);
            SetFlankerVisible(false);
        }

        /// <summary>
        /// Sizes the stimulus card and the glyph rect for one stimulus kind.
        /// Flanker (Arrows) claims the full StimulusArea with no visible card, so
        /// the arrow row is large and unobstructed; every other kind keeps its
        /// existing card geometry.
        /// </summary>
        private void ApplyStimulusFrame(StimulusVisualKind kind)
        {
            bool flanker = kind == StimulusVisualKind.Arrows;
            var panelRect = _stimulusPanel.rectTransform;
            var textRect = _stimulusText.rectTransform;

            if (flanker)
            {
                // The chevrons are their own row (see BuildFlankerRow); the shared card
                // is claimed at full size but drawn transparent, and the glyph text is
                // unused here.
                panelRect.sizeDelta = StimulusAreaSize;
                _stimulusText.text = "";
                return;
            }

            panelRect.sizeDelta =
                kind == StimulusVisualKind.GoNoGo || kind == StimulusVisualKind.CorsiMap
                    ? LargeStimulusPanelSize : DefaultStimulusPanelSize;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            Stretch(textRect, 4, 4, -4, -4);
            ConfigureBestFit(_stimulusText, 30, 130);
            _stimulusText.alignment = TextAnchor.MiddleCenter;
            _stimulusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private void RenderStimulus(StimulusVisual visual)
        {
            foreach (var cell in _grid) cell.gameObject.SetActive(false);
            SetCorsiMapVisible(visual.Kind == StimulusVisualKind.CorsiMap);
            SetFlankerVisible(visual.Kind == StimulusVisualKind.Arrows);
            ApplyStimulusFrame(visual.Kind);

            switch (visual.Kind)
            {
                case StimulusVisualKind.CorsiMap:
                    _stimulusPanel.color = new Color(0.13f, 0.13f, 0.17f);
                    _stimulusText.text = "";
                    break;
                case StimulusVisualKind.Arrows:
                    // Clean geometric chevrons on the bare tablet body (no card).
                    _stimulusPanel.color = TransparentPanel;
                    _stimulusText.text = "";
                    RenderFlanker(visual.Text);
                    break;
                case StimulusVisualKind.Symbol:
                    _stimulusPanel.color = new Color(0.2f, 0.2f, 0.25f);
                    _stimulusText.text = visual.Text;
                    _stimulusText.color = visual.Color;
                    break;
                case StimulusVisualKind.Color:
                    _stimulusPanel.color = visual.Color;
                    _stimulusText.text = "";
                    break;
                case StimulusVisualKind.GoNoGo:
                    _stimulusPanel.color = Color.white;
                    _stimulusText.color = Color.black;
                    _stimulusText.text = visual.IsNoGo ? "NO GO" : "GO";
                    break;
                case StimulusVisualKind.Position:
                    _stimulusPanel.color = new Color(0.16f, 0.16f, 0.2f);
                    _stimulusText.text = "";
                    for (int i = 0; i < _grid.Length; i++)
                    {
                        _grid[i].gameObject.SetActive(true);
                        _grid[i].color = i == visual.PositionIndex
                            ? visual.Color : new Color(0.28f, 0.28f, 0.34f);
                    }
                    break;
                default:
                    HideStimulus();
                    break;
            }
        }

        public void ShowCorsiLit(int positionIndex, int step, int totalSteps)
        {
            if (_corsiCells == null || positionIndex < 0 || positionIndex >= _corsiCells.Length) return;
            for (int i = 0; i < _corsiCells.Length; i++)
                _corsiCells[i].color = i == positionIndex ? CorsiLit : CorsiDim;
            _corsiHint.text = $"Prati redosljed ({step}/{totalSteps})";
            _corsiHint.color = new Color(0.75f, 0.77f, 0.82f);
        }

        public void DimCorsiCells()
        {
            if (_corsiCells == null) return;
            foreach (var cell in _corsiCells) cell.color = CorsiDim;
        }

        public void ShowCorsiResponseHint(bool backward = false)
        {
            if (_corsiHint == null) return;
            _corsiHint.gameObject.SetActive(true);
            _corsiHint.text = backward
                ? "Ponovi sekvencu UNAZAD, od posljednje pozicije"
                : "Ponovi redosljed na konzoli";
            _corsiHint.color = new Color(0.55f, 0.9f, 0.6f);
        }

        private void SetCorsiMapVisible(bool visible)
        {
            if (_corsiCells == null) return;
            foreach (var cell in _corsiCells)
            {
                cell.color = CorsiDim;
                cell.gameObject.SetActive(visible);
            }
            if (_corsiHint != null)
            {
                _corsiHint.gameObject.SetActive(visible);
                if (visible) _corsiHint.text = "Gledaj redosljed";
            }
        }

        public void ShowCountdown(int value)
        {
            ClearFeedback();
            Model.Screen = TabletScreenState.Countdown;
            Model.CountdownValue = value;
            _countdownText.text = value.ToString();
            _countdownGroup.SetActive(true);
        }

        public void HideCountdown() => _countdownGroup.SetActive(false);

        public void ShowBlockTransition(string nextTaskName, int blockNumber, int blockTotal, float accuracyPrev)
        {
            string previous = accuracyPrev >= 0 ? $"Tačnost prethodnog bloka: {accuracyPrev * 100f:0}%\n\n" : "";
            ShowInstruction($"Blok {blockNumber}/{blockTotal}",
                previous + $"Sljedeći zadatak: {nextTaskName}\n\nPripremi se…");
            Model.Screen = TabletScreenState.BlockTransition;
        }

        public void SetSessionProgress(int currentBlock, int totalBlocks,
            int currentRound, int totalRounds, string taskName, int difficultyLevel)
        {
            Model.CurrentBlock = Mathf.Max(0, currentBlock);
            Model.TotalBlocks = Mathf.Max(0, totalBlocks);
            Model.CurrentRound = Mathf.Max(0, currentRound);
            Model.TotalRounds = Mathf.Max(0, totalRounds);
            Model.TaskName = taskName ?? "";
            Model.DifficultyLevel = Mathf.Max(0, difficultyLevel);
            RefreshSessionHeader();
        }

        /// <summary>Compatibility API for demo flows; production uses SetSessionProgress.</summary>
        public void SetHeader(string blockInfo)
        {
            Model.HeaderBlockInfo = blockInfo ?? "";
            if (Model.CurrentBlock <= 0)
                _taskAndLevelText.text = Model.HeaderBlockInfo;
        }

        public void SetTrialProgress(int currentTrial, int totalTrials)
        {
            Model.TotalTrials = Mathf.Max(0, totalTrials);
            Model.CurrentTrial = Model.TotalTrials == 0 ? 0 : Mathf.Clamp(currentTrial, 0, Model.TotalTrials);
            _trialProgressText.text = Model.TotalTrials > 0
                ? $"Pokušaj {Model.CurrentTrial}/{Model.TotalTrials}" : "";
        }

        private void RefreshSessionHeader()
        {
            _blockText.text = Model.TotalBlocks > 0
                ? $"Blok {Model.CurrentBlock} od {Model.TotalBlocks}" : "";
            _roundText.text = Model.TotalRounds > 0
                ? $"Runda {Model.CurrentRound} od {Model.TotalRounds}" : "";
            _taskAndLevelText.text = !string.IsNullOrEmpty(Model.TaskName) && Model.DifficultyLevel > 0
                ? $"{Model.TaskName} · nivo {Model.DifficultyLevel}"
                : Model.TaskName;
        }

        public void SetTimer(double secondsRemaining)
        {
            if (secondsRemaining < 0)
            {
                Model.HeaderTimer = "";
                _globalTimeText.text = "";
                return;
            }
            int minutes = (int)(secondsRemaining / 60);
            int seconds = (int)(secondsRemaining % 60);
            Model.HeaderTimer = $"{minutes:0}:{seconds:00}";
            _globalTimeText.text = Model.HeaderTimer;
        }

        public void SetActiveControls(TaskType taskType)
        {
            Model.ActiveTask = taskType;
            switch (taskType)
            {
                case TaskType.NBack: Model.ActiveControlsText = "MATCH · NO MATCH"; break;
                case TaskType.GoNoGo: Model.ActiveControlsText = "GO"; break;
                case TaskType.Flanker: Model.ActiveControlsText = "LEFT · RIGHT"; break;
                case TaskType.CorsiSequence: Model.ActiveControlsText = "9 Corsi pozicija"; break;
                default: Model.ActiveControlsText = ""; break;
            }
            _activeControlsText.text = Model.ActiveControlsText;
        }

        public void ShowTrialFeedback(string message, bool correct)
        {
            Model.Screen = TabletScreenState.Feedback;
            Model.FeedbackText = message ?? "";
            Model.FeedbackIsCorrect = correct;
            _feedbackText.text = Model.FeedbackText;
            _feedbackText.color = correct ? new Color(0.30f, 0.90f, 0.45f) : new Color(1f, 0.42f, 0.30f);
        }

        public void ClearFeedback()
        {
            Model.FeedbackText = "";
            if (_feedbackText != null) _feedbackText.text = "";
        }

        public void SetHeartRateRecordingActive(bool active)
        {
            Model.HrRecordingActive = active;
            if (_zoneText == null || _zoneDot == null) return;
            _zoneText.gameObject.SetActive(active);
            _zoneDot.gameObject.SetActive(active);
            if (!active) return;
            _zoneText.text = "HR snimanje aktivno";
            _zoneDot.color = new Color(0.25f, 0.78f, 0.38f);
        }

        public void SetHrZone(HrZone zone, bool visible,
            HrSourceType sourceType = HrSourceType.NetworkBridge)
        {
            Model.HrZone = zone;
            Model.HrSourceType = sourceType;
            bool showRealSource = visible && sourceType == HrSourceType.NetworkBridge;
            Model.ShowHrZone = showRealSource;
            if (Model.HrRecordingActive)
            {
                _zoneText.gameObject.SetActive(true);
                _zoneDot.gameObject.SetActive(true);
                _zoneText.text = "HR snimanje aktivno";
                switch (zone)
                {
                    case HrZone.Stable: _zoneDot.color = new Color(0.25f, 0.75f, 0.35f); break;
                    case HrZone.Elevated: _zoneDot.color = new Color(0.95f, 0.75f, 0.2f); break;
                    case HrZone.High: _zoneDot.color = new Color(0.9f, 0.3f, 0.2f); break;
                    default: _zoneDot.color = Color.gray; break;
                }
                return;
            }
            _zoneText.gameObject.SetActive(showRealSource);
            _zoneDot.gameObject.SetActive(showRealSource);
            if (!showRealSource) return;
            _zoneText.text = "HR signal aktivan";
        }

        public void SetPausedOverlay(bool paused)
        {
            Model.Screen = paused ? TabletScreenState.Paused : Model.Screen;
            _pausedGroup.SetActive(paused);
        }

        public void ShowIdle(string message = "")
        {
            Model.Screen = TabletScreenState.Idle;
            SetSessionProgress(0, 0, 0, 0, "", 0);
            SetTrialProgress(0, 0);
            SetActiveControls(TaskType.None);
            SetTimer(-1);
            ShowInstruction("", message);
        }

        private static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image MakeImage(Transform parent, string name, Color color)
        {
            var rect = MakeRect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text MakeText(Transform parent, string name, string value, int size, TextAnchor anchor)
        {
            var rect = MakeRect(parent, name);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = RuntimeVisualUtil.BuiltinFont;
            text.text = value;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(0.92f, 0.93f, 0.96f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static void ConfigureBestFit(Text text, int min, int max)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = min;
            text.resizeTextMaxSize = max;
        }

        private static void FlexibleWidth(GameObject gameObject, float minWidth,
            float preferredWidth, float flexibleWidth)
        {
            var element = gameObject.GetComponent<LayoutElement>() ??
                          gameObject.AddComponent<LayoutElement>();
            element.minWidth = minWidth;
            element.preferredWidth = preferredWidth;
            element.flexibleWidth = flexibleWidth;
        }

        private static void Layout(GameObject gameObject, float preferredHeight,
            float minHeight, float flexibleHeight, float preferredWidth = -1)
        {
            var element = gameObject.AddComponent<LayoutElement>();
            if (preferredHeight >= 0) element.preferredHeight = preferredHeight;
            if (minHeight >= 0) element.minHeight = minHeight;
            element.flexibleHeight = flexibleHeight;
            if (preferredWidth >= 0) element.preferredWidth = preferredWidth;
            if (preferredWidth > 0) element.minWidth = preferredWidth;
        }

        private static void SetTopHeight(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.sizeDelta = new Vector2(0, height);
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SetBottomHeight(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.sizeDelta = new Vector2(0, height);
            rect.anchoredPosition = new Vector2(0, 12f);
        }

        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
    }

    /// <summary>
    /// Editor-visible stand-in for the runtime-created tablet. The tablet is built
    /// procedurally on Play, so it cannot be placed in the scene by hand — drag THIS
    /// object instead and the tablet will spawn exactly here (see
    /// <see cref="TabletDisplayController.AttachToAnchor"/>). Parent it under the
    /// robot arm's claw to have the arm hold the tablet.
    /// </summary>
    public sealed class TabletAnchorGizmo : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.9f);
            // Matches the real slab: 0.58 x 0.42 m, 0.02 thick.
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.58f, 0.42f, 0.02f));
            // The readable face points along local -Z (toward the participant).
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, Vector3.back * 0.25f);
        }
#endif
    }
}
