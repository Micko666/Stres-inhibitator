// TEMP helper (outside Assets/, never committed): runs EditMode tests through
// the official Unity Test Runner API and writes results to a polled file.
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

public static class FableTestRunner
{
    private static TestRunnerApi _api;
    private static string _resultPath;

    public static void Execute()
    {
        _resultPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "FABLE_EDITMODE_TEST_RESULTS.txt");
        File.WriteAllText(_resultPath, "RUNNING");
        string failPath = _resultPath + ".failures";
        if (File.Exists(failPath)) File.Delete(failPath);

        _api = ScriptableObject.CreateInstance<TestRunnerApi>();
        _api.RegisterCallbacks(new ResultWriter(_resultPath));
        var filter = new Filter { testMode = TestMode.EditMode };
        _api.Execute(new ExecutionSettings(filter));
        Debug.Log("[FableTestRunner] EditMode test run started → " + _resultPath);
    }

    private sealed class ResultWriter : ICallbacks
    {
        private readonly string _path;
        public ResultWriter(string path) { _path = path; }

        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            File.WriteAllText(_path,
                "DONE passed=" + result.PassCount +
                " failed=" + result.FailCount +
                " skipped=" + result.SkipCount +
                " duration=" + result.Duration.ToString("0.0") + "s");
            Debug.Log("[FableTestRunner] " + File.ReadAllText(_path));
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus == TestStatus.Failed && !result.HasChildren)
                File.AppendAllText(_path + ".failures",
                    result.FullName + " :: " + result.Message + "\n");
        }
    }
}
