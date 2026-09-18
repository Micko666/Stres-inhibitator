using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Adaptation
{
    /// <summary>
    /// Between-session rule-based scheduler (spec §24). Runs ONLY after a session
    /// is closed — never during an active block (architecture rules 17/18).
    /// Produces one decision per task plus a global pressure decision, each with
    /// machine-readable reason codes, the fired rules, an input snapshot and a
    /// human-readable explanation.
    /// </summary>
    public sealed class AdaptationScheduler
    {
        private readonly AdaptationConfig _cfg;

        public AdaptationScheduler(AdaptationConfig cfg = null)
        {
            _cfg = cfg ?? new AdaptationConfig();
        }

        public AdaptationDecisionData Decide(AdaptationInput input)
        {
            var decision = new AdaptationDecisionData
            {
                decidedAtUtcIso = UtcTime.NowIso(),
                sessionId = input.sessionId,
                inputSnapshotJson = JsonUtility.ToJson(input)
            };

            bool sessionUsable =
                input.validityStatus == ValidityStatus.Valid ||
                input.validityStatus == ValidityStatus.ValidWithWarnings;

            if (!sessionUsable)
            {
                // Invalid/incomplete/demo/time-expired sessions (spec §32 rule 9/11):
                // data is kept, levels are frozen, no routine increase is possible.
                string code = "SESSION_NOT_USABLE_" + input.validityStatus;
                decision.nBack = FrozenTask(input.nBack, code);
                decision.goNoGo = FrozenTask(input.goNoGo, code);
                decision.flanker = FrozenTask(input.flanker, code);
                decision.corsi = FrozenTask(input.corsi, code);
                decision.pressure = new PressureAdaptationDecisionData
                {
                    directive = AdaptationDirective.NoDecisionInvalidSession,
                    previousLevel = input.currentPressureLevel,
                    newLevel = input.currentPressureLevel,
                    reasonCodes = { code }
                };
                decision.firedRules.Add("G_SESSION_INVALID");
                decision.humanExplanation = AdaptationExplanationBuilder.Build(decision, input, _cfg);
                return decision;
            }

            decision.nBack = DecideTask(input, input.nBack, decision.firedRules);
            decision.goNoGo = DecideTask(input, input.goNoGo, decision.firedRules);
            decision.flanker = DecideTask(input, input.flanker, decision.firedRules);
            decision.corsi = DecideTask(input, input.corsi, decision.firedRules);

            var ctx = new AdaptationRuleSet.PressureRuleContext
            {
                AnyTaskIncreased =
                    decision.nBack.directive == AdaptationDirective.Increase ||
                    decision.goNoGo.directive == AdaptationDirective.Increase ||
                    decision.flanker.directive == AdaptationDirective.Increase ||
                    decision.corsi.directive == AdaptationDirective.Increase,
                AnyTaskDecreasedOrRepeat =
                    IsDownOrRepeat(decision.nBack.directive) ||
                    IsDownOrRepeat(decision.goNoGo.directive) ||
                    IsDownOrRepeat(decision.flanker.directive) ||
                    IsDownOrRepeat(decision.corsi.directive)
            };

            var pressureFired = new List<string>();
            var pDirective = AdaptationRuleSet.EvaluatePressure(input, _cfg, ctx, pressureFired);
            decision.firedRules.AddRange(pressureFired);
            decision.pressure = ApplyPressure(input.currentPressureLevel, pDirective, pressureFired);

            decision.humanExplanation = AdaptationExplanationBuilder.Build(decision, input, _cfg);
            return decision;
        }

        private static bool IsDownOrRepeat(AdaptationDirective d) =>
            d == AdaptationDirective.Decrease || d == AdaptationDirective.RepeatForStability;

        private TaskAdaptationDecisionData FrozenTask(TaskMetricsInput m, string code) =>
            new TaskAdaptationDecisionData
            {
                taskType = m.taskType,
                directive = AdaptationDirective.NoDecisionInvalidSession,
                previousLevel = m.currentLevel,
                newLevel = m.currentLevel,
                reasonCodes = { code }
            };

        private TaskAdaptationDecisionData DecideTask(
            AdaptationInput input, TaskMetricsInput metrics, List<string> firedRules)
        {
            var d = new TaskAdaptationDecisionData
            {
                taskType = metrics.taskType,
                previousLevel = metrics.currentLevel,
                directive = AdaptationDirective.Hold
            };

            // The 3-of-4 selector omitted this task — no performance data exists,
            // so its level is held verbatim (spec §33).
            if (!metrics.wasSelectedThisSession)
            {
                d.newLevel = metrics.currentLevel;
                d.reasonCodes.Add("NOT_SELECTED_THIS_SESSION");
                firedRules.Add($"{metrics.taskType}:NOT_SELECTED_THIS_SESSION");
                return d;
            }

            foreach (var rule in AdaptationRuleSet.TaskRules)
            {
                var directive = rule.Evaluate(input, metrics, _cfg);
                if (directive == null) continue;
                d.directive = directive.Value;
                d.reasonCodes.Add(rule.Id);
                firedRules.Add($"{metrics.taskType}:{rule.Id}");
                break;
            }

            int level = metrics.currentLevel;
            switch (d.directive)
            {
                case AdaptationDirective.Increase:
                    if (level >= _cfg.maxLevel)
                    {
                        d.directive = AdaptationDirective.Hold;
                        d.reasonCodes.Add("AT_MAX_LEVEL");
                    }
                    else level++;
                    break;
                case AdaptationDirective.Decrease:
                    if (level <= _cfg.minLevel)
                    {
                        d.directive = AdaptationDirective.RepeatForStability;
                        d.reasonCodes.Add("AT_MIN_LEVEL_REPEAT");
                    }
                    else level--;
                    break;
            }
            d.newLevel = level;
            return d;
        }

        private PressureAdaptationDecisionData ApplyPressure(
            int current, AdaptationDirective directive, List<string> reasonCodes)
        {
            var d = new PressureAdaptationDecisionData
            {
                directive = directive,
                previousLevel = current,
                newLevel = current
            };
            d.reasonCodes.AddRange(reasonCodes);

            switch (directive)
            {
                case AdaptationDirective.Increase:
                    if (current >= _cfg.maxPressureLevel)
                    {
                        d.directive = AdaptationDirective.Hold;
                        d.reasonCodes.Add("AT_MAX_PRESSURE");
                    }
                    else d.newLevel = current + 1;
                    break;
                case AdaptationDirective.Decrease:
                    if (current <= _cfg.minPressureLevel)
                    {
                        d.directive = AdaptationDirective.Hold;
                        d.reasonCodes.Add("AT_MIN_PRESSURE");
                    }
                    else d.newLevel = current - 1;
                    break;
            }
            return d;
        }
    }
}
