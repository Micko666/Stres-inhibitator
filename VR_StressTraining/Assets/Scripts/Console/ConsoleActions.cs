using System;
using StressTraining.Data;

namespace StressTraining.Console
{
    public enum ConsoleControlType
    {
        Button = 0,
        Toggle = 1,
        Lever = 2,
        Knob = 3,
        IndicatorLight = 4
    }

    /// <summary>Maps a physical control (by stable controlId) to a semantic action (spec §10).</summary>
    [Serializable]
    public sealed class ConsoleBindingDefinition
    {
        public string controlId;
        public SemanticAction action;

        public ConsoleBindingDefinition() { }
        public ConsoleBindingDefinition(string id, SemanticAction a)
        {
            controlId = id;
            action = a;
        }
    }

    /// <summary>One physical interaction event, timestamped for the event log.</summary>
    public struct ConsoleControlEvent
    {
        public string ControlId;
        public ConsoleControlType ControlType;
        public float Value;                 // 1 = pressed, toggle/lever state, knob step value
        public string TimestampUtcIso;
        public double MonotonicSeconds;     // Time.realtimeSinceStartupAsDouble
        public bool IsDistractor;
    }
}
