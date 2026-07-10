using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Tormia.Ontology.Editor
{
    public static class OntologyTestRunnerEditor
    {
        private static TestRunnerApi runner;
        private static OntologyTestCallbacks callbacks;

        [MenuItem("Tools/Ontology/Run Ontology Tests _F8")]
        public static void RunOntologyTests()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            callbacks = new OntologyTestCallbacks();
            runner.RegisterCallbacks(callbacks);
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                groupNames = new[] { "Tormia.Ontology.Tests" }
            }));
        }

        private sealed class OntologyTestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log("[OntologyTests] Started");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Debug.Log($"[OntologyTests] Finished: pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount}");
                if (runner != null)
                {
                    Object.DestroyImmediate(runner);
                    runner = null;
                    callbacks = null;
                }
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.FailCount > 0 && !result.HasChildren)
                {
                    Debug.LogError($"[OntologyTests] Failed: {result.Name}\n{result.Message}\n{result.StackTrace}");
                }
            }
        }
    }
}
