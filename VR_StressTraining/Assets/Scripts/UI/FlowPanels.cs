using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.UI
{
    /// <summary>
    /// Coping/breathing panel (spec §8). A circle grows on inhale and shrinks on
    /// exhale following BreathingGuidanceConfig (PROJECT_HEURISTIC values, never
    /// presented as a medical protocol). Self-instruction lines rotate below.
    /// Continue unlocks after the configured duration.
    /// </summary>
    public sealed class BreathingPanel : MenuPanelBase
    {
        public event Action Continued;

        private BreathingGuidanceConfig _cfg;
        private Image _circle;
        private Text _phaseText, _selfInstructionText;
        private float _elapsed, _cycleT, _totalDuration;
        private bool _running, _canContinue;
        private bool _flowDriven;   // merged reference phase: the FLOW ends the phase
        private int _selfInstructionIdx;
        private float _nextSelfInstructionAt;

        private static readonly string[] SelfInstructions =
        {
            "„Nastavi redom.“",
            "„Provjeri pravilo.“",
            "„Ne reaguj impulsivno.“"
        };

        public override void Build(Transform canvasRoot, string panelName)
        {
            base.Build(canvasRoot, panelName);
            SetTitle("Priprema — mirno disanje");

            _circle = UiBuilder.Image(PanelRoot.transform, "Circle", new Color(0.30f, 0.62f, 0.92f, 0.85f));
            var crt = _circle.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.45f);
            crt.sizeDelta = new Vector2(120, 120);

            _phaseText = UiBuilder.Text(PanelRoot.transform, "Phase", "", 30, TextAnchor.MiddleCenter);
            var prt = _phaseText.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.45f);
            prt.sizeDelta = new Vector2(400, 50);

            _selfInstructionText = UiBuilder.Text(PanelRoot.transform, "SelfInstruction", "", 22,
                TextAnchor.MiddleCenter, UiBuilder.TextDim);
            var srt = _selfInstructionText.rectTransform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0);
            srt.pivot = new Vector2(0.5f, 0);
            srt.sizeDelta = new Vector2(0, 40);
            srt.anchoredPosition = new Vector2(0, 110);
        }

        public void Begin(BreathingGuidanceConfig cfg, bool firstSessionOfCycle)
        {
            _cfg = cfg;
            _elapsed = 0;
            _cycleT = 0;
            _running = true;
            _canContinue = false;
            _flowDriven = false;
            _selfInstructionIdx = 0;
            _nextSelfInstructionAt = 8f;
            _totalDuration = firstSessionOfCycle
                ? cfg.totalDurationSecondsFirstSession : cfg.totalDurationSecondsReminder;
            float total = _totalDuration;
            SetBody(firstSessionOfCycle
                ? "Fiziološka pobuđenost (ubrzan puls, napetost) je normalna reakcija na pritisak. " +
                  "Mirno, neforsirano disanje i pažnja usmjerena na SLJEDEĆI korak pomažu da ostaneš efikasan/na.\n" +
                  "Prati krug: širi se dok udišeš, skuplja dok izdišeš."
                : "Kratki podsjetnik: mirno disanje, pažnja na sljedeći korak.");
            SetInfo($"Trajanje: {total:0}s");
            SetOptions(new List<(string, Action)>()); // continue appears when unlocked
        }

        /// <summary>
        /// MERGED breathing + reference HR measurement (2026-07-14). The breathing
        /// visuals run for exactly <paramref name="durationSeconds"/> — the existing
        /// baseline duration — and the PHASE IS ENDED BY THE FLOW, not by this panel.
        /// No Continue button is ever shown here, so there is never a second one.
        /// </summary>
        public void BeginReference(BreathingGuidanceConfig cfg, float durationSeconds,
            bool firstSessionOfCycle)
        {
            Begin(cfg, firstSessionOfCycle);
            _flowDriven = true;
            _canContinue = true;             // suppress the panel's own Continue for good
            _totalDuration = Mathf.Max(1f, durationSeconds);
            SetTitle("Priprema 1/2 — Vođeno disanje i mjerenje pulsa");
            SetBody(firstSessionOfCycle
                ? "Prati krug: širi se dok udišeš, skuplja dok izdišeš.\n" +
                  "Istovremeno mjerimo tvoj referentni puls — samo uzorci iz ove faze ulaze u referentnu vrijednost."
                : "Prati ritam disanja. Istovremeno mjerimo referentni puls za ovu sesiju.");
            SetOptions(new List<(string, Action)>());   // flow advances automatically
        }

        /// <summary>Live status line during the merged phase (driven by the flow each tick).</summary>
        public void SetReferenceStatus(float progress01, double remainingSeconds,
            bool hrFresh, int bpm)
        {
            int pct = Mathf.Clamp(Mathf.RoundToInt(progress01 * 100f), 0, 100);
            string hr = hrFresh && bpm > 0
                ? $"Puls: {bpm} bpm"
                : "Puls: signal trenutno nedostaje (mjerenje se ne prekida)";
            SetInfo($"Napredak: {pct}% · Preostalo: {Math.Max(0, remainingSeconds):0} s · {hr}");
        }

        private void Update()
        {
            if (!_running || _cfg == null) return;
            float dt = Time.unscaledDeltaTime;
            _elapsed += dt;
            _cycleT += dt;

            float inhale = _cfg.inhaleSeconds, holdIn = _cfg.holdAfterInhaleSeconds,
                  exhale = _cfg.exhaleSeconds, holdOut = _cfg.holdAfterExhaleSeconds;
            float cycle = inhale + holdIn + exhale + holdOut;
            if (_cycleT >= cycle) _cycleT -= cycle;

            float scale;
            string phase;
            if (_cycleT < inhale) { scale = Mathf.Lerp(0.6f, 1.5f, _cycleT / inhale); phase = "Udahni…"; }
            else if (_cycleT < inhale + holdIn) { scale = 1.5f; phase = "Zadrži…"; }
            else if (_cycleT < inhale + holdIn + exhale)
            { scale = Mathf.Lerp(1.5f, 0.6f, (_cycleT - inhale - holdIn) / exhale); phase = "Izdahni…"; }
            else { scale = 0.6f; phase = "…"; }

            _circle.rectTransform.localScale = Vector3.one * scale;
            _phaseText.text = phase;

            if (_elapsed >= _nextSelfInstructionAt)
            {
                _selfInstructionText.text = SelfInstructions[_selfInstructionIdx % SelfInstructions.Length];
                _selfInstructionIdx++;
                _nextSelfInstructionAt += 10f;
            }

            if (!_flowDriven && !_canContinue && _elapsed >= _totalDuration)
            {
                _canContinue = true;
                SetOptions(new List<(string, Action)> { ("Nastavi", () => { _running = false; Continued?.Invoke(); }) });
                SetInfo("A = nastavi");
            }
        }
    }

    /// <summary>
    /// Quiet measurement panel for Baseline and Recovery (spec §18/§7): no raw
    /// BPM, no changing UI except a discreet segmented progress indicator.
    /// </summary>
    internal sealed class DeferredProgressPanel : MenuPanelBase
    {
        private Image[] _segments;
        private const int SegmentCount = 12;

        public override void Build(Transform canvasRoot, string panelName)
        {
            base.Build(canvasRoot, panelName);
            var row = new GameObject("Segments", typeof(RectTransform));
            row.transform.SetParent(PanelRoot.transform, false);
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = new Vector2(0.5f, 0); rrt.anchorMax = new Vector2(0.5f, 0);
            rrt.pivot = new Vector2(0.5f, 0);
            rrt.sizeDelta = new Vector2(520, 26);
            rrt.anchoredPosition = new Vector2(0, 120);

            _segments = new Image[SegmentCount];
            for (int i = 0; i < SegmentCount; i++)
            {
                var seg = UiBuilder.Image(row.transform, "S" + i, new Color(0.22f, 0.23f, 0.28f));
                var srt = seg.rectTransform;
                srt.anchorMin = new Vector2(0, 0.5f); srt.anchorMax = new Vector2(0, 0.5f);
                srt.pivot = new Vector2(0, 0.5f);
                srt.sizeDelta = new Vector2(38, 18);
                srt.anchoredPosition = new Vector2(i * 44, 0);
                _segments[i] = seg;
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

        public void SetProgress(float t01)
        {
            int lit = Mathf.RoundToInt(Mathf.Clamp01(t01) * SegmentCount);
            for (int i = 0; i < SegmentCount; i++)
                _segments[i].color = i < lit ? UiBuilder.Accent : new Color(0.22f, 0.23f, 0.28f);
        }
    }

    /// <summary>
    /// Pause menu (spec §16): exactly two primary options — Continue Session and
    /// End Session; End opens the reason list. Reasons are user-selected and
    /// stored separately from system-detected causes.
    /// </summary>
    internal sealed class DeferredPauseMenuPanel : MenuPanelBase
    {
        public event Action ContinueRequested;
        public event Action<UserTerminationReason> EndConfirmed;

        private static readonly (string label, UserTerminationReason reason)[] Reasons =
        {
            ("Želim da prekinem", UserTerminationReason.UserRequested),
            ("Nelagodnost", UserTerminationReason.UserDiscomfort),
            ("Simptomi mučnine", UserTerminationReason.SimulatorSickness),
            ("Zadatak nije jasan", UserTerminationReason.TaskUnclear),
            ("Tehnički problem", UserTerminationReason.TechnicalProblem),
            ("Drugo", UserTerminationReason.Other),
        };

        public void ShowMain()
        {
            SetTitle("Pauza");
            SetBody("Sesija je zaustavljena. Vrijeme pauze se mjeri odvojeno i ne ulazi u rezultate.");
            SetInfo("A = potvrdi izbor");
            SetOptions(new List<(string, Action)>
            {
                ("▶  Nastavi sesiju (Continue)", () => ContinueRequested?.Invoke()),
                ("■  Završi sesiju (End Session)", ShowReasons)
            });
        }

        private void ShowReasons()
        {
            SetTitle("Razlog završetka");
            SetBody("Izaberi razlog prekida — čuva se uz podatke sesije.");
            var options = new List<(string, Action)>();
            foreach (var (label, reason) in Reasons)
            {
                var r = reason;
                options.Add((label, () => EndConfirmed?.Invoke(r)));
            }
            options.Add(("←  Nazad", ShowMain));
            SetOptions(options);
        }
    }
}
