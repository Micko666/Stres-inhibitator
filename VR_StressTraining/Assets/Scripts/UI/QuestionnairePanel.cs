using System;
using System.Collections.Generic;
using StressTraining.Data;
using StressTraining.Questionnaires;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StressTraining.UI
{
    /// <summary>
    /// World-space questionnaire UI optimized for Quest ray + trigger input.
    /// SSQ and STAI render paged answer grids with real uGUI Buttons. Raw
    /// NASA-TLX renders one dimension at a time with clickable step and quick
    /// select controls. Responses stay local while navigating and are submitted
    /// to the existing linear flow only after the current questionnaire is done.
    /// </summary>
    public sealed class QuestionnairePanel : MenuPanelBase
    {
        private const int SsqItemsPerPage = 4;
        private const int StaiItemsPerPage = 3;
        private const int TlxStep = 5;

        private static readonly Color AnswerNormal = new Color(0.15f, 0.16f, 0.20f);
        private static readonly Color AnswerHover = new Color(0.24f, 0.48f, 0.78f);
        private static readonly Color AnswerPressed = new Color(0.12f, 0.68f, 0.48f);
        private static readonly Color AnswerSelected = new Color(0.16f, 0.55f, 0.48f);
        private static readonly Color AnswerSelectedHover = new Color(0.20f, 0.68f, 0.58f);
        private static readonly Color Disabled = new Color(0.08f, 0.09f, 0.11f, 0.62f);
        private static readonly Color FooterNormal = new Color(0.17f, 0.20f, 0.27f);
        private static readonly Color FooterHover = new Color(0.24f, 0.48f, 0.78f);
        private static readonly Color FooterPressed = new Color(0.15f, 0.63f, 0.48f);

        private sealed class AnswerRowView
        {
            public string ItemId;
            public Button[] Buttons;
        }

        public event Action Finished;

        private QuestionnaireFlowController _flow;
        private Action _allCompletedHandler;
        private QuestionnaireDefinitionData _questionnaire;
        private readonly Dictionary<string, int> _answers = new Dictionary<string, int>();
        private readonly List<AnswerRowView> _visibleRows = new List<AnswerRowView>();

        private RectTransform _contentRoot;
        private GameObject _introRoot;
        private Text _introBody;
        private Button _startButton;

        private GameObject _pagedRoot;
        private Text _pageText;
        private Text _answeredText;
        private Text _instructionText;
        private RectTransform _columnHeaderRoot;
        private RectTransform _rowsRoot;
        private Button _backButton;
        private Button _nextButton;

        private GameObject _tlxRoot;
        private Text _tlxProgressText;
        private Text _tlxInstructionText;
        private Text _tlxItemText;
        private Text _tlxEnglishText;
        private Text _tlxValueText;
        private Text _tlxAnchorsText;
        private Button _tlxMinusButton;
        private Button _tlxPlusButton;
        private Button _tlxBackButton;
        private Button _tlxConfirmButton;
        private readonly List<Button> _tlxQuickButtons = new List<Button>();

        private int _pageIndex;
        private int _tlxValue = 50;
        private bool _showingIntro;
        private bool _isSubmitting;
        private bool _finishedRaised;
        private int _navRow;
        private int _navColumn;

        public override void Build(Transform canvasRoot, string panelName)
        {
            base.Build(canvasRoot, panelName);

            // QuestionnairePanel owns its full content layout. The inherited
            // list/body/footer remain available to other panels but are hidden here.
            BodyText.gameObject.SetActive(false);
            ListRoot.gameObject.SetActive(false);
            InfoText.gameObject.SetActive(false);

            _contentRoot = new GameObject("QuestionnaireContent", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _contentRoot.SetParent(PanelRoot.transform, false);
            UiBuilder.Stretch(_contentRoot, 24f, 16f, 24f, 66f);
            _contentRoot.gameObject.AddComponent<RectMask2D>();

            BuildIntroView();
            BuildPagedView();
            BuildTlxView();
            HideAllViews();
        }

        public void Begin(QuestionnaireFlowController flow)
        {
            DisconnectFlow();

            _flow = flow;
            _finishedRaised = false;
            _isSubmitting = false;
            _allCompletedHandler = HandleFlowAllCompleted;

            if (_flow == null)
            {
                HideAllViews();
                return;
            }

            _flow.AllCompleted += _allCompletedHandler;
            if (_flow.IsFinished || _flow.CurrentQuestionnaire == null)
            {
                HandleFlowAllCompleted();
                return;
            }

            PrepareCurrentQuestionnaire();
            ShowIntroduction();
        }

        private void OnDestroy()
        {
            DisconnectFlow();
        }

        private void DisconnectFlow()
        {
            if (_flow != null && _allCompletedHandler != null)
                _flow.AllCompleted -= _allCompletedHandler;

            _flow = null;
            _allCompletedHandler = null;
        }

        private void HandleFlowAllCompleted()
        {
            if (_finishedRaised) return;
            _finishedRaised = true;
            Finished?.Invoke();
        }

        private void PrepareCurrentQuestionnaire()
        {
            _questionnaire = _flow != null ? _flow.CurrentQuestionnaire : null;
            _answers.Clear();
            _visibleRows.Clear();
            _pageIndex = 0;
            _tlxValue = 50;
            _navRow = 0;
            _navColumn = 0;
            _isSubmitting = false;
        }

        private void BuildIntroView()
        {
            _introRoot = new GameObject("IntroView", typeof(RectTransform));
            _introRoot.transform.SetParent(_contentRoot, false);
            UiBuilder.Stretch((RectTransform)_introRoot.transform);

            _introBody = UiBuilder.Text(_introRoot.transform, "IntroBody", "", 23,
                TextAnchor.MiddleCenter);
            SetRect(_introBody.rectTransform, 0.10f, 0.34f, 0.90f, 0.82f, 12f, 10f, -12f, -10f);

            _startButton = CreateButton(_introRoot.transform, "StartButton", "Počni", 24,
                FooterNormal, FooterHover, FooterPressed, UiBuilder.RowSelectedBg, Disabled);
            SetRect(_startButton.GetComponent<RectTransform>(), 0.34f, 0.13f, 0.66f, 0.29f,
                0f, 0f, 0f, 0f);
            _startButton.onClick.AddListener(StartCurrentQuestionnaire);
        }

        private void BuildPagedView()
        {
            _pagedRoot = new GameObject("PagedQuestionnaireView", typeof(RectTransform));
            _pagedRoot.transform.SetParent(_contentRoot, false);
            UiBuilder.Stretch((RectTransform)_pagedRoot.transform);

            _pageText = UiBuilder.Text(_pagedRoot.transform, "PageText", "", 18,
                TextAnchor.MiddleLeft, UiBuilder.TextDim);
            SetRect(_pageText.rectTransform, 0f, 0.92f, 0.5f, 1f, 4f, 0f, -4f, 0f);

            _answeredText = UiBuilder.Text(_pagedRoot.transform, "AnsweredText", "", 18,
                TextAnchor.MiddleRight, UiBuilder.TextDim);
            SetRect(_answeredText.rectTransform, 0.5f, 0.92f, 1f, 1f, 4f, 0f, -4f, 0f);

            _instructionText = UiBuilder.Text(_pagedRoot.transform, "Instruction", "", 21,
                TextAnchor.MiddleCenter);
            SetRect(_instructionText.rectTransform, 0.03f, 0.82f, 0.97f, 0.92f,
                0f, 0f, 0f, 0f);

            _columnHeaderRoot = new GameObject("ColumnHeaders", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _columnHeaderRoot.SetParent(_pagedRoot.transform, false);
            SetRect(_columnHeaderRoot, 0f, 0.73f, 1f, 0.82f, 0f, 0f, 0f, 0f);

            _rowsRoot = new GameObject("Rows", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _rowsRoot.SetParent(_pagedRoot.transform, false);
            SetRect(_rowsRoot, 0f, 0.17f, 1f, 0.73f, 0f, 0f, 0f, 0f);

            _backButton = CreateButton(_pagedRoot.transform, "BackButton", "Nazad", 21,
                FooterNormal, FooterHover, FooterPressed, UiBuilder.RowSelectedBg, Disabled);
            SetRect(_backButton.GetComponent<RectTransform>(), 0.04f, 0.02f, 0.28f, 0.13f,
                0f, 0f, 0f, 0f);
            _backButton.onClick.AddListener(GoBackFromPagedView);

            _nextButton = CreateButton(_pagedRoot.transform, "NextButton", "Dalje", 21,
                FooterNormal, FooterHover, FooterPressed, UiBuilder.RowSelectedBg, Disabled);
            SetRect(_nextButton.GetComponent<RectTransform>(), 0.72f, 0.02f, 0.96f, 0.13f,
                0f, 0f, 0f, 0f);
            _nextButton.onClick.AddListener(GoForwardFromPagedView);
        }

        private void BuildTlxView()
        {
            _tlxRoot = new GameObject("TlxView", typeof(RectTransform));
            _tlxRoot.transform.SetParent(_contentRoot, false);
            UiBuilder.Stretch((RectTransform)_tlxRoot.transform);

            _tlxProgressText = UiBuilder.Text(_tlxRoot.transform, "TlxProgress", "", 18,
                TextAnchor.MiddleRight, UiBuilder.TextDim);
            SetRect(_tlxProgressText.rectTransform, 0.45f, 0.92f, 1f, 1f, 0f, 0f, -4f, 0f);

            _tlxInstructionText = UiBuilder.Text(_tlxRoot.transform, "TlxInstruction", "", 20,
                TextAnchor.MiddleCenter);
            SetRect(_tlxInstructionText.rectTransform, 0.04f, 0.82f, 0.96f, 0.92f,
                0f, 0f, 0f, 0f);

            _tlxItemText = UiBuilder.Text(_tlxRoot.transform, "TlxItem", "", 24,
                TextAnchor.MiddleCenter);
            SetRect(_tlxItemText.rectTransform, 0.06f, 0.62f, 0.94f, 0.82f,
                0f, 0f, 0f, 0f);

            _tlxEnglishText = UiBuilder.Text(_tlxRoot.transform, "TlxEnglish", "", 14,
                TextAnchor.UpperCenter, UiBuilder.TextDim);
            SetRect(_tlxEnglishText.rectTransform, 0.08f, 0.56f, 0.92f, 0.63f,
                0f, 0f, 0f, 0f);

            _tlxMinusButton = CreateButton(_tlxRoot.transform, "TlxMinus", "−5", 26,
                AnswerNormal, AnswerHover, AnswerPressed, AnswerSelected, Disabled);
            SetRect(_tlxMinusButton.GetComponent<RectTransform>(), 0.18f, 0.39f, 0.34f, 0.55f,
                0f, 0f, 0f, 0f);
            _tlxMinusButton.onClick.AddListener(() => AdjustTlxValue(-TlxStep));

            _tlxValueText = UiBuilder.Text(_tlxRoot.transform, "TlxValue", "50", 42,
                TextAnchor.MiddleCenter);
            SetRect(_tlxValueText.rectTransform, 0.36f, 0.38f, 0.64f, 0.56f,
                0f, 0f, 0f, 0f);

            _tlxPlusButton = CreateButton(_tlxRoot.transform, "TlxPlus", "+5", 26,
                AnswerNormal, AnswerHover, AnswerPressed, AnswerSelected, Disabled);
            SetRect(_tlxPlusButton.GetComponent<RectTransform>(), 0.66f, 0.39f, 0.82f, 0.55f,
                0f, 0f, 0f, 0f);
            _tlxPlusButton.onClick.AddListener(() => AdjustTlxValue(TlxStep));

            _tlxAnchorsText = UiBuilder.Text(_tlxRoot.transform, "TlxAnchors", "", 15,
                TextAnchor.MiddleCenter, UiBuilder.TextDim);
            SetRect(_tlxAnchorsText.rectTransform, 0.08f, 0.32f, 0.92f, 0.39f,
                0f, 0f, 0f, 0f);

            int[] quickValues = { 0, 25, 50, 75, 100 };
            for (int i = 0; i < quickValues.Length; i++)
            {
                int value = quickValues[i];
                var quick = CreateButton(_tlxRoot.transform, "Quick_" + value, value.ToString(), 19,
                    AnswerNormal, AnswerHover, AnswerPressed, AnswerSelected, Disabled);
                float width = 0.14f;
                float gap = 0.015f;
                float total = quickValues.Length * width + (quickValues.Length - 1) * gap;
                float left = (1f - total) * 0.5f + i * (width + gap);
                SetRect(quick.GetComponent<RectTransform>(), left, 0.20f, left + width, 0.31f,
                    0f, 0f, 0f, 0f);
                quick.onClick.AddListener(() => SetTlxValue(value));
                _tlxQuickButtons.Add(quick);
            }

            _tlxBackButton = CreateButton(_tlxRoot.transform, "TlxBack", "Nazad", 21,
                FooterNormal, FooterHover, FooterPressed, UiBuilder.RowSelectedBg, Disabled);
            SetRect(_tlxBackButton.GetComponent<RectTransform>(), 0.04f, 0.02f, 0.28f, 0.13f,
                0f, 0f, 0f, 0f);
            _tlxBackButton.onClick.AddListener(GoBackFromTlx);

            _tlxConfirmButton = CreateButton(_tlxRoot.transform, "TlxConfirm", "Potvrdi", 21,
                FooterNormal, FooterHover, FooterPressed, UiBuilder.RowSelectedBg, Disabled);
            SetRect(_tlxConfirmButton.GetComponent<RectTransform>(), 0.72f, 0.02f, 0.96f, 0.13f,
                0f, 0f, 0f, 0f);
            _tlxConfirmButton.onClick.AddListener(ConfirmTlxValue);
        }

        private void ShowIntroduction()
        {
            if (_questionnaire == null) return;

            HideAllViews();
            _showingIntro = true;
            _introRoot.SetActive(true);
            SetTitle(GetIntroTitle());
            _introBody.text = GetIntroBody();
            _startButton.interactable = !_isSubmitting;
            UpdateButtonLabel(_startButton, "Počni");
            SelectForFallback(_startButton);
        }

        private string GetIntroTitle()
        {
            if (_questionnaire.questionnaireId == QuestionnaireCatalog.SsqId)
                return "Upitnik simulatorske mučnine";
            if (_questionnaire.questionnaireId == QuestionnaireCatalog.StaiId)
                return "Kratka skala trenutnog osjećanja";
            if (_questionnaire.questionnaireId == QuestionnaireCatalog.TlxId)
                return "Procjena opterećenja (NASA-TLX)";
            return _questionnaire.displayName;
        }

        private string GetIntroBody()
        {
            if (_questionnaire.questionnaireId == QuestionnaireCatalog.SsqId)
            {
                return _flow.Phase == QuestionnairePhase.PostSession
                    ? "Nakon sesije označi kako se trenutno osjećaš. Upitnik sadrži 16 simptoma i traje približno jedan minut."
                    : "Prije sesije označi kako se trenutno osjećaš. Upitnik sadrži 16 simptoma i traje približno jedan minut.";
            }

            if (_questionnaire.questionnaireId == QuestionnaireCatalog.StaiId)
                return "Označi kako se osjećaš upravo sada. Upitnik sadrži šest kratkih tvrdnji.";

            if (_questionnaire.questionnaireId == QuestionnaireCatalog.TlxId)
                return "Procijeni upravo završenu sesiju kroz šest dimenzija opterećenja.";

            return _questionnaire.instructions;
        }

        private void StartCurrentQuestionnaire()
        {
            if (_questionnaire == null || _isSubmitting) return;
            _showingIntro = false;
            _pageIndex = 0;
            _navRow = 0;
            _navColumn = 0;

            if (IsTlx()) RenderTlx();
            else RenderPagedQuestionnaire();
        }

        private void RenderPagedQuestionnaire()
        {
            if (_questionnaire == null) return;

            HideAllViews();
            _showingIntro = false;
            _pagedRoot.SetActive(true);
            SetTitle(GetPagedTitle());
            _instructionText.text = IsSsq()
                ? "Za svaki simptom izaberi koliko je trenutno izražen."
                : "Za svaku tvrdnju izaberi odgovor koji najbolje opisuje kako se trenutno osjećaš.";

            int itemsPerPage = GetItemsPerPage();
            int pageCount = GetPageCount(itemsPerPage);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, Math.Max(0, pageCount - 1));

            BuildColumnHeaders();
            BuildRows(itemsPerPage);
            UpdatePagedStatus();
            FocusPagedFallback();
        }

        private string GetPagedTitle()
        {
            if (IsSsq())
            {
                if (_flow.Phase == QuestionnairePhase.PreSession) return "SSQ prije sesije";
                if (_flow.Phase == QuestionnairePhase.PostSession) return "SSQ poslije sesije";
                return "SSQ";
            }

            if (IsStai()) return "STAI-6";
            return _questionnaire.displayName;
        }

        private void BuildColumnHeaders()
        {
            DestroyChildren(_columnHeaderRoot);

            string first = IsSsq() ? "SIMPTOM" : "TVRDNJA";
            var firstText = UiBuilder.Text(_columnHeaderRoot, "ItemHeader", first, 16,
                TextAnchor.MiddleLeft, UiBuilder.TextDim);
            SetRect(firstText.rectTransform, 0f, 0f, 0.40f, 1f, 8f, 0f, -4f, 0f);

            int optionCount = _questionnaire.scaleMax - _questionnaire.scaleMin + 1;
            for (int option = 0; option < optionCount; option++)
            {
                int value = _questionnaire.scaleMin + option;
                string label = value + " " + GetScaleHeaderLabel(option);
                var text = UiBuilder.Text(_columnHeaderRoot, "ScaleHeader_" + value,
                    label, 15, TextAnchor.MiddleCenter, UiBuilder.TextDim);
                SetAnswerColumnRect(text.rectTransform, option, optionCount, 2f);
            }
        }

        private string GetScaleHeaderLabel(int anchorIndex)
        {
            if (IsSsq())
            {
                string[] labels = { "NIMALO", "BLAGO", "UMJERENO", "JAKO" };
                return anchorIndex >= 0 && anchorIndex < labels.Length ? labels[anchorIndex] : "";
            }

            if (_questionnaire.scaleAnchors != null &&
                anchorIndex >= 0 && anchorIndex < _questionnaire.scaleAnchors.Count)
            {
                string anchor = _questionnaire.scaleAnchors[anchorIndex] ?? "";
                int englishStart = anchor.IndexOf(" (", StringComparison.Ordinal);
                if (englishStart >= 0) anchor = anchor.Substring(0, englishStart);
                return anchor.ToUpperInvariant();
            }

            return "";
        }

        private void BuildRows(int itemsPerPage)
        {
            DestroyChildren(_rowsRoot);
            _visibleRows.Clear();

            int start = _pageIndex * itemsPerPage;
            int count = Math.Min(itemsPerPage, _questionnaire.items.Count - start);
            int optionCount = _questionnaire.scaleMax - _questionnaire.scaleMin + 1;

            for (int rowIndex = 0; rowIndex < count; rowIndex++)
            {
                int itemIndex = start + rowIndex;
                var item = _questionnaire.items[itemIndex];

                var rowBg = UiBuilder.Image(_rowsRoot, "Row_" + rowIndex,
                    rowIndex % 2 == 0
                        ? new Color(0.12f, 0.13f, 0.17f, 0.92f)
                        : new Color(0.14f, 0.15f, 0.19f, 0.92f));
                rowBg.raycastTarget = false;
                SetRowRect(rowBg.rectTransform, rowIndex, count, 3f);

                var bcs = UiBuilder.Text(rowBg.transform, "BcsText", item.text,
                    IsSsq() ? 20 : 19, TextAnchor.MiddleLeft);
                SetRect(bcs.rectTransform, 0f, 0.38f, 0.40f, 1f, 9f, 0f, -5f, -2f);

                var english = UiBuilder.Text(rowBg.transform, "EnglishText",
                    item.textEnglish ?? "", 14, TextAnchor.UpperLeft, UiBuilder.TextDim);
                SetRect(english.rectTransform, 0f, 0f, 0.40f, 0.42f, 9f, 2f, -5f, 0f);

                var rowView = new AnswerRowView
                {
                    ItemId = item.itemId,
                    Buttons = new Button[optionCount]
                };

                for (int option = 0; option < optionCount; option++)
                {
                    int value = _questionnaire.scaleMin + option;
                    int capturedValue = value;
                    var button = CreateButton(rowBg.transform, "Answer_" + value,
                        value.ToString(), 22, AnswerNormal, AnswerHover, AnswerPressed,
                        AnswerSelected, Disabled);
                    SetAnswerColumnRect(button.GetComponent<RectTransform>(), option, optionCount, 4f);
                    button.onClick.AddListener(() => SelectAnswer(rowView, capturedValue));
                    rowView.Buttons[option] = button;
                }

                _visibleRows.Add(rowView);
                UpdateRowSelection(rowView);
            }
        }

        private void SelectAnswer(AnswerRowView row, int value)
        {
            if (row == null || _isSubmitting) return;
            _answers[row.ItemId] = Mathf.Clamp(value,
                _questionnaire.scaleMin, _questionnaire.scaleMax);
            UpdateRowSelection(row);
            UpdatePagedStatus();
        }

        private void UpdateRowSelection(AnswerRowView row)
        {
            int selectedValue;
            bool hasSelection = _answers.TryGetValue(row.ItemId, out selectedValue);
            for (int i = 0; i < row.Buttons.Length; i++)
            {
                int value = _questionnaire.scaleMin + i;
                bool selected = hasSelection && selectedValue == value;
                ApplyAnswerVisual(row.Buttons[i], selected);
                row.Buttons[i].interactable = !_isSubmitting;
            }
        }

        private void ApplyAnswerVisual(Button button, bool selected)
        {
            if (button == null) return;
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = button.GetComponent<Image>();
            button.colors = new ColorBlock
            {
                normalColor = selected ? AnswerSelected : AnswerNormal,
                highlightedColor = selected ? AnswerSelectedHover : AnswerHover,
                pressedColor = AnswerPressed,
                selectedColor = selected ? AnswerSelectedHover : UiBuilder.RowSelectedBg,
                disabledColor = Disabled,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            if (button.targetGraphic != null)
                button.targetGraphic.color = selected ? AnswerSelected : AnswerNormal;
        }

        private void UpdatePagedStatus()
        {
            int itemsPerPage = GetItemsPerPage();
            int pageCount = GetPageCount(itemsPerPage);
            int start = _pageIndex * itemsPerPage;
            int count = Math.Min(itemsPerPage, _questionnaire.items.Count - start);
            int answered = 0;

            for (int i = 0; i < count; i++)
            {
                if (_answers.ContainsKey(_questionnaire.items[start + i].itemId))
                    answered++;
            }

            _pageText.text = "Stranica " + (_pageIndex + 1) + " od " + pageCount;
            _answeredText.text = "Odgovoreno " + answered + " od " + count;

            bool lastPage = _pageIndex >= pageCount - 1;
            UpdateButtonLabel(_nextButton, lastPage ? "Završi" : "Dalje");
            _nextButton.interactable = !_isSubmitting && answered == count;
            _backButton.interactable = !_isSubmitting;

            for (int i = 0; i < _visibleRows.Count; i++)
                UpdateRowSelection(_visibleRows[i]);
        }

        private void GoForwardFromPagedView()
        {
            if (_isSubmitting || !IsCurrentPageComplete()) return;

            int pageCount = GetPageCount(GetItemsPerPage());
            if (_pageIndex < pageCount - 1)
            {
                _pageIndex++;
                _navRow = 0;
                _navColumn = 0;
                RenderPagedQuestionnaire();
                return;
            }

            SubmitCurrentQuestionnaire();
        }

        private void GoBackFromPagedView()
        {
            if (_isSubmitting) return;
            if (_pageIndex > 0)
            {
                _pageIndex--;
                _navRow = 0;
                _navColumn = 0;
                RenderPagedQuestionnaire();
            }
            else
            {
                ShowIntroduction();
            }
        }

        private bool IsCurrentPageComplete()
        {
            int itemsPerPage = GetItemsPerPage();
            int start = _pageIndex * itemsPerPage;
            int count = Math.Min(itemsPerPage, _questionnaire.items.Count - start);
            for (int i = 0; i < count; i++)
            {
                if (!_answers.ContainsKey(_questionnaire.items[start + i].itemId))
                    return false;
            }
            return count > 0;
        }

        private void RenderTlx()
        {
            if (_questionnaire == null || _questionnaire.items.Count == 0) return;

            HideAllViews();
            _showingIntro = false;
            _tlxRoot.SetActive(true);
            SetTitle("NASA-TLX");

            _pageIndex = Mathf.Clamp(_pageIndex, 0, _questionnaire.items.Count - 1);
            var item = _questionnaire.items[_pageIndex];
            int stored;
            _tlxValue = _answers.TryGetValue(item.itemId, out stored) ? stored : 50;
            _tlxValue = SnapTlxValue(_tlxValue);

            _tlxProgressText.text = "Dimenzija " + (_pageIndex + 1) + " od " + _questionnaire.items.Count;
            _tlxInstructionText.text = "Ocijeni ovu dimenziju u koracima od 5.";
            _tlxItemText.text = item.text;
            _tlxEnglishText.text = item.textEnglish ?? "";
            _tlxAnchorsText.text = GetTlxAnchorText();
            UpdateButtonLabel(_tlxConfirmButton,
                _pageIndex == _questionnaire.items.Count - 1 ? "Završi" : "Potvrdi");
            SetTlxControlsInteractable(!_isSubmitting);
            UpdateTlxValueVisuals();
            SelectForFallback(_tlxMinusButton);
        }

        private string GetTlxAnchorText()
        {
            if (_questionnaire.scaleAnchors == null || _questionnaire.scaleAnchors.Count == 0)
                return "0  —  100";
            if (_questionnaire.scaleAnchors.Count == 1)
                return _questionnaire.scaleAnchors[0];
            return "0: " + _questionnaire.scaleAnchors[0] + "     100: " +
                   _questionnaire.scaleAnchors[_questionnaire.scaleAnchors.Count - 1];
        }

        private void AdjustTlxValue(int delta)
        {
            if (_isSubmitting) return;
            SetTlxValue(_tlxValue + delta);
        }

        private void SetTlxValue(int value)
        {
            if (_isSubmitting) return;
            _tlxValue = SnapTlxValue(value);
            UpdateTlxValueVisuals();
        }

        private int SnapTlxValue(int value)
        {
            int clamped = Mathf.Clamp(value, _questionnaire != null ? _questionnaire.scaleMin : 0,
                _questionnaire != null ? _questionnaire.scaleMax : 100);
            return Mathf.RoundToInt(clamped / (float)TlxStep) * TlxStep;
        }

        private void UpdateTlxValueVisuals()
        {
            _tlxValueText.text = _tlxValue.ToString();
            for (int i = 0; i < _tlxQuickButtons.Count; i++)
            {
                int quickValue;
                if (!int.TryParse(GetButtonLabel(_tlxQuickButtons[i]), out quickValue))
                    quickValue = -1;
                ApplyAnswerVisual(_tlxQuickButtons[i], quickValue == _tlxValue);
                _tlxQuickButtons[i].interactable = !_isSubmitting;
            }
        }

        private void ConfirmTlxValue()
        {
            if (_isSubmitting || _questionnaire == null) return;
            var item = _questionnaire.items[_pageIndex];
            _answers[item.itemId] = SnapTlxValue(_tlxValue);

            if (_pageIndex < _questionnaire.items.Count - 1)
            {
                _pageIndex++;
                RenderTlx();
                return;
            }

            SubmitCurrentQuestionnaire();
        }

        private void GoBackFromTlx()
        {
            if (_isSubmitting) return;
            if (_pageIndex > 0)
            {
                _pageIndex--;
                RenderTlx();
            }
            else
            {
                ShowIntroduction();
            }
        }

        private void SetTlxControlsInteractable(bool interactable)
        {
            _tlxMinusButton.interactable = interactable;
            _tlxPlusButton.interactable = interactable;
            _tlxBackButton.interactable = interactable;
            _tlxConfirmButton.interactable = interactable;
            for (int i = 0; i < _tlxQuickButtons.Count; i++)
                _tlxQuickButtons[i].interactable = interactable;
        }

        private void SubmitCurrentQuestionnaire()
        {
            if (_isSubmitting || _flow == null || _questionnaire == null) return;
            if (_answers.Count < _questionnaire.items.Count) return;

            for (int i = 0; i < _questionnaire.items.Count; i++)
            {
                if (!_answers.ContainsKey(_questionnaire.items[i].itemId))
                    return;
            }

            _isSubmitting = true;
            SetAllControlsInteractable(false);

            var submittedQuestionnaire = _questionnaire;
            for (int i = 0; i < submittedQuestionnaire.items.Count; i++)
            {
                // The flow must still point to the same questionnaire while the
                // original-order response sequence is being submitted.
                if (_flow == null || _flow.CurrentQuestionnaire != submittedQuestionnaire)
                    break;
                _flow.SubmitCurrentResponse(_answers[submittedQuestionnaire.items[i].itemId]);
            }

            if (_flow != null && !_flow.IsFinished &&
                _flow.CurrentQuestionnaire != null &&
                _flow.CurrentQuestionnaire != submittedQuestionnaire)
            {
                PrepareCurrentQuestionnaire();
                ShowIntroduction();
            }
        }

        private void SetAllControlsInteractable(bool interactable)
        {
            if (_startButton != null) _startButton.interactable = interactable;
            if (_backButton != null) _backButton.interactable = interactable;
            if (_nextButton != null) _nextButton.interactable = interactable;
            if (_tlxBackButton != null) _tlxBackButton.interactable = interactable;
            if (_tlxConfirmButton != null) _tlxConfirmButton.interactable = interactable;
            if (_tlxMinusButton != null) _tlxMinusButton.interactable = interactable;
            if (_tlxPlusButton != null) _tlxPlusButton.interactable = interactable;
            for (int i = 0; i < _tlxQuickButtons.Count; i++)
                _tlxQuickButtons[i].interactable = interactable;
            for (int row = 0; row < _visibleRows.Count; row++)
                for (int col = 0; col < _visibleRows[row].Buttons.Length; col++)
                    _visibleRows[row].Buttons[col].interactable = interactable;
        }

        private int GetItemsPerPage()
        {
            return IsSsq() ? SsqItemsPerPage : StaiItemsPerPage;
        }

        private int GetPageCount(int itemsPerPage)
        {
            if (_questionnaire == null || itemsPerPage <= 0) return 0;
            return Mathf.CeilToInt(_questionnaire.items.Count / (float)itemsPerPage);
        }

        private bool IsSsq()
        {
            return _questionnaire != null &&
                   _questionnaire.questionnaireId == QuestionnaireCatalog.SsqId;
        }

        private bool IsStai()
        {
            return _questionnaire != null &&
                   _questionnaire.questionnaireId == QuestionnaireCatalog.StaiId;
        }

        private bool IsTlx()
        {
            return _questionnaire != null &&
                   _questionnaire.questionnaireId == QuestionnaireCatalog.TlxId;
        }

        private void HideAllViews()
        {
            if (_introRoot != null) _introRoot.SetActive(false);
            if (_pagedRoot != null) _pagedRoot.SetActive(false);
            if (_tlxRoot != null) _tlxRoot.SetActive(false);
        }

        public override void OnNav(NavEvent nav)
        {
            if (_flow == null || _questionnaire == null || _isSubmitting) return;

            if (_showingIntro)
            {
                if (nav == NavEvent.Confirm) StartCurrentQuestionnaire();
                else if (nav == NavEvent.Back) OnBack?.Invoke();
                return;
            }

            if (IsTlx())
            {
                switch (nav)
                {
                    case NavEvent.Left:
                    case NavEvent.Down:
                        AdjustTlxValue(-TlxStep);
                        break;
                    case NavEvent.Right:
                    case NavEvent.Up:
                        AdjustTlxValue(TlxStep);
                        break;
                    case NavEvent.Confirm:
                        ConfirmTlxValue();
                        break;
                    case NavEvent.Back:
                        GoBackFromTlx();
                        break;
                }
                return;
            }

            int footerRow = _visibleRows.Count;
            switch (nav)
            {
                case NavEvent.Up:
                    _navRow = (_navRow - 1 + footerRow + 1) % (footerRow + 1);
                    FocusPagedFallback();
                    break;
                case NavEvent.Down:
                    _navRow = (_navRow + 1) % (footerRow + 1);
                    FocusPagedFallback();
                    break;
                case NavEvent.Left:
                    if (_navRow < footerRow)
                    {
                        _navColumn = (_navColumn - 1 + _visibleRows[_navRow].Buttons.Length) %
                                     _visibleRows[_navRow].Buttons.Length;
                        FocusPagedFallback();
                    }
                    else
                    {
                        SelectForFallback(_backButton);
                    }
                    break;
                case NavEvent.Right:
                    if (_navRow < footerRow)
                    {
                        _navColumn = (_navColumn + 1) % _visibleRows[_navRow].Buttons.Length;
                        FocusPagedFallback();
                    }
                    else
                    {
                        SelectForFallback(_nextButton);
                    }
                    break;
                case NavEvent.Confirm:
                    if (_navRow < footerRow)
                    {
                        var row = _visibleRows[_navRow];
                        SelectAnswer(row, _questionnaire.scaleMin + _navColumn);
                    }
                    else if (_nextButton.interactable)
                    {
                        GoForwardFromPagedView();
                    }
                    break;
                case NavEvent.Back:
                    GoBackFromPagedView();
                    break;
            }
        }

        private void FocusPagedFallback()
        {
            if (_visibleRows.Count == 0) return;
            int footerRow = _visibleRows.Count;
            _navRow = Mathf.Clamp(_navRow, 0, footerRow);
            if (_navRow < footerRow)
            {
                _navColumn = Mathf.Clamp(_navColumn, 0,
                    _visibleRows[_navRow].Buttons.Length - 1);
                SelectForFallback(_visibleRows[_navRow].Buttons[_navColumn]);
            }
            else
            {
                SelectForFallback(_nextButton.interactable ? _nextButton : _backButton);
            }
        }

        private static void SelectForFallback(Button button)
        {
            if (button == null || !button.interactable) return;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        private static Button CreateButton(Transform parent, string name, string label,
            int fontSize, Color normal, Color highlighted, Color pressed,
            Color selected, Color disabled)
        {
            var image = UiBuilder.Image(parent, name, normal);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.colors = new ColorBlock
            {
                normalColor = normal,
                highlightedColor = highlighted,
                pressedColor = pressed,
                selectedColor = selected,
                disabledColor = disabled,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            var text = UiBuilder.Text(image.transform, "Label", label, fontSize,
                TextAnchor.MiddleCenter);
            UiBuilder.Stretch(text.rectTransform, 5f, 3f, 5f, 3f);
            text.raycastTarget = false;
            return button;
        }

        private static void UpdateButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.transform.Find("Label")?.GetComponent<Text>();
            if (text != null) text.text = label;
        }

        private static string GetButtonLabel(Button button)
        {
            if (button == null) return "";
            var text = button.transform.Find("Label")?.GetComponent<Text>();
            return text != null ? text.text : "";
        }

        private static void SetRect(RectTransform rect, float minX, float minY,
            float maxX, float maxY, float left, float bottom, float right, float top)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        private static void SetAnswerColumnRect(RectTransform rect, int column,
            int columnCount, float horizontalInset)
        {
            const float answerStart = 0.42f;
            const float answerEnd = 1f;
            float width = (answerEnd - answerStart) / columnCount;
            float minX = answerStart + column * width;
            float maxX = minX + width;
            SetRect(rect, minX, 0f, maxX, 1f,
                horizontalInset, 4f, -horizontalInset, -4f);
        }

        private static void SetRowRect(RectTransform rect, int row, int rowCount, float gap)
        {
            float height = 1f / rowCount;
            float maxY = 1f - row * height;
            float minY = maxY - height;
            SetRect(rect, 0f, minY, 1f, maxY, 0f, gap, 0f, -gap);
        }

        private static void DestroyChildren(Transform root)
        {
            if (root == null) return;
            var children = new List<GameObject>(root.childCount);
            foreach (Transform child in root) children.Add(child.gameObject);
            for (int i = 0; i < children.Count; i++)
            {
                children[i].SetActive(false);
                if (Application.isPlaying) Destroy(children[i]);
                else DestroyImmediate(children[i]);
            }
        }
    }
}
