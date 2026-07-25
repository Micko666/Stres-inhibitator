using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.UI
{
    public sealed class ProgressPanel : MenuPanelBase
    {
        private Image[] _segments;
        private const int SegmentCount = 12;

        public override void Build(Transform canvasRoot, string panelName)
        {
            base.Build(canvasRoot, panelName);
            var row = new GameObject("Segments", typeof(RectTransform));
            row.transform.SetParent(PanelRoot.transform, false);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(520, 26);
            rt.anchoredPosition = new Vector2(0, 120);

            _segments = new Image[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                var segment = UiBuilder.Image(row.transform, "S" + i, new Color(0.22f, 0.23f, 0.28f));
                var segmentRt = segment.rectTransform;
                segmentRt.anchorMin = segmentRt.anchorMax = new Vector2(0, 0.5f);
                segmentRt.pivot = new Vector2(0, 0.5f);
                segmentRt.sizeDelta = new Vector2(38, 18);
                segmentRt.anchoredPosition = new Vector2(i * 44, 0);
                _segments[i] = segment;
            }
        }

        public void Configure(string title, string body, string info = "")
        {
            SetTitle(title);
            SetBody(body);
            SetInfo(info);
            SetOptions(new List<(string, Action)>());
            SetProgress(0);
        }

        public void SetProgress(float value)
        {
            int lit = Mathf.RoundToInt(Mathf.Clamp01(value) * SegmentCount);
            for (int i = 0; i < SegmentCount; i++)
                _segments[i].color = i < lit ? UiBuilder.Accent : new Color(0.22f, 0.23f, 0.28f);
        }
    }
}
