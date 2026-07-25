using System;
using System.Collections.Generic;

namespace StressTraining.Data
{
    /// <summary>
    /// Questionnaire definition (SSQ, Raw NASA-TLX, STAI-6). Definitions are built
    /// in code by QuestionnaireCatalog; instances of this class are data only.
    ///
    /// IMPORTANT: item texts marked needsValidatedText=true are placeholders /
    /// working translations and MUST NOT be presented as validated instruments.
    /// textSource documents where the wording came from.
    /// </summary>
    [Serializable]
    public sealed class QuestionnaireDefinitionData
    {
        public string questionnaireId;          // "SSQ", "NASA_TLX_RAW", "STAI6"
        public string displayName;
        public string instructions;
        public string textSource;               // provenance note
        public bool needsValidatedText;         // NEEDS_VALIDATED_ITEM_TEXT flag
        public int scaleMin;
        public int scaleMax;
        public List<string> scaleAnchors = new List<string>();  // labels for min..max ends
        public List<QuestionnaireItemDefinitionData> items = new List<QuestionnaireItemDefinitionData>();
    }

    [Serializable]
    public sealed class QuestionnaireItemDefinitionData
    {
        public string itemId;
        public string text;                     // presented text (BCS where available)
        public string textEnglish;              // canonical English wording
        public bool reverseScored;
        public string subscale = "";            // e.g. SSQ: "N","O","D" (comma-separated when multiple)
        public bool needsValidatedText;
    }

    [Serializable]
    public sealed class QuestionnaireResponseData
    {
        public string itemId;
        public int value;
        public string answeredAtUtcIso;
    }

    /// <summary>Scored result stored in questionnaires.json and in the session summary.</summary>
    [Serializable]
    public sealed class QuestionnaireResultData
    {
        public int schemaVersion = 1;
        public string questionnaireId;
        public QuestionnairePhase phase;
        public string administeredAtUtcIso;
        public bool isDeveloperFixture;         // true when filled by dev fixtures, not a person

        public List<QuestionnaireResponseData> responses = new List<QuestionnaireResponseData>();

        public float totalScore;
        // Parallel lists (JsonUtility cannot serialize dictionaries)
        public List<string> subscaleNames = new List<string>();
        public List<float> subscaleScores = new List<float>();
    }

    [Serializable]
    public sealed class SessionQuestionnairesFile
    {
        public int schemaVersion = 1;
        public string sessionId;
        public List<QuestionnaireResultData> results = new List<QuestionnaireResultData>();
    }
}
