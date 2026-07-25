using System;
using System.Collections.Generic;

namespace StressTraining.Data
{
    public enum AdaptationDirective
    {
        NoDecisionInvalidSession = 0,
        Increase = 1,
        Hold = 2,
        Decrease = 3,
        RepeatForStability = 4
    }

    /// <summary>Decision for one local task difficulty.</summary>
    [Serializable]
    public sealed class TaskAdaptationDecisionData
    {
        public TaskType taskType;
        public AdaptationDirective directive;
        public int previousLevel;
        public int newLevel;
        public List<string> reasonCodes = new List<string>();
    }

    /// <summary>Decision for the global pressure level.</summary>
    [Serializable]
    public sealed class PressureAdaptationDecisionData
    {
        public AdaptationDirective directive;
        public int previousLevel;
        public int newLevel;
        public List<string> reasonCodes = new List<string>();
    }

    /// <summary>
    /// Full scheduler output for one session. Stored in the session summary and
    /// shown to the user through AdaptationExplanationBuilder's human-readable text.
    /// </summary>
    [Serializable]
    public sealed class AdaptationDecisionData
    {
        public int schemaVersion = 1;
        public string decidedAtUtcIso;
        public string sessionId;

        public TaskAdaptationDecisionData nBack = new TaskAdaptationDecisionData();
        public TaskAdaptationDecisionData goNoGo = new TaskAdaptationDecisionData();
        public TaskAdaptationDecisionData flanker = new TaskAdaptationDecisionData();
        public TaskAdaptationDecisionData corsi = new TaskAdaptationDecisionData();   // v2
        public PressureAdaptationDecisionData pressure = new PressureAdaptationDecisionData();
        public string schedulerVersion = "sched-v2";                                  // v2

        public List<string> firedRules = new List<string>();   // machine-readable rule ids
        public string humanExplanation = "";                   // BCS explanation for the user
        public string inputSnapshotJson = "";                  // full AdaptationInput for audit
    }
}
