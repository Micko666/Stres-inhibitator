using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.Questionnaires
{
    /// <summary>
    /// Scores completed questionnaires using the published formulas:
    /// - SSQ: subscale raw sums × published weights (N 9.54, O 7.58, D 13.92);
    ///   Total = 3.74 × (rawN + rawO + rawD). (Kennedy et al., 1993)
    /// - Raw NASA-TLX: total = mean of the six 0–100 dimensions; each dimension
    ///   is also stored individually because the scheduler consumes them.
    /// - STAI-6: reverse-score calm/relaxed/content (5 − v on the 1..4 scale),
    ///   prorated total = sum × 20 / 6 (range 20..80). (Marteau &amp; Bekker, 1992)
    /// Pure static — fully unit-testable.
    /// </summary>
    public static class QuestionnaireScoringService
    {
        public const float SsqWeightN = 9.54f;
        public const float SsqWeightO = 7.58f;
        public const float SsqWeightD = 13.92f;
        public const float SsqWeightTotal = 3.74f;

        public static QuestionnaireResultData Score(
            QuestionnaireDefinitionData definition,
            List<QuestionnaireResponseData> responses,
            QuestionnairePhase phase,
            bool isFixture = false)
        {
            var result = new QuestionnaireResultData
            {
                questionnaireId = definition.questionnaireId,
                phase = phase,
                administeredAtUtcIso = UtcTime.NowIso(),
                isDeveloperFixture = isFixture,
                responses = responses
            };

            switch (definition.questionnaireId)
            {
                case QuestionnaireCatalog.SsqId: ScoreSsq(definition, result); break;
                case QuestionnaireCatalog.TlxId: ScoreTlx(result); break;
                case QuestionnaireCatalog.StaiId: ScoreStai(definition, result); break;
            }
            return result;
        }

        private static int Value(QuestionnaireResultData r, string itemId)
        {
            foreach (var resp in r.responses)
                if (resp.itemId == itemId) return resp.value;
            return -1;
        }

        private static void ScoreSsq(QuestionnaireDefinitionData def, QuestionnaireResultData r)
        {
            float rawN = 0, rawO = 0, rawD = 0;
            foreach (var item in def.items)
            {
                int v = Value(r, item.itemId);
                if (v < 0) continue;
                if (item.subscale.Contains("N")) rawN += v;
                if (item.subscale.Contains("O")) rawO += v;
                if (item.subscale.Contains("D")) rawD += v;
            }
            r.subscaleNames.Add("Nausea");
            r.subscaleScores.Add(rawN * SsqWeightN);
            r.subscaleNames.Add("Oculomotor");
            r.subscaleScores.Add(rawO * SsqWeightO);
            r.subscaleNames.Add("Disorientation");
            r.subscaleScores.Add(rawD * SsqWeightD);
            r.totalScore = (rawN + rawO + rawD) * SsqWeightTotal;
        }

        private static void ScoreTlx(QuestionnaireResultData r)
        {
            float sum = 0;
            int n = 0;
            foreach (var resp in r.responses)
            {
                r.subscaleNames.Add(resp.itemId);
                r.subscaleScores.Add(resp.value);
                sum += resp.value;
                n++;
            }
            r.totalScore = n > 0 ? sum / n : 0f;
        }

        private static void ScoreStai(QuestionnaireDefinitionData def, QuestionnaireResultData r)
        {
            float sum = 0;
            int n = 0;
            foreach (var item in def.items)
            {
                int v = Value(r, item.itemId);
                if (v < 0) continue;
                int scored = item.reverseScored ? (def.scaleMin + def.scaleMax) - v : v; // 5 − v on 1..4
                r.subscaleNames.Add(item.itemId);
                r.subscaleScores.Add(scored);
                sum += scored;
                n++;
            }
            r.totalScore = n > 0 ? sum * 20f / 6f : 0f;
        }

        /// <summary>Convenience: read one TLX dimension (0..100) from a scored result; -1 when absent.</summary>
        public static float TlxDimension(QuestionnaireResultData tlx, string itemId)
        {
            if (tlx == null) return -1f;
            for (int i = 0; i < tlx.subscaleNames.Count; i++)
                if (tlx.subscaleNames[i] == itemId) return tlx.subscaleScores[i];
            return -1f;
        }
    }
}
