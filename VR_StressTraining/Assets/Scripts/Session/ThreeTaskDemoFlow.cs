using StressTraining.Data;

namespace StressTraining.Session
{
    /// <summary>
    /// User-visible three-task demo flow. This is presentation/orchestration
    /// state only; it does not add a scientific or adaptive state machine.
    /// </summary>
    public enum DemoFlowStage
    {
        ProfileSelection = 0,
        DemoOverview = 1,
        NBackTutorial = 2,
        NBackPractice = 3,
        NBackPracticeReview = 4,
        NBackBlockCountdown = 5,
        NBackBlock = 6,
        GoNoGoTutorial = 7,
        GoNoGoPractice = 8,
        GoNoGoPracticeReview = 9,
        GoNoGoBlockCountdown = 10,
        GoNoGoBlock = 11,
        FlankerTutorial = 12,
        FlankerPractice = 13,
        FlankerPracticeReview = 14,
        FlankerBlockCountdown = 15,
        FlankerBlock = 16,
        SessionSummary = 17
    }

    public static class ThreeTaskDemoFlow
    {
        public static DemoFlowStage TutorialStage(TaskType task) => task switch
        {
            TaskType.NBack => DemoFlowStage.NBackTutorial,
            TaskType.GoNoGo => DemoFlowStage.GoNoGoTutorial,
            TaskType.Flanker => DemoFlowStage.FlankerTutorial,
            _ => DemoFlowStage.DemoOverview
        };

        public static DemoFlowStage PracticeStage(TaskType task) => task switch
        {
            TaskType.NBack => DemoFlowStage.NBackPractice,
            TaskType.GoNoGo => DemoFlowStage.GoNoGoPractice,
            TaskType.Flanker => DemoFlowStage.FlankerPractice,
            _ => DemoFlowStage.DemoOverview
        };

        public static DemoFlowStage PracticeReviewStage(TaskType task) => task switch
        {
            TaskType.NBack => DemoFlowStage.NBackPracticeReview,
            TaskType.GoNoGo => DemoFlowStage.GoNoGoPracticeReview,
            TaskType.Flanker => DemoFlowStage.FlankerPracticeReview,
            _ => DemoFlowStage.DemoOverview
        };

        public static DemoFlowStage CountdownStage(TaskType task) => task switch
        {
            TaskType.NBack => DemoFlowStage.NBackBlockCountdown,
            TaskType.GoNoGo => DemoFlowStage.GoNoGoBlockCountdown,
            TaskType.Flanker => DemoFlowStage.FlankerBlockCountdown,
            _ => DemoFlowStage.DemoOverview
        };

        public static DemoFlowStage BlockStage(TaskType task) => task switch
        {
            TaskType.NBack => DemoFlowStage.NBackBlock,
            TaskType.GoNoGo => DemoFlowStage.GoNoGoBlock,
            TaskType.Flanker => DemoFlowStage.FlankerBlock,
            _ => DemoFlowStage.DemoOverview
        };

        public static TaskType NextTask(TaskType current) => current switch
        {
            TaskType.NBack => TaskType.GoNoGo,
            TaskType.GoNoGo => TaskType.Flanker,
            _ => TaskType.None
        };
    }
}
