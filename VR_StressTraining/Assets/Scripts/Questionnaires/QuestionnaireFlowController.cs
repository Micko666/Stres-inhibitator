using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.Questionnaires
{
    /// <summary>
    /// Pure flow model for administering one or more questionnaires in sequence.
    /// The UI panel renders CurrentItem and calls SubmitCurrentResponse; the flow
    /// never writes JSON itself (architecture rule 2) — completed results are
    /// handed to the coordinator via <see cref="QuestionnaireCompleted"/>.
    /// </summary>
    public sealed class QuestionnaireFlowController
    {
        private readonly Queue<QuestionnaireDefinitionData> _queue = new Queue<QuestionnaireDefinitionData>();
        private readonly QuestionnairePhase _phase;
        private List<QuestionnaireResponseData> _responses;

        public QuestionnaireDefinitionData CurrentQuestionnaire { get; private set; }
        public int CurrentItemIndex { get; private set; }
        public bool IsFinished { get; private set; }
        public QuestionnairePhase Phase => _phase;

        public QuestionnaireItemDefinitionData CurrentItem =>
            CurrentQuestionnaire != null && CurrentItemIndex < CurrentQuestionnaire.items.Count
                ? CurrentQuestionnaire.items[CurrentItemIndex] : null;

        public event Action<QuestionnaireResultData> QuestionnaireCompleted;
        public event Action AllCompleted;

        /// <summary>When true, results are permanently flagged as developer fixtures.</summary>
        public bool IsFixtureMode { get; set; }

        public QuestionnaireFlowController(IEnumerable<QuestionnaireDefinitionData> questionnaires,
            QuestionnairePhase phase)
        {
            _phase = phase;
            foreach (var q in questionnaires) _queue.Enqueue(q);
            // No event may fire from the constructor (subscribers attach after);
            // an empty queue is visible through IsFinished immediately.
            AdvanceQuestionnaire(raiseCompletion: false);
        }

        private void AdvanceQuestionnaire(bool raiseCompletion = true)
        {
            if (_queue.Count == 0)
            {
                CurrentQuestionnaire = null;
                IsFinished = true;
                if (raiseCompletion) AllCompleted?.Invoke();
                return;
            }
            CurrentQuestionnaire = _queue.Dequeue();
            CurrentItemIndex = 0;
            _responses = new List<QuestionnaireResponseData>(CurrentQuestionnaire.items.Count);
        }

        /// <summary>Records the answer for the current item and advances. Value is clamped to scale.</summary>
        public void SubmitCurrentResponse(int value)
        {
            var item = CurrentItem;
            if (item == null) return;

            int clamped = Math.Max(CurrentQuestionnaire.scaleMin,
                Math.Min(CurrentQuestionnaire.scaleMax, value));
            _responses.Add(new QuestionnaireResponseData
            {
                itemId = item.itemId,
                value = clamped,
                answeredAtUtcIso = UtcTime.NowIso()
            });

            CurrentItemIndex++;
            if (CurrentItemIndex >= CurrentQuestionnaire.items.Count)
            {
                var result = QuestionnaireScoringService.Score(
                    CurrentQuestionnaire, _responses, _phase, IsFixtureMode);
                QuestionnaireCompleted?.Invoke(result);
                AdvanceQuestionnaire();
            }
        }

        /// <summary>Developer fixture path: answer everything at once (spec §22/§26).</summary>
        public void SubmitFixture(Func<QuestionnaireItemDefinitionData, int> valueSelector)
        {
            while (!IsFinished && CurrentItem != null)
                SubmitCurrentResponse(valueSelector(CurrentItem));
        }
    }
}
