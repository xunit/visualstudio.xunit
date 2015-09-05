using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Xunit.Abstractions;
using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class VsExecutionVisitor : TestMessageVisitor<ITestAssemblyFinished>
    {
        readonly Func<bool> cancelledThunk;
        readonly ITestFrameworkExecutionOptions executionOptions;
        readonly LoggerHelper logger;
        readonly ITestExecutionRecorder recorder;
        readonly Dictionary<ITestCase, TestCase> testCases;

        public VsExecutionVisitor(ITestExecutionRecorder recorder,
                                  LoggerHelper logger,
                                  Dictionary<ITestCase, TestCase> testCases,
                                  ITestFrameworkExecutionOptions executionOptions,
                                  Func<bool> cancelledThunk)
        {
            this.recorder = recorder;
            this.logger = logger;
            this.testCases = testCases;
            this.executionOptions = executionOptions;
            this.cancelledThunk = cancelledThunk;

            ExecutionSummary = new ExecutionSummary();
        }

        public ExecutionSummary ExecutionSummary { get; private set; }

        TestCase FindTestCase(ITestCase testCase)
        {
            TestCase result;
            if (testCases.TryGetValue(testCase, out result))
                return result;

            result = testCases.Where(tc => tc.Key.UniqueID == testCase.UniqueID).Select(kvp => kvp.Value).FirstOrDefault();
            if (result != null)
                return result;

            logger.LogError(testCase, "Result reported for unknown test case: {0}", testCase.DisplayName);
            return null;
        }

        private void TryAndReport(string actionDescription, ITestCase testCase, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                logger.LogError(testCase, "Error occured while {0} for test case {1}: {2}", actionDescription, testCase.DisplayName, ex);
            }
        }

        protected override bool Visit(IErrorMessage error)
        {
            ExecutionSummary.Errors++;

            logger.LogError("Catastrophic failure: {0}", ExceptionUtility.CombineMessages(error));

            return !cancelledThunk();
        }

        protected override bool Visit(ITestAssemblyFinished assemblyFinished)
        {
            ExecutionSummary.Failed = assemblyFinished.TestsFailed;
            ExecutionSummary.Skipped = assemblyFinished.TestsSkipped;
            ExecutionSummary.Time = assemblyFinished.ExecutionTime;
            ExecutionSummary.Total = assemblyFinished.TestsRun;

            return base.Visit(assemblyFinished);
        }

        protected override bool Visit(ITestFailed testFailed)
        {
            var result = MakeVsTestResult(TestOutcome.Failed, testFailed);
            if (result != null)
            {
                result.ErrorMessage = ExceptionUtility.CombineMessages(testFailed);
                result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(testFailed);

                TryAndReport("RecordResult (Fail)", testFailed.TestCase, () => recorder.RecordResult(result));
            }
            else
                logger.LogWarning(testFailed.TestCase, "(Fail) Could not find VS test case for {0} (ID = {1})", testFailed.TestCase.DisplayName, testFailed.TestCase.UniqueID);

            return !cancelledThunk();
        }

        protected override bool Visit(ITestPassed testPassed)
        {
            var result = MakeVsTestResult(TestOutcome.Passed, testPassed);
            if (result != null)
                TryAndReport("RecordResult (Pass)", testPassed.TestCase, () => recorder.RecordResult(result));
            else
                logger.LogWarning(testPassed.TestCase, "(Pass) Could not find VS test case for {0} (ID = {1})", testPassed.TestCase.DisplayName, testPassed.TestCase.UniqueID);

            return !cancelledThunk();
        }

        protected override bool Visit(ITestSkipped testSkipped)
        {
            var result = MakeVsTestResult(TestOutcome.Skipped, testSkipped);
            if (result != null)
                TryAndReport("RecordResult (Skip)", testSkipped.TestCase, () => recorder.RecordResult(result));
            else
                logger.LogWarning(testSkipped.TestCase, "(Skip) Could not find VS test case for {0} (ID = {1})", testSkipped.TestCase.DisplayName, testSkipped.TestCase.UniqueID);

            return !cancelledThunk();
        }

        protected override bool Visit(ITestCaseStarting testCaseStarting)
        {
            var vsTestCase = FindTestCase(testCaseStarting.TestCase);
            if (vsTestCase != null)
                TryAndReport("RecordStart", testCaseStarting.TestCase, () => recorder.RecordStart(vsTestCase));
            else
                logger.LogWarning(testCaseStarting.TestCase, "(Starting) Could not find VS test case for {0} (ID = {1})", testCaseStarting.TestCase.DisplayName, testCaseStarting.TestCase.UniqueID);

            return !cancelledThunk();
        }

        protected override bool Visit(ITestCaseFinished testCaseFinished)
        {
            var vsTestCase = FindTestCase(testCaseFinished.TestCase);
            if (vsTestCase != null)
                TryAndReport("RecordEnd", testCaseFinished.TestCase, () => recorder.RecordEnd(vsTestCase, TestOutcome.Passed));    // TODO: Don't have an aggregate outcome here!
            else
                logger.LogWarning(testCaseFinished.TestCase, "(Finished) Could not find VS test case for {0} (ID = {1})", testCaseFinished.TestCase.DisplayName, testCaseFinished.TestCase.UniqueID);

            return !cancelledThunk();
        }

        protected override bool Visit(ITestAssemblyCleanupFailure cleanupFailure)
        {
            ExecutionSummary.Errors++;

            return WriteError($"Test Assembly Cleanup Failure ({cleanupFailure.TestAssembly.Assembly.AssemblyPath})", cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestCaseCleanupFailure cleanupFailure)
        {
            ExecutionSummary.Errors++;

            return WriteError($"Test Case Cleanup Failure ({cleanupFailure.TestCase.DisplayName})", cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestClassCleanupFailure cleanupFailure)
        {
            ExecutionSummary.Errors++;

            return WriteError($"Test Class Cleanup Failure ({cleanupFailure.TestClass.Class.Name})", cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestCollectionCleanupFailure cleanupFailure)
        {
            ExecutionSummary.Errors++;

            return WriteError($"Test Collection Cleanup Failure ({cleanupFailure.TestCollection.DisplayName})", cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestCleanupFailure cleanupFailure)
        {
            ExecutionSummary.Errors++;

            return WriteError($"Test Cleanup Failure ({cleanupFailure.Test.DisplayName})", cleanupFailure, cleanupFailure.TestCases);
        }

        protected override bool Visit(ITestMethodCleanupFailure cleanupFailure)
        {
            ExecutionSummary.Errors++;

            return WriteError($"Test Method Cleanup Failure ({cleanupFailure.TestMethod.Method.Name})", cleanupFailure, cleanupFailure.TestCases);
        }

        protected bool WriteError(string failureName, IFailureInformation failureInfo, IEnumerable<ITestCase> testCases)
        {
            foreach (var testCase in testCases)
            {
                var result = MakeVsTestResult(TestOutcome.Failed, testCase, testCase.DisplayName);
                if (result != null)
                {
                    result.ErrorMessage = $"[{failureName}]: {ExceptionUtility.CombineMessages(failureInfo)}";
                    result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(failureInfo);

                    TryAndReport("RecordEnd (Failure)", testCase, () => recorder.RecordEnd(result.TestCase, result.Outcome));
                    TryAndReport("RecordResult (Failure)", testCase, () => recorder.RecordResult(result));
                }
                else
                    logger.LogWarning(testCase, "(Failure) Could not find VS test case for {0} (ID = {1})", testCase.DisplayName, testCase.UniqueID);
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
#if !PLATFORM_DOTNET
                ComputerName = Environment.MachineName,
#endif
                DisplayName = displayName,
                Duration = TimeSpan.FromSeconds(executionTime),
                Outcome = outcome,
            };

            // Work around VS considering a test "not run" when the duration is 0
            if (result.Duration.TotalMilliseconds == 0)
                result.Duration = TimeSpan.FromMilliseconds(1);

            if (!string.IsNullOrEmpty(output))
                result.Messages.Add(new VsTestResultMessage(VsTestResultMessage.StandardOutCategory, output));

            return result;
        }
    }
}