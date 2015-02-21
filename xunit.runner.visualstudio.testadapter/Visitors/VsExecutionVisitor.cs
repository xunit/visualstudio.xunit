using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;
using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class VsExecutionVisitor : TestMessageVisitor<ITestAssemblyFinished>
    {
        readonly Func<bool> cancelledThunk;
        readonly ITestFrameworkExecutionOptions executionOptions;
        readonly ITestExecutionRecorder recorder;
        readonly Dictionary<ITestCase, TestCase> testCases;

        public VsExecutionVisitor(ITestExecutionRecorder recorder,
                                  Dictionary<ITestCase, TestCase> testCases,
                                  ITestFrameworkExecutionOptions executionOptions,
                                  Func<bool> cancelledThunk)
        {
            this.recorder = recorder;
            this.testCases = testCases;
            this.executionOptions = executionOptions;
            this.cancelledThunk = cancelledThunk;
        }

        TestCase FindTestCase(ITestCase testCase)
        {
            TestCase result;
            if (testCases.TryGetValue(testCase, out result))
                return result;

            result = testCases.Where(tc => tc.Key.UniqueID == testCase.UniqueID).Select(kvp => kvp.Value).FirstOrDefault();
            if (result != null)
                return result;

            recorder.SendMessage(TestMessageLevel.Error, String.Format("Result reported for unknown test case: {0}", testCase.DisplayName));
            return null;
        }

        protected override bool Visit(IErrorMessage error)
        {
            recorder.SendMessage(TestMessageLevel.Error, String.Format("Catastrophic failure: {0}", ExceptionUtility.CombineMessages(error)));

            return !cancelledThunk();
        }

        protected override bool Visit(ITestFailed testFailed)
        {
            var result = MakeVsTestResult(TestOutcome.Failed, testFailed);
            if (result != null)
            {
                result.ErrorMessage = ExceptionUtility.CombineMessages(testFailed);
                result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(testFailed);

                recorder.RecordEnd(result.TestCase, result.Outcome);
                recorder.RecordResult(result);
            }

            return !cancelledThunk();
        }

        protected override bool Visit(ITestPassed testPassed)
        {
            var result = MakeVsTestResult(TestOutcome.Passed, testPassed);
            if (result != null)
            {
                recorder.RecordEnd(result.TestCase, result.Outcome);
                recorder.RecordResult(result);
            }

            return !cancelledThunk();
        }

        protected override bool Visit(ITestSkipped testSkipped)
        {
            var result = MakeVsTestResult(TestOutcome.Skipped, testSkipped);
            if (result != null)
            {
                recorder.RecordEnd(result.TestCase, result.Outcome);
                recorder.RecordResult(result);
            }

            return !cancelledThunk();
        }

        protected override bool Visit(ITestStarting testStarting)
        {
            var vsTestCase = FindTestCase(testStarting.TestCase);
            if (vsTestCase != null)
                recorder.RecordStart(vsTestCase);

            return !cancelledThunk();
        }

        protected override bool Visit(ITestAssemblyCleanupFailure cleanupFailure)
        {
            return WriteError(String.Format("Test Assembly Cleanup Failure ({0})", cleanupFailure.TestAssembly.Assembly.AssemblyPath), cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestCaseCleanupFailure cleanupFailure)
        {
            return WriteError(String.Format("Test Case Cleanup Failure ({0})", cleanupFailure.TestCase.DisplayName), cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestClassCleanupFailure cleanupFailure)
        {
            return WriteError(String.Format("Test Class Cleanup Failure ({0})", cleanupFailure.TestClass.Class.Name), cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestCollectionCleanupFailure cleanupFailure)
        {
            return WriteError(String.Format("Test Collection Cleanup Failure ({0})", cleanupFailure.TestCollection.DisplayName), cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestCleanupFailure cleanupFailure)
        {
            return WriteError(String.Format("Test Cleanup Failure ({0})", cleanupFailure.Test.DisplayName), cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestMethodCleanupFailure cleanupFailure)
        {
            return WriteError(String.Format("Test Method Cleanup Failure ({0})", cleanupFailure.TestMethod.Method.Name), cleanupFailure, cleanupFailure.TestCases);
        }

        protected bool WriteError(string failureName, IFailureInformation failureInfo, IEnumerable<ITestCase> testCases)
        {
            foreach (var testCase in testCases)
            {
                var result = MakeVsTestResult(TestOutcome.Failed, testCase, testCase.DisplayName);
                if (result != null)
                {
                    result.ErrorMessage = String.Format("[{0}]: {1}", failureName, ExceptionUtility.CombineMessages(failureInfo));
                    result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(failureInfo);

                    recorder.RecordEnd(result.TestCase, result.Outcome);
                    recorder.RecordResult(result);
                }
            }

            return !cancelledThunk();
        }

        private VsTestResult MakeVsTestResult(TestOutcome outcome, ITestResultMessage testResult)
        {
            return MakeVsTestResult(outcome, testResult.TestCase, testResult.Test.DisplayName, (double)testResult.ExecutionTime, testResult.Output);
        }

        private VsTestResult MakeVsTestResult(TestOutcome outcome, ITestCase testCase, string displayName, double executionTime = 0.0, string output = null)
        {
            var vsTestCase = FindTestCase(testCase);
            if (vsTestCase == null)
                return null;

            var result = new VsTestResult(vsTestCase)
            {
#if !WINDOWS_PHONE_APP && !WINDOWS_PHONE
                ComputerName = Environment.MachineName,
#endif
                DisplayName = displayName,
                Duration = TimeSpan.FromSeconds(executionTime),
                Outcome = outcome,
            };

            // Work around VS considering a test "not run" when the duration is 0
            if (result.Duration.TotalMilliseconds == 0)
                result.Duration = TimeSpan.FromMilliseconds(1);

            if (!String.IsNullOrEmpty(output))
                result.Messages.Add(new VsTestResultMessage(VsTestResultMessage.StandardOutCategory, output));

            return result;
        }
    }
}