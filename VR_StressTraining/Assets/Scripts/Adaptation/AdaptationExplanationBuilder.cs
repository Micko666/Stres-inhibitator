using System.Text;
using StressTraining.Data;

namespace StressTraining.Adaptation
{
    /// <summary>
    /// Turns a scheduler decision into a short, honest BCS explanation shown to
    /// the user in the AdaptationReview state (spec §24). No clinical claims —
    /// only what the rules actually saw.
    /// </summary>
    public static class AdaptationExplanationBuilder
    {
        public static string Build(AdaptationDecisionData d, AdaptationInput input, AdaptationConfig cfg)
        {
            var sb = new StringBuilder(512);

            if (d.pressure.directive == AdaptationDirective.NoDecisionInvalidSession)
            {
                sb.Append("Ova sesija nije korišćena za prilagođavanje težine (");
                sb.Append(ValidityText(input.validityStatus));
                sb.Append("). Svi nivoi ostaju nepromijenjeni, a podaci su sačuvani.");
                return sb.ToString();
            }

            sb.Append(TaskLine("N-back", d.nBack));
            sb.Append(' ');
            sb.Append(TaskLine("Go/No-Go", d.goNoGo));
            sb.Append(' ');
            sb.Append(TaskLine("Flanker", d.flanker));
            sb.Append(' ');
            sb.Append(TaskLine("Sekvencijalna memorija", d.corsi));
            sb.Append(' ');
            sb.Append(PressureLine(d.pressure, input, cfg));
            return sb.ToString();
        }

        /// <summary>
        /// PRESENTATION ONLY — same decision, readable shape. One line per task with
        /// "nivo X → nivo Y", and the reason on its own line beneath it. Replaces the
        /// single run-on paragraph <see cref="Build"/> produces, which overflowed the
        /// review panel. The decision, its rules and the persisted humanExplanation
        /// are unchanged.
        /// </summary>
        public static string BuildCompact(AdaptationDecisionData d)
        {
            if (d == null) return "Prilagođavanje nije izračunato.\nSvi nivoi ostaju nepromijenjeni.";

            var sb = new StringBuilder(420);
            AppendCompactTask(sb, "N-back", d.nBack);
            AppendCompactTask(sb, "Go/No-Go", d.goNoGo);
            AppendCompactTask(sb, "Flanker", d.flanker);
            AppendCompactTask(sb, "Sekvencijalna memorija", d.corsi);

            sb.Append("Globalni pritisak: nivo ").Append(d.pressure.previousLevel)
              .Append(" → nivo ").Append(d.pressure.newLevel).Append('\n');
            sb.Append(CompactPressureReason(d.pressure));
            return sb.ToString();
        }

        private static void AppendCompactTask(StringBuilder sb, string name,
            TaskAdaptationDecisionData t)
        {
            if (t == null) return;
            sb.Append(name).Append(": nivo ").Append(t.previousLevel)
              .Append(" → nivo ").Append(t.newLevel).Append('\n');
            sb.Append(CompactTaskReason(t)).Append("\n\n");
        }

        private static string CompactTaskReason(TaskAdaptationDecisionData t)
        {
            if (t.reasonCodes.Contains("NOT_SELECTED_THIS_SESSION"))
                return "Nije korišćen u ovoj sesiji.";
            switch (t.directive)
            {
                case AdaptationDirective.Increase:
                    return "Visoka tačnost uz prihvatljivo opterećenje.";
                case AdaptationDirective.Decrease:
                    return "Tačnost je bila ispod radne granice.";
                case AdaptationDirective.RepeatForStability:
                    return "Nivo se ponavlja radi stabilnosti rezultata.";
                case AdaptationDirective.NoDecisionInvalidSession:
                    return "Sesija nije korišćena za prilagođavanje.";
                default:
                    if (t.reasonCodes.Contains("AT_MAX_LEVEL")) return "Već je dostignut najviši nivo.";
                    if (t.reasonCodes.Contains("T_HIGH_ACC_HIGH_COST_HOLD"))
                        return "Dobar rezultat, ali uz visoku fiziološku ili subjektivnu cijenu.";
                    if (t.reasonCodes.Contains("T_FRUSTRATION_HOLD"))
                        return "Prijavljena frustracija je bila visoka.";
                    if (t.reasonCodes.Contains("T_TEMPORAL_DOMINANT_HOLD"))
                        return "Vremenski pritisak je dominirao — vrijeme se ne skraćuje dodatno.";
                    if (t.reasonCodes.Contains("T_MENTAL_DOMINANT_MEMORY_HOLD"))
                        return "Mentalno opterećenje je dominiralo.";
                    return "Rezultat je stabilan.";
            }
        }

        private static string CompactPressureReason(PressureAdaptationDecisionData p)
        {
            switch (p.directive)
            {
                case AdaptationDirective.Increase:
                    return "Svi zadaci stabilni uz prihvatljiv oporavak pulsa.";
                case AdaptationDirective.Decrease:
                    return "Opterećenje visoko, oporavak pulsa sporiji od radne granice.";
                case AdaptationDirective.NoDecisionInvalidSession:
                    return "Sesija nije korišćena za prilagođavanje.";
                default:
                    if (p.reasonCodes.Contains("P_TASK_INCREASED_HOLD"))
                        return "Task i pritisak se nikad ne povećavaju istovremeno.";
                    if (p.reasonCodes.Contains("P_HIGH_COST_HOLD"))
                        return "Opterećenje je bilo blizu granice.";
                    return "Rezultat je stabilan.";
            }
        }

        private static string TaskLine(string name, TaskAdaptationDecisionData t)
        {
            if (t.reasonCodes.Contains("NOT_SELECTED_THIS_SESSION"))
                return $"{name}: nije bio u ovoj sesiji — nivo ostaje {t.newLevel}.";
            switch (t.directive)
            {
                case AdaptationDirective.Increase:
                    return $"{name}: nivo se povećava na {t.newLevel} — uspješnost je bila visoka uz prihvatljivo opterećenje.";
                case AdaptationDirective.Decrease:
                    return $"{name}: nivo se smanjuje na {t.newLevel} — tačnost je bila ispod radne granice.";
                case AdaptationDirective.RepeatForStability:
                    return $"{name}: nivo {t.newLevel} se ponavlja radi stabilnosti rezultata.";
                default:
                    string why = t.reasonCodes.Contains("T_HIGH_ACC_HIGH_COST_HOLD")
                        ? " — uspješnost je bila dobra, ali je zabilježena visoka fiziološka ili subjektivna cijena."
                        : t.reasonCodes.Contains("T_FRUSTRATION_HOLD")
                            ? " — prijavljena frustracija je bila visoka."
                            : t.reasonCodes.Contains("T_TEMPORAL_DOMINANT_HOLD")
                                ? " — vremenski pritisak je dominirao u NASA-TLX odgovorima, pa se vrijeme dodatno ne skraćuje."
                                : t.reasonCodes.Contains("T_MENTAL_DOMINANT_MEMORY_HOLD")
                                    ? " — mentalno opterećenje je dominiralo, pa se nivo memorijskog zadatka ne podiže odmah."
                                    : t.reasonCodes.Contains("AT_MAX_LEVEL")
                                        ? " — već je dostignut najviši nivo."
                                        : ".";
                    return $"{name}: nivo ostaje {t.newLevel}{why}";
            }
        }

        private static string PressureLine(PressureAdaptationDecisionData p, AdaptationInput input, AdaptationConfig cfg)
        {
            switch (p.directive)
            {
                case AdaptationDirective.Increase:
                    return $"Globalni nivo pritiska se povećava na {p.newLevel} — svi zadaci su bili stabilni uz prihvatljiv oporavak pulsa.";
                case AdaptationDirective.Decrease:
                    return $"Globalni nivo pritiska se smanjuje na {p.newLevel} — opterećenje je bilo visoko, a oporavak pulsa sporiji od projektne radne granice.";
                default:
                    if (p.reasonCodes.Contains("P_TASK_INCREASED_HOLD"))
                        return "Globalni nivo pritiska je zadržan jer se u ovoj odluci povećava težina zadatka — nikad oboje istovremeno.";
                    if (p.reasonCodes.Contains("P_HIGH_COST_HOLD"))
                        return "Globalni nivo pritiska je zadržan — uspješnost je bila dobra, ali je fiziološko ili subjektivno opterećenje bilo blizu granice.";
                    return "Globalni nivo pritiska je zadržan.";
            }
        }

        private static string ValidityText(ValidityStatus v)
        {
            switch (v)
            {
                case ValidityStatus.InvalidSimulatorSickness: return "prijavljeni su simptomi simulatorske mučnine";
                case ValidityStatus.InvalidTechnicalFailure: return "tehnički problem tokom sesije";
                case ValidityStatus.InvalidTrackingFailure: return "problem sa praćenjem pokreta";
                case ValidityStatus.InvalidInsufficientHeartRate: return "nedovoljno validnih mjerenja pulsa";
                case ValidityStatus.IncompleteUserTerminated: return "sesija je prekinuta prije kraja";
                case ValidityStatus.DemoOnly: return "demo sesija";
                default: return "sesija nije validna";
            }
        }
    }
}
