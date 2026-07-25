using System.Text;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Short, level-specific participant instructions.
    ///
    /// EVERY sentence below is derived from the REAL numbers in
    /// <see cref="TaskDifficultyConfig"/> — the rule line, and then what actually
    /// changes at this level compared to the level before it (response window,
    /// congruent/incongruent mix, set size, no-go share, sequence length, forward vs
    /// backward). Nothing here invents a rule the task engine does not implement.
    ///
    /// Levels only ever change BETWEEN sessions, so a page is shown the first time a
    /// participant meets a task, and again the first time they meet a new level of
    /// it — never before every block of the same level.
    /// </summary>
    public static class TaskInstructions
    {
        /// <summary>"Flanker — nivo 2"</summary>
        public static string Title(TaskType task, int level) =>
            DemoTaskFactory.DisplayName(task) + " — nivo " + TaskDifficultyConfig.Clamp(level);

        /// <summary>The unchanging rule of the task (identical at every level).</summary>
        public static string Rule(TaskType task)
        {
            switch (task)
            {
                case TaskType.Flanker:
                    return "Odredi smjer SREDNJE strelice i pritisni LEFT ili RIGHT. " +
                           "Spoljašnje strelice su samo ometanje — ignoriši ih.";
                case TaskType.NBack:
                    return "Prati simbole. Pritisni MATCH ako je trenutni simbol isti kao onaj " +
                           "N koraka ranije, inače NO MATCH.";
                case TaskType.GoNoGo:
                    return "Pritisni GO kada se pojavi GO. Kada se pojavi NO GO — ne pritiskaj ništa.";
                case TaskType.CorsiSequence:
                    return "Zapamti redosljed kojim pozicije svijetle na tabletu, pa ga ponovi " +
                           "na devet dugmadi konzole.";
                default:
                    return "";
            }
        }

        /// <summary>
        /// What is actually different at this level, read from the difficulty table.
        /// Level 1 states the starting values; levels 2–3 state the delta vs. level−1.
        /// </summary>
        public static string LevelChange(TaskType task, int level)
        {
            level = TaskDifficultyConfig.Clamp(level);
            TaskLevelParameters p = TaskDifficultyConfig.Get(task, level);
            TaskLevelParameters previous = level > TaskDifficultyConfig.MinLevel
                ? TaskDifficultyConfig.Get(task, level - 1)
                : null;

            switch (task)
            {
                case TaskType.Flanker: return Flanker(p, previous);
                case TaskType.NBack: return NBack(p, previous);
                case TaskType.GoNoGo: return GoNoGo(p, previous);
                case TaskType.CorsiSequence: return Corsi(p, previous);
                default: return "";
            }
        }

        /// <summary>Full page body: rule, then the level delta.</summary>
        public static string Page(TaskType task, int level)
        {
            var sb = new StringBuilder(320);
            sb.Append(Rule(task)).Append("\n\n").Append(LevelChange(task, level));
            return sb.ToString();
        }

        // ── per-task level deltas (real config values only) ───────────────

        private static string Flanker(TaskLevelParameters p, TaskLevelParameters previous)
        {
            var sb = new StringBuilder(220);
            if (previous == null)
            {
                sb.Append("Pravilo je uvijek isto. Na ovom nivou imaš ")
                  .Append(Seconds(p.responseWindowSeconds)).Append(" za odgovor, a ")
                  .Append(Percent(p.incongruentProportion))
                  .Append(" pokušaja ima suprotno usmjerene spoljašnje strelice.");
                return sb.ToString();
            }

            sb.Append("Pravilo ostaje isto. ");
            sb.Append(Faster(p.responseWindowSeconds, previous.responseWindowSeconds,
                "Vrijeme za odgovor je kraće: " + Seconds(p.responseWindowSeconds) + ". ",
                "Vrijeme za odgovor: " + Seconds(p.responseWindowSeconds) + ". "));
            if (p.incongruentProportion > previous.incongruentProportion)
                sb.Append("Ometajućih (suprotnih) kombinacija ima više — ")
                  .Append(Percent(p.incongruentProportion)).Append(" pokušaja. ");
            if (p.neutralProportion <= 0 && previous.neutralProportion > 0)
                sb.Append("Neutralnih (—) nizova više nema. ");
            return sb.ToString().TrimEnd();
        }

        private static string NBack(TaskLevelParameters p, TaskLevelParameters previous)
        {
            var sb = new StringBuilder(240);
            sb.Append("Ovdje je N = ").Append(p.nBackN).Append(" (")
              .Append(p.nBackN == 1 ? "prethodni simbol" : "simbol dva koraka ranije")
              .Append("). ");

            if (previous == null)
            {
                sb.Append("Koristi se ").Append(p.stimulusSetSize)
                  .Append(" različitih simbola, uz ").Append(Seconds(p.responseWindowSeconds))
                  .Append(" za odgovor.");
                return sb.ToString();
            }

            if (p.nBackN > previous.nBackN)
                sb.Append("Pravilo se mijenja: sada porediš sa simbolom ")
                  .Append(p.nBackN).Append(" koraka ranije, ne sa prethodnim. ");
            if (p.stimulusSetSize > previous.stimulusSetSize)
                sb.Append("Skup simbola je veći (").Append(p.stimulusSetSize).Append("). ");
            sb.Append(Faster(p.responseWindowSeconds, previous.responseWindowSeconds,
                "Vrijeme za odgovor je kraće: " + Seconds(p.responseWindowSeconds) + ".",
                "Vrijeme za odgovor: " + Seconds(p.responseWindowSeconds) + "."));
            return sb.ToString().TrimEnd();
        }

        private static string GoNoGo(TaskLevelParameters p, TaskLevelParameters previous)
        {
            var sb = new StringBuilder(240);
            if (previous == null)
            {
                sb.Append("NO GO se pojavljuje u ").Append(Percent(p.noGoProportion))
                  .Append(" pokušaja, znaci su jasno različiti, a imaš ")
                  .Append(Seconds(p.responseWindowSeconds)).Append(" za GO.");
                return sb.ToString();
            }

            sb.Append("Pravilo ostaje isto. ");
            if (p.noGoProportion > previous.noGoProportion)
                sb.Append("NO GO se javlja češće — ").Append(Percent(p.noGoProportion))
                  .Append(" pokušaja, pa je zaustavljanje teže. ");
            if (p.stimulusSimilarityTier > previous.stimulusSimilarityTier)
                sb.Append("GO i NO GO znaci su vizuelno sličniji. ");
            sb.Append(Faster(p.responseWindowSeconds, previous.responseWindowSeconds,
                "Vrijeme za odgovor je kraće: " + Seconds(p.responseWindowSeconds) + ".",
                "Vrijeme za odgovor: " + Seconds(p.responseWindowSeconds) + "."));
            return sb.ToString().TrimEnd();
        }

        private static string Corsi(TaskLevelParameters p, TaskLevelParameters previous)
        {
            var sb = new StringBuilder(260);
            if (p.corsiBackward)
                sb.Append("PRAVILO SE MIJENJA: sekvencu ponavljaš UNAZAD — kreni od " +
                          "posljednje pozicije koja je zasvijetlila. ");
            else
                sb.Append("Sekvencu ponavljaš istim redosljedom kojim je prikazana. ");

            sb.Append("Dužina niza: ").Append(p.corsiMinSequenceLength)
              .Append("–").Append(p.corsiMaxSequenceLength).Append(" pozicija. ");

            if (previous == null)
            {
                sb.Append("Svaka pozicija svijetli ")
                  .Append(Seconds(p.corsiPresentationStepSeconds)).Append(".");
                return sb.ToString();
            }

            if (p.corsiMaxSequenceLength > previous.corsiMaxSequenceLength)
                sb.Append("Nizovi su duži nego ranije. ");
            sb.Append(Faster(p.corsiPresentationStepSeconds, previous.corsiPresentationStepSeconds,
                "Pozicije svijetle kraće: " + Seconds(p.corsiPresentationStepSeconds) + ".",
                "Pozicije svijetle " + Seconds(p.corsiPresentationStepSeconds) + "."));
            sb.Append(" Prva greška prekida pokušaj.");
            return sb.ToString();
        }

        // ── formatting helpers ───────────────────────────────────────────

        private static string Faster(double now, double before, string shorter, string same) =>
            now < before ? shorter : same;

        private static string Seconds(double value) =>
            value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

        private static string Percent(double proportion) =>
            System.Math.Round(proportion * 100.0).ToString(
                System.Globalization.CultureInfo.InvariantCulture) + " %";
    }
}
