using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.UI
{
    public interface IUiPanel
    {
        void OnNav(NavEvent nav);
        GameObject PanelRoot { get; }
    }

    /// <summary>
    /// Base for list-selection panels: title, info line, vertical option list
    /// navigated with up/down + confirm. Options are rebuilt via SetOptions.
    /// </summary>
    public abstract class MenuPanelBase : MonoBehaviour, IUiPanel
    {
        public GameObject PanelRoot { get; private set; }

        protected Text TitleText;
        protected Text BodyText;
        protected Text InfoText;
        protected RectTransform ListRoot;

        private readonly List<(Text label, Image bg, Action onSelect)> _rows =
            new List<(Text, Image, Action)>();
        protected int SelectedIndex { get; private set; }

        public Action OnBack;

        public virtual void Build(Transform canvasRoot, string panelName)
        {
            PanelRoot = new GameObject(panelName, typeof(RectTransform));
            PanelRoot.transform.SetParent(canvasRoot, false);
            UiBuilder.Stretch((RectTransform)PanelRoot.transform);

            var bg = UiBuilder.Image(PanelRoot.transform, "BG", UiBuilder.PanelBg);
            UiBuilder.Stretch(bg.rectTransform);

            var header = UiBuilder.Image(PanelRoot.transform, "Header", UiBuilder.HeaderBg);
            UiBuilder.TopBar(header.rectTransform, 56);
            TitleText = UiBuilder.Text(header.transform, "Title", "", 28, TextAnchor.MiddleCenter);
            UiBuilder.Stretch(TitleText.rectTransform, 10, 0, 10, 0);

            // Content/footer separation (spec §23): the body text lives in a
            // clipped container whose bottom edge is recomputed from the option
            // count, and the option rows are a bottom-anchored footer. Long
            // tutorial text can therefore never overlap the action buttons —
            // it is clipped by the mask instead.
            _bodyContainer = new GameObject("BodyContainer", typeof(RectTransform))
                .GetComponent<RectTransform>();
            _bodyContainer.SetParent(PanelRoot.transform, false);
            UiBuilder.Stretch(_bodyContainer, 24, FooterBaseHeight, 24, 66);
            _bodyContainer.gameObject.AddComponent<RectMask2D>();

            BodyText = UiBuilder.Text(_bodyContainer, "Body", "", 20, TextAnchor.UpperLeft);
            UiBuilder.Stretch(BodyText.rectTransform, 0, 0, 0, 0);
            BodyText.verticalOverflow = VerticalWrapMode.Overflow; // clipped by the mask

            var list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(PanelRoot.transform, false);
            ListRoot = (RectTransform)list.transform;
            ListRoot.anchorMin = new Vector2(0, 0);
            ListRoot.anchorMax = new Vector2(1, 0);
            ListRoot.pivot = new Vector2(0.5f, 0);
            ListRoot.sizeDelta = new Vector2(-48, 0);
            ListRoot.anchoredPosition = new Vector2(0, FooterBaseHeight);

            InfoText = UiBuilder.Text(PanelRoot.transform, "Info", "", 17,
                TextAnchor.MiddleCenter, UiBuilder.TextDim);
            UiBuilder.BottomBar(InfoText.rectTransform, 46, 4);

            PanelRoot.SetActive(false);
        }

        private const float FooterBaseHeight = 52f;   // info line + margin
        private const float RowHeight = 44f;
        private const float RowGap = 6f;
        private RectTransform _bodyContainer;

        public void SetTitle(string t) => TitleText.text = t;
        public void SetBody(string b) => BodyText.text = b;
        public void SetInfo(string i) => InfoText.text = i;

        protected void SetOptions(List<(string label, Action onSelect)> options, int keepIndex = 0)
        {
            // Snapshot first: DestroyImmediate mutates the child list while
            // iterating. Edit-mode (tests/installer) must not call Destroy().
            var oldRows = new List<GameObject>(ListRoot.childCount);
            foreach (Transform child in ListRoot) oldRows.Add(child.gameObject);
            foreach (var row in oldRows)
            {
                if (Application.isPlaying) Destroy(row);
                else DestroyImmediate(row);
            }
            _rows.Clear();

            const float rowH = RowHeight, gap = RowGap;
            for (int i = 0; i < options.Count; i++)
            {
                var bgImg = UiBuilder.Image(ListRoot, "Row" + i, UiBuilder.RowBg);
                bgImg.raycastTarget = true;
                var rt = bgImg.rectTransform;
                // Footer stacking: rows anchor to the BOTTOM of the panel and
                // grow upward, keeping the first option visually on top.
                rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(0.5f, 0);
                rt.sizeDelta = new Vector2(0, rowH);
                rt.anchoredPosition = new Vector2(0, (options.Count - 1 - i) * (rowH + gap));

                var label = UiBuilder.Text(bgImg.transform, "Label", options[i].label, 20, TextAnchor.MiddleLeft);
                UiBuilder.Stretch(label.rectTransform, 14, 0, 14, 0);
                Action callback = options[i].onSelect;
                var button = bgImg.gameObject.AddComponent<Button>();
                button.targetGraphic = bgImg;
                button.transition = Selectable.Transition.ColorTint;
                button.colors = new ColorBlock
                {
                    normalColor = UiBuilder.RowBg,
                    highlightedColor = new Color(0.28f, 0.48f, 0.72f),
                    pressedColor = new Color(0.18f, 0.72f, 0.48f),
                    selectedColor = UiBuilder.RowSelectedBg,
                    disabledColor = new Color(0.10f, 0.10f, 0.12f, 0.55f),
                    colorMultiplier = 1f,
                    fadeDuration = 0.08f
                };
                button.onClick.AddListener(() => callback?.Invoke());
                _rows.Add((label, bgImg, callback));
            }

            // Reserve exactly enough footer space for the rows and clip the body
            // above it — the "Nastavi" button can never cover tutorial text.
            float footerHeight = options.Count > 0
                ? options.Count * (rowH + gap) - gap : 0f;
            ListRoot.sizeDelta = new Vector2(ListRoot.sizeDelta.x, footerHeight);
            if (_bodyContainer != null)
                _bodyContainer.offsetMin = new Vector2(24f,
                    FooterBaseHeight + footerHeight + (options.Count > 0 ? 12f : 0f));

            SelectedIndex = Mathf.Clamp(keepIndex, 0, Math.Max(0, _rows.Count - 1));
            UpdateHighlight();
        }

        /// <summary>Exposed for layout tests: bottom edge (px) of the clipped body area.</summary>
        public float BodyBottomOffset => _bodyContainer != null ? _bodyContainer.offsetMin.y : -1f;
        /// <summary>Exposed for layout tests: current footer (options) height in px.</summary>
        public float FooterHeight => ListRoot != null ? ListRoot.sizeDelta.y : -1f;

        private void UpdateHighlight()
        {
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].bg.color = i == SelectedIndex ? UiBuilder.RowSelectedBg : UiBuilder.RowBg;
        }

        public virtual void OnNav(NavEvent nav)
        {
            switch (nav)
            {
                case NavEvent.Up:
                    if (_rows.Count > 0) { SelectedIndex = (SelectedIndex - 1 + _rows.Count) % _rows.Count; UpdateHighlight(); }
                    break;
                case NavEvent.Down:
                    if (_rows.Count > 0) { SelectedIndex = (SelectedIndex + 1) % _rows.Count; UpdateHighlight(); }
                    break;
                case NavEvent.Confirm:
                    if (_rows.Count > 0) _rows[SelectedIndex].onSelect?.Invoke();
                    break;
                case NavEvent.Back:
                    OnBack?.Invoke();
                    break;
            }
        }

        public virtual void Show() => PanelRoot.SetActive(true);
        public virtual void Hide() => PanelRoot.SetActive(false);
    }

    /// <summary>
    /// General-purpose information panel: title + body + configurable options.
    /// Used for PreSession, Tutorial pages, Ready, AdaptationReview,
    /// PostSessionSummary and Error states.
    /// </summary>
    public sealed class InfoPanel : MenuPanelBase
    {
        public void Configure(string title, string body,
            List<(string label, Action onSelect)> options, string info = "")
        {
            SetTitle(title);
            SetBody(body);
            SetInfo(info);
            SetOptions(options);
        }
    }
}
