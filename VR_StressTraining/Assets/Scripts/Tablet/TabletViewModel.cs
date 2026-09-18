using UnityEngine;

namespace StressTraining.Tablet
{
    public enum TabletScreenState
    {
        Idle = 0,
        Instruction = 1,
        Stimulus = 2,
        Countdown = 3,
        Feedback = 4,
        BlockTransition = 5,
        Paused = 6
    }

    public enum StimulusVisualKind
    {
        None = 0,
        Symbol = 1,
        Color = 2,
        Position = 3,
        GoNoGo = 4,
        Arrows = 5,
        CorsiMap = 6
    }

    public struct StimulusVisual
    {
        public StimulusVisualKind Kind;
        public string Text;
        public Color Color;
        public int PositionIndex;
        public bool IsNoGo;
        public int SimilarityTier;
    }

    /// <summary>Pure render state; participant-facing progress contains no seeds.</summary>
    public sealed class TabletViewModel
    {
        public TabletScreenState Screen = TabletScreenState.Idle;
        public string Title = "";
        public string Body = "";

        public int CurrentBlock;
        public int TotalBlocks;
        public int CurrentRound;
        public int TotalRounds;
        public string TaskName = "";
        public int DifficultyLevel;
        public string HeaderTimer = "";

        // Compatibility field used by demo/tutorial code only.
        public string HeaderBlockInfo = "";

        public Data.HrZone HrZone = Data.HrZone.SignalLost;
        public Data.HrSourceType HrSourceType = Data.HrSourceType.None;
        public bool ShowHrZone;
        public bool HrRecordingActive;
        public int CurrentTrial;
        public int TotalTrials;
        public Data.TaskType ActiveTask = Data.TaskType.None;
        public string ActiveControlsText = "";
        public StimulusVisual Stimulus;
        public int CountdownValue;
        public string FeedbackText = "";
        public bool FeedbackIsCorrect;

        public static StimulusVisual Decode(string encoded)
        {
            var v = new StimulusVisual { Kind = StimulusVisualKind.None, Color = Color.white };
            if (string.IsNullOrEmpty(encoded)) return v;

            if (encoded.StartsWith("sym:"))
            {
                v.Kind = StimulusVisualKind.Symbol;
                v.Text = encoded.Substring(4);
                v.Color = new Color(0.92f, 0.92f, 0.95f);
            }
            else if (encoded.StartsWith("col:"))
            {
                v.Kind = StimulusVisualKind.Color;
                v.Color = NamedColor(encoded.Substring(4));
            }
            else if (encoded.StartsWith("pos:"))
            {
                v.Kind = StimulusVisualKind.Position;
                int.TryParse(encoded.Substring(4), out v.PositionIndex);
                v.Color = new Color(0.3f, 0.7f, 1f);
            }
            else if (encoded.StartsWith("go:T") || encoded.StartsWith("nogo:T"))
            {
                v.Kind = StimulusVisualKind.GoNoGo;
                v.IsNoGo = encoded.StartsWith("nogo:");
                int.TryParse(encoded.Substring(encoded.IndexOf('T') + 1), out v.SimilarityTier);
                v.Color = Color.white;
                v.Text = v.IsNoGo ? "NO GO" : "GO";
            }
            else if (encoded.StartsWith("corsi:"))
            {
                v.Kind = StimulusVisualKind.CorsiMap;
                v.Color = new Color(0.16f, 0.16f, 0.2f);
            }
            else if (encoded.StartsWith("arr:"))
            {
                v.Kind = StimulusVisualKind.Arrows;
                v.Text = encoded.Substring(4).Replace("<", "◄").Replace(">", "►").Replace("-", "▬");
                v.Color = new Color(0.92f, 0.92f, 0.95f);
            }
            return v;
        }

        public static Color NamedColor(string name)
        {
            switch (name)
            {
                case "RED": return new Color(0.85f, 0.2f, 0.2f);
                case "BLUE": return new Color(0.25f, 0.45f, 0.9f);
                case "GREEN": return new Color(0.2f, 0.75f, 0.3f);
                case "YELLOW": return new Color(0.95f, 0.85f, 0.2f);
                case "PURPLE": return new Color(0.6f, 0.3f, 0.8f);
                case "ORANGE": return new Color(0.95f, 0.55f, 0.15f);
                case "CYAN": return new Color(0.2f, 0.8f, 0.85f);
                default: return new Color(0.9f, 0.9f, 0.9f);
            }
        }

        [System.Obsolete("Go/No-Go uses black GO / NO GO text on white.")]
        public static Color GoNoGoColor(bool isNoGo, int tier) => Color.white;
    }
}
