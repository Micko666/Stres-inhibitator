using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Questionnaires
{
    /// <summary>
    /// Builds the three instruments used by the study flow (spec §22).
    ///
    /// TEXT PROVENANCE POLICY:
    /// - English item wordings are the standard published short labels
    ///   (SSQ: Kennedy, Lane, Berbaum &amp; Lilienthal, 1993; Raw NASA-TLX:
    ///   Hart &amp; Staveland, 1988; STAI-6: Marteau &amp; Bekker, 1992).
    /// - BCS (crnogorski/ijekavski) translations below are WORKING TRANSLATIONS
    ///   for the prototype and are flagged NEEDS_VALIDATED_ITEM_TEXT
    ///   (needsValidatedText = true). They must be replaced with validated
    ///   translations before any real data collection, and the UI shows the
    ///   English original alongside.
    /// </summary>
    public static class QuestionnaireCatalog
    {
        public const string SsqId = "SSQ";
        public const string TlxId = "NASA_TLX_RAW";
        public const string StaiId = "STAI6";

        // ── SSQ ──────────────────────────────────────────────────────────
        // Subscales: N = Nausea, O = Oculomotor, D = Disorientation.
        // Item→subscale mapping per the original 1993 publication.
        public static QuestionnaireDefinitionData BuildSsq()
        {
            var q = new QuestionnaireDefinitionData
            {
                questionnaireId = SsqId,
                displayName = "Upitnik simulatorske mučnine (SSQ)",
                instructions = "Označi koliko te trenutno pogađa svaki od navedenih simptoma.",
                textSource = "Kennedy, Lane, Berbaum & Lilienthal (1993) — standard 16-item SSQ; " +
                             "English labels canonical, BCS translation NOT validated.",
                needsValidatedText = true, // BCS side needs validation
                scaleMin = 0,
                scaleMax = 3,
                scaleAnchors = { "Uopšte ne (None)", "Blago (Slight)", "Umjereno (Moderate)", "Jako (Severe)" }
            };

            void Item(string id, string bcs, string en, string subscales) =>
                q.items.Add(new QuestionnaireItemDefinitionData
                {
                    itemId = id, text = bcs, textEnglish = en,
                    subscale = subscales, needsValidatedText = true
                });

            Item("ssq01", "Opšta nelagodnost", "General discomfort", "N,O");
            Item("ssq02", "Umor", "Fatigue", "O");
            Item("ssq03", "Glavobolja", "Headache", "O");
            Item("ssq04", "Naprezanje očiju", "Eye strain", "O");
            Item("ssq05", "Teškoće sa fokusiranjem", "Difficulty focusing", "O,D");
            Item("ssq06", "Pojačano lučenje pljuvačke", "Increased salivation", "N");
            Item("ssq07", "Znojenje", "Sweating", "N");
            Item("ssq08", "Mučnina", "Nausea", "N,D");
            Item("ssq09", "Teškoće sa koncentracijom", "Difficulty concentrating", "N,O");
            Item("ssq10", "Osjećaj punoće u glavi", "Fullness of head", "D");
            Item("ssq11", "Zamagljen vid", "Blurred vision", "O,D");
            Item("ssq12", "Vrtoglavica (otvorene oči)", "Dizzy (eyes open)", "D");
            Item("ssq13", "Vrtoglavica (zatvorene oči)", "Dizzy (eyes closed)", "D");
            Item("ssq14", "Osjećaj okretanja (vertigo)", "Vertigo", "D");
            Item("ssq15", "Svijest o stomaku", "Stomach awareness", "N");
            Item("ssq16", "Podrigivanje", "Burping", "N");
            return q;
        }

        // ── Raw NASA-TLX ─────────────────────────────────────────────────
        // Six dimensions, 0..100 (step 5 in UI). Raw TLX: no pairwise weighting;
        // total = mean of the six dimensions. Individual dimensions are always
        // persisted (spec §22) because the scheduler reads them separately.
        public static QuestionnaireDefinitionData BuildNasaTlx()
        {
            var q = new QuestionnaireDefinitionData
            {
                questionnaireId = TlxId,
                displayName = "NASA-TLX (sirova verzija)",
                instructions = "Ocijeni upravo završenu sesiju na svakoj od šest skala.",
                textSource = "Hart & Staveland (1988), Raw TLX variant; " +
                             "English labels canonical, BCS translation NOT validated.",
                needsValidatedText = true,
                scaleMin = 0,
                scaleMax = 100,
                scaleAnchors = { "Nisko (Low)", "Visoko (High)" }
            };

            void Item(string id, string bcs, string en) =>
                q.items.Add(new QuestionnaireItemDefinitionData
                {
                    itemId = id, text = bcs, textEnglish = en,
                    subscale = id, needsValidatedText = true
                });

            Item("tlx_mental", "Mentalni zahtjev — koliko je zadatak bio mentalno zahtjevan?", "Mental Demand");
            Item("tlx_physical", "Fizički zahtjev — koliko je zadatak bio fizički zahtjevan?", "Physical Demand");
            Item("tlx_temporal", "Vremenski pritisak — koliko je tempo bio užurban?", "Temporal Demand");
            // Performance anchors are inverted (Good→Poor) — handled by reverseScored
            // presentation note; the stored value keeps the standard convention
            // (0 = perfect, 100 = failure).
            Item("tlx_performance", "Učinak — koliko si nezadovoljan/na svojim učinkom? (0 = savršen, 100 = neuspješan)", "Performance");
            Item("tlx_effort", "Napor — koliko si se morao/la truditi?", "Effort");
            Item("tlx_frustration", "Frustracija — koliko si bio/la iznerviran/a i pod stresom?", "Frustration");
            return q;
        }

        // ── STAI-6 (short state anxiety) ─────────────────────────────────
        // Marteau & Bekker (1992): 6 items, 1..4, three reverse-scored,
        // prorated total = sum × 20 / 6 (range 20..80).
        public static QuestionnaireDefinitionData BuildStai6()
        {
            var q = new QuestionnaireDefinitionData
            {
                questionnaireId = StaiId,
                displayName = "Kratka skala trenutne anksioznosti (STAI-6)",
                instructions = "Označi kako se osjećaš UPRAVO SADA, u ovom trenutku.",
                textSource = "Marteau & Bekker (1992) six-item STAI short form; " +
                             "English labels canonical, BCS translation NOT validated.",
                needsValidatedText = true,
                scaleMin = 1,
                scaleMax = 4,
                scaleAnchors = { "Uopšte ne (Not at all)", "Donekle (Somewhat)", "Umjereno (Moderately)", "Veoma (Very much)" }
            };

            void Item(string id, string bcs, string en, bool reverse) =>
                q.items.Add(new QuestionnaireItemDefinitionData
                {
                    itemId = id, text = bcs, textEnglish = en,
                    reverseScored = reverse, needsValidatedText = true
                });

            Item("stai_calm", "Osjećam se smireno", "I feel calm", true);
            Item("stai_tense", "Osjećam se napeto", "I am tense", false);
            Item("stai_upset", "Osjećam se uznemireno", "I feel upset", false);
            Item("stai_relaxed", "Osjećam se opušteno", "I am relaxed", true);
            Item("stai_content", "Osjećam se zadovoljno", "I feel content", true);
            Item("stai_worried", "Osjećam se zabrinuto", "I am worried", false);
            return q;
        }
    }
}
