#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Questionnaires;
using StressTraining.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.Tests.EditMode
{
    public sealed class QuestionnairePanelTests
    {
        private GameObject _root;
        private QuestionnairePanel _panel;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("QuestionnairePanelTestRoot", typeof(RectTransform));
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 600f);
            _panel = _root.AddComponent<QuestionnairePanel>();
            _panel.Build(_root.transform, "QuestionnairePanel");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void Ssq_HasExactlySixteenItems()
        {
            Assert.That(QuestionnaireCatalog.BuildSsq().items.Count, Is.EqualTo(16));
        }

        [Test]
        public void Ssq_ItemOrder_MatchesKennedy1993()
        {
            string[] expected =
            {
                "General discomfort", "Fatigue", "Headache", "Eyestrain",
                "Difficulty focusing", "Increased salivation", "Sweating", "Nausea",
                "Difficulty concentrating", "Fullness of head", "Blurred vision",
                "Dizzy, eyes open", "Dizzy, eyes closed", "Vertigo",
                "Stomach awareness", "Burping"
            };

            var actual = QuestionnaireCatalog.BuildSsq().items
                .Select(item => NormalizeScientificLabel(item.textEnglish)).ToArray();
            Assert.That(actual, Is.EqualTo(expected.Select(NormalizeScientificLabel).ToArray()));
        }

        [Test]
        public void Ssq_UsesZeroToThreeScale()
        {
            var ssq = QuestionnaireCatalog.BuildSsq();
            Assert.That(ssq.scaleMin, Is.EqualTo(0));
            Assert.That(ssq.scaleMax, Is.EqualTo(3));
            Assert.That(ssq.scaleAnchors.Count, Is.EqualTo(4));
        }

        [Test]
        public void Ssq_RendersFourItemsPerPage()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            Assert.That(RowsRoot().childCount, Is.EqualTo(4));
        }

        [Test]
        public void Ssq_RendersFourPages()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            for (int page = 1; page <= 4; page++)
            {
                Assert.That(TextAt("QuestionnaireContent/PagedQuestionnaireView/PageText").text,
                    Is.EqualTo("Stranica " + page + " od 4"));
                if (page < 4)
                {
                    AnswerVisibleRows(0);
                    ButtonAt("QuestionnaireContent/PagedQuestionnaireView/NextButton").onClick.Invoke();
                }
            }
        }

        [Test]
        public void Ssq_EachRowContainsFourRealButtons()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            for (int row = 0; row < 4; row++)
            {
                Transform rowTransform = RowsRoot().Find("Row_" + row);
                Assert.That(rowTransform, Is.Not.Null);
                var answerButtons = rowTransform.GetComponentsInChildren<Button>(true)
                    .Where(button => button.name.StartsWith("Answer_", StringComparison.Ordinal))
                    .ToArray();
                Assert.That(answerButtons.Length, Is.EqualTo(4));
                Assert.That(answerButtons.All(button =>
                    button.targetGraphic != null && button.targetGraphic.raycastTarget), Is.True);
            }
        }

        [Test]
        public void Ssq_OnlyOneAnswerPerRowIsStored()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            RowButton(0, 0).onClick.Invoke();
            RowButton(0, 2).onClick.Invoke();

            var answers = Answers();
            Assert.That(answers.Count, Is.EqualTo(1));
            Assert.That(answers["ssq01"], Is.EqualTo(2));
            Assert.That(RowButton(0, 2).colors.normalColor,
                Is.Not.EqualTo(RowButton(0, 0).colors.normalColor));
        }

        [Test]
        public void Ssq_NextIsDisabledUntilPageComplete()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            Button next = ButtonAt("QuestionnaireContent/PagedQuestionnaireView/NextButton");
            Assert.That(next.interactable, Is.False);

            for (int row = 0; row < 3; row++) RowButton(row, 0).onClick.Invoke();
            Assert.That(next.interactable, Is.False);

            RowButton(3, 0).onClick.Invoke();
            Assert.That(next.interactable, Is.True);
        }

        [Test]
        public void Ssq_BackPreservesAnswersAndSelection()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            for (int row = 0; row < 4; row++) RowButton(row, row % 4).onClick.Invoke();
            ButtonAt("QuestionnaireContent/PagedQuestionnaireView/NextButton").onClick.Invoke();
            ButtonAt("QuestionnaireContent/PagedQuestionnaireView/BackButton").onClick.Invoke();

            var answers = Answers();
            Assert.That(answers["ssq01"], Is.EqualTo(0));
            Assert.That(answers["ssq02"], Is.EqualTo(1));
            Assert.That(answers["ssq03"], Is.EqualTo(2));
            Assert.That(answers["ssq04"], Is.EqualTo(3));
            Assert.That(RowButton(2, 2).colors.normalColor,
                Is.Not.EqualTo(RowButton(2, 0).colors.normalColor));
        }

        [Test]
        public void Ssq_FinishSubmitsAllSixteenAnswersInOriginalOrder()
        {
            var flow = new QuestionnaireFlowController(
                new[] { QuestionnaireCatalog.BuildSsq() }, QuestionnairePhase.PostSession);
            int resultCount = 0;
            flow.QuestionnaireCompleted += _ => resultCount++;
            _panel.Begin(flow);
            StartButton().onClick.Invoke();

            CompletePagedQuestionnaire();

            Assert.That(flow.IsFinished, Is.True);
            Assert.That(resultCount, Is.EqualTo(1));
            Assert.That(FlowResponseCount(flow), Is.EqualTo(16));
        }

        [Test]
        public void Ssq_FinishCannotSubmitTwice()
        {
            var flow = new QuestionnaireFlowController(
                new[] { QuestionnaireCatalog.BuildSsq() }, QuestionnairePhase.PostSession);
            int resultCount = 0;
            int finishedCount = 0;
            flow.QuestionnaireCompleted += _ => resultCount++;
            _panel.Finished += () => finishedCount++;
            _panel.Begin(flow);
            StartButton().onClick.Invoke();

            for (int page = 0; page < 3; page++)
            {
                AnswerVisibleRows(0);
                ButtonAt("QuestionnaireContent/PagedQuestionnaireView/NextButton").onClick.Invoke();
            }
            AnswerVisibleRows(0);
            Button finish = ButtonAt("QuestionnaireContent/PagedQuestionnaireView/NextButton");
            finish.onClick.Invoke();
            finish.onClick.Invoke();

            Assert.That(resultCount, Is.EqualTo(1));
            Assert.That(finishedCount, Is.EqualTo(1));
            Assert.That(FlowResponseCount(flow), Is.EqualTo(16));
        }

        [Test]
        public void RepeatedBegin_DoesNotKeepOldFinishedSubscription()
        {
            var oldFlow = new QuestionnaireFlowController(
                new[] { QuestionnaireCatalog.BuildSsq() }, QuestionnairePhase.PreSession);
            var currentFlow = new QuestionnaireFlowController(
                new[] { QuestionnaireCatalog.BuildSsq() }, QuestionnairePhase.PostSession);
            int finishedCount = 0;
            _panel.Finished += () => finishedCount++;

            _panel.Begin(oldFlow);
            _panel.Begin(currentFlow);
            oldFlow.SubmitFixture(_ => 0);
            Assert.That(finishedCount, Is.EqualTo(0));

            currentFlow.SubmitFixture(_ => 0);
            Assert.That(finishedCount, Is.EqualTo(1));
        }

        [Test]
        public void Stai_UsesDynamicHeadersAndValuesOneToFour()
        {
            var stai = QuestionnaireCatalog.BuildStai6();
            BeginAndStart(stai, QuestionnairePhase.CycleStart);

            Assert.That(RowsRoot().childCount, Is.EqualTo(3));
            for (int value = 1; value <= 4; value++)
            {
                string expectedAnchor = stai.scaleAnchors[value - 1];
                int englishStart = expectedAnchor.IndexOf(" (", StringComparison.Ordinal);
                if (englishStart >= 0) expectedAnchor = expectedAnchor.Substring(0, englishStart);
                string expected = value + " " + expectedAnchor.ToUpperInvariant();
                Assert.That(TextAt("QuestionnaireContent/PagedQuestionnaireView/ColumnHeaders/ScaleHeader_" + value).text,
                    Is.EqualTo(expected));
                Assert.That(RowButton(0, value), Is.Not.Null);
            }
        }

        [Test]
        public void NasaTlx_HasClickableMinusPlusAndConfirm()
        {
            BeginAndStart(QuestionnaireCatalog.BuildNasaTlx(), QuestionnairePhase.PostSession);
            Button minus = ButtonAt("QuestionnaireContent/TlxView/TlxMinus");
            Button plus = ButtonAt("QuestionnaireContent/TlxView/TlxPlus");
            Button confirm = ButtonAt("QuestionnaireContent/TlxView/TlxConfirm");

            Assert.That(minus, Is.Not.Null);
            Assert.That(plus, Is.Not.Null);
            Assert.That(confirm, Is.Not.Null);
            plus.onClick.Invoke();
            Assert.That(TextAt("QuestionnaireContent/TlxView/TlxValue").text, Is.EqualTo("55"));
            minus.onClick.Invoke();
            Assert.That(TextAt("QuestionnaireContent/TlxView/TlxValue").text, Is.EqualTo("50"));
        }

        [TestCase("SSQ")]
        [TestCase("STAI")]
        public void SsqAndStai_DoNotDisplayTlxBar(string questionnaire)
        {
            QuestionnaireDefinitionData definition = questionnaire == "SSQ"
                ? QuestionnaireCatalog.BuildSsq()
                : QuestionnaireCatalog.BuildStai6();
            BeginAndStart(definition, QuestionnairePhase.PreSession);

            Assert.That(Find("QuestionnaireContent/TlxView").gameObject.activeSelf, Is.False);
            Assert.That(_panel.PanelRoot.GetComponentsInChildren<Transform>(true)
                .Any(transform => transform.name == "Bar"), Is.False);
        }

        [Test]
        public void ParticipantUi_DoesNotShowInternalValidationTerms()
        {
            BeginAndStart(QuestionnaireCatalog.BuildSsq(), QuestionnairePhase.PreSession);
            var builder = new StringBuilder();
            foreach (var text in _panel.PanelRoot.GetComponentsInChildren<Text>(true))
                builder.AppendLine(text.text);

            string visible = builder.ToString();
            Assert.That(visible, Does.Not.Contain("NEEDS_VALIDATED_ITEM_TEXT"));
            Assert.That(visible, Does.Not.Contain("fixture"));
            Assert.That(visible, Does.Not.Contain("placeholder"));
            Assert.That(visible, Does.Not.Contain("developer mode"));
            Assert.That(visible, Does.Not.Contain("spec §"));
        }

        private QuestionnaireFlowController BeginAndStart(QuestionnaireDefinitionData definition,
            QuestionnairePhase phase)
        {
            var flow = new QuestionnaireFlowController(new[] { definition }, phase);
            _panel.Begin(flow);
            StartButton().onClick.Invoke();
            return flow;
        }

        private void CompletePagedQuestionnaire()
        {
            for (int page = 0; page < 4; page++)
            {
                AnswerVisibleRows(page % 4);
                ButtonAt("QuestionnaireContent/PagedQuestionnaireView/NextButton").onClick.Invoke();
            }
        }

        private void AnswerVisibleRows(int value)
        {
            int count = RowsRoot().childCount;
            for (int row = 0; row < count; row++) RowButton(row, value).onClick.Invoke();
        }

        private Button StartButton()
        {
            return ButtonAt("QuestionnaireContent/IntroView/StartButton");
        }

        private RectTransform RowsRoot()
        {
            return Find("QuestionnaireContent/PagedQuestionnaireView/Rows") as RectTransform;
        }

        private Button RowButton(int row, int value)
        {
            return ButtonAt("QuestionnaireContent/PagedQuestionnaireView/Rows/Row_" + row +
                            "/Answer_" + value);
        }

        private Button ButtonAt(string path)
        {
            Transform transform = Find(path);
            Assert.That(transform, Is.Not.Null, "Missing UI path: " + path);
            return transform.GetComponent<Button>();
        }

        private Text TextAt(string path)
        {
            Transform transform = Find(path);
            Assert.That(transform, Is.Not.Null, "Missing UI path: " + path);
            return transform.GetComponent<Text>();
        }

        private Transform Find(string path)
        {
            return _panel.PanelRoot.transform.Find(path);
        }

        private Dictionary<string, int> Answers()
        {
            FieldInfo field = typeof(QuestionnairePanel).GetField("_answers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Dictionary<string, int>)field.GetValue(_panel);
        }

        private static int FlowResponseCount(QuestionnaireFlowController flow)
        {
            FieldInfo field = typeof(QuestionnaireFlowController).GetField("_responses",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            var responses = field.GetValue(flow) as ICollection;
            return responses != null ? responses.Count : 0;
        }

        private static string NormalizeScientificLabel(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }
    }
}
#endif
