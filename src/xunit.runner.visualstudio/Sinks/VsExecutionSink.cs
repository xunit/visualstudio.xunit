using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Runner.Common;
using Xunit.Sdk;
using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;
using XunitTestResultMessage = Xunit.Sdk.TestResultMessage;

namespace Xunit.Runner.VisualStudio;

public sealed class VsExecutionSink : TestMessageSink, IDisposable
{
	readonly Func<bool> cancelledThunk;
	readonly LoggerHelper logger;
	readonly IMessageSink innerSink;
	readonly MessageMetadataCache metadataCache = new();
	readonly ITestExecutionRecorder recorder;
	readonly ConcurrentDictionary<string, List<TestCaseStarting>> testCasesByAssemblyID = [];
	readonly ConcurrentDictionary<string, TestCaseStarting> testCasesByCaseID = new();
	readonly ConcurrentDictionary<string, List<TestCaseStarting>> testCasesByClassID = [];
	readonly ConcurrentDictionary<string, List<TestCaseStarting>> testCasesByCollectionID = [];
	readonly ConcurrentDictionary<string, List<TestCaseStarting>> testCasesByMethodID = [];
	readonly Dictionary<string, TestCase> testCasesMap;

	public VsExecutionSink(
		IMessageSink innerSink,
		ITestExecutionRecorder recorder,
		LoggerHelper logger,
		Dictionary<string, TestCase> testCasesMap,
		Func<bool> cancelledThunk)
	{
		this.innerSink = innerSink;
		this.recorder = recorder;
		this.logger = logger;
		this.testCasesMap = testCasesMap;
		this.cancelledThunk = cancelledThunk;

		ExecutionSummary = new ExecutionSummary();

		Diagnostics.ErrorMessageEvent += HandleErrorMessage;

		// Test assemblies
		Execution.TestAssemblyCleanupFailureEvent += HandleTestAssemblyCleanupFailure;
		Execution.TestAssemblyFinishedEvent += HandleTestAssemblyFinished;
		Execution.TestAssemblyStartingEvent += HandleTestAssemblyStarting;

		// Test collections
		Execution.TestCollectionCleanupFailureEvent += HandleTestCollectionCleanupFailure;
		Execution.TestCollectionFinishedEvent += HandleTestCollectionFinished;
		Execution.TestCollectionStartingEvent += HandleTestCollectionStarting;

		// Test classes
		Execution.TestClassCleanupFailureEvent += HandleTestClassCleanupFailure;
		Execution.TestClassFinishedEvent += HandleTestClassFinished;
		Execution.TestClassStartingEvent += HandleTestClassStarting;

		// Test methods
		Execution.TestMethodCleanupFailureEvent += HandleTestMethodCleanupFailure;
		Execution.TestMethodFinishedEvent += HandleTestMethodFinished;
		Execution.TestMethodStartingEvent += HandleTestMethodStarting;

		// Test cases
		Execution.TestCaseCleanupFailureEvent += HandleTestCaseCleanupFailure;
		Execution.TestCaseFinishedEvent += HandleTestCaseFinished;
		Execution.TestCaseStartingEvent += HandleTestCaseStarting;

		// Tests
		Execution.TestCleanupFailureEvent += HandleTestCleanupFailure;
		Execution.TestFailedEvent += HandleTestFailed;
		Execution.TestFinishedEvent += HandleTestFinished;
		Execution.TestNotRunEvent += HandleTestNotRun;
		Execution.TestPassedEvent += HandleTestPassed;
		Execution.TestSkippedEvent += HandleTestSkipped;
		Execution.TestStartingEvent += HandleTestStarting;
	}

	public ExecutionSummary ExecutionSummary { get; private set; }

	public ManualResetEvent Finished { get; } = new ManualResetEvent(initialState: false);

	public void Dispose()
	{
		Finished.Dispose();
	}

	TestCase? FindTestCase(string testCaseUniqueID)
	{
		if (testCasesMap.TryGetValue(testCaseUniqueID, out var result))
			return result;

		var testCaseMetadata = metadataCache.TryGetTestCaseMetadata(testCaseUniqueID);
		var testCaseDisplayName = testCaseMetadata?.TestCaseDisplayName ?? "<unknown test case>";

		LogError(testCaseUniqueID, "Result reported for unknown test case: {0}", testCaseDisplayName);
		return null;
	}

	static TestOutcome GetAggregatedTestOutcome(TestCaseFinished testCaseFinished) =>
		testCaseFinished switch
		{
			{ TestsTotal: 0 } => TestOutcome.NotFound,
			{ TestsFailed: > 0 } => TestOutcome.Failed,
			{ TestsSkipped: > 0 } => TestOutcome.Skipped,
			_ => TestOutcome.Passed,
		};

	void HandleCancellation(MessageHandlerArgs args)
	{
		if (cancelledThunk())
			args.Stop();
	}

	void HandleErrorMessage(MessageHandlerArgs<ErrorMessage> args)
	{
		ExecutionSummary.Errors++;

		logger.LogError("Catastrophic failure: {0}", ExceptionUtility.CombineMessages(args.Message));

		HandleCancellation(args);
	}

	void HandleTestAssemblyCleanupFailure(MessageHandlerArgs<TestAssemblyCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByAssemblyID.TryGetValue(cleanupFailure.AssemblyUniqueID, out var testCases))
			WriteError($"Test Assembly Cleanup Failure ({TestAssemblyPath(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestAssemblyFinished(MessageHandlerArgs<TestAssemblyFinished> args)
	{
		var assemblyFinished = args.Message;

		testCasesByAssemblyID.TryRemove(assemblyFinished.AssemblyUniqueID, out _);

		try
		{
			ExecutionSummary.Failed = assemblyFinished.TestsFailed;
			ExecutionSummary.Skipped = assemblyFinished.TestsSkipped;
			ExecutionSummary.Time = assemblyFinished.ExecutionTime;
			ExecutionSummary.Total = assemblyFinished.TestsTotal;

			Finished.Set();

			HandleCancellation(args);
		}
		finally
		{
			metadataCache.TryRemove(assemblyFinished);
		}
	}

	void HandleTestAssemblyStarting(MessageHandlerArgs<TestAssemblyStarting> args) =>
		metadataCache.Set(args.Message);

	void HandleTestCaseCleanupFailure(MessageHandlerArgs<TestCaseCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByCaseID.TryGetValue(cleanupFailure.TestCaseUniqueID, out var testCase))
			WriteError($"Test Case Cleanup Failure ({TestCaseDisplayName(cleanupFailure)})", cleanupFailure, [testCase]);

		HandleCancellation(args);
	}

	void HandleTestCaseFinished(MessageHandlerArgs<TestCaseFinished> args)
	{
		var testCaseFinished = args.Message;

		testCasesByCaseID.TryRemove(testCaseFinished.TestCaseUniqueID, out _);

		try
		{
			var vsTestCase = FindTestCase(testCaseFinished.TestCaseUniqueID);
			if (vsTestCase is not null)
				TryAndReport("RecordEnd", testCaseFinished, () => recorder.RecordEnd(vsTestCase, GetAggregatedTestOutcome(testCaseFinished)));
			else
				LogWarning(testCaseFinished, "(Finished) Could not find VS test case for {0} (ID = {1})", TestCaseDisplayName(testCaseFinished), testCaseFinished.TestCaseUniqueID);

			HandleCancellation(args);
		}
		finally
		{
			metadataCache.TryRemove(testCaseFinished);
		}
	}

	void HandleTestCaseStarting(MessageHandlerArgs<TestCaseStarting> args)
	{
		var testCaseStarting = args.Message;
		metadataCache.Set(testCaseStarting);

		testCasesByAssemblyID.Add(testCaseStarting.AssemblyUniqueID, testCaseStarting);
		testCasesByCollectionID.Add(testCaseStarting.TestCollectionUniqueID, testCaseStarting);
		if (testCaseStarting.TestClassUniqueID is not null)
			testCasesByClassID.Add(testCaseStarting.TestClassUniqueID, testCaseStarting);
		if (testCaseStarting.TestMethodUniqueID is not null)
			testCasesByMethodID.Add(testCaseStarting.TestMethodUniqueID, testCaseStarting);
		testCasesByCaseID[testCaseStarting.TestCaseUniqueID] = testCaseStarting;

		var vsTestCase = FindTestCase(testCaseStarting.TestCaseUniqueID);
		if (vsTestCase is not null)
			TryAndReport("RecordStart", testCaseStarting, () => recorder.RecordStart(vsTestCase));
		else
			LogWarning(testCaseStarting, "(Starting) Could not find VS test case for {0} (ID = {1})", TestCaseDisplayName(testCaseStarting), testCaseStarting.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestClassCleanupFailure(MessageHandlerArgs<TestClassCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (cleanupFailure.TestClassUniqueID is not null && testCasesByClassID.TryGetValue(cleanupFailure.TestClassUniqueID, out var testCases))
			WriteError($"Test Class Cleanup Failure ({TestClassName(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestClassFinished(MessageHandlerArgs<TestClassFinished> args)
	{
		var classFinished = args.Message;
		if (classFinished.TestClassUniqueID is not null)
			testCasesByClassID.TryRemove(classFinished.TestClassUniqueID, out _);

		metadataCache.TryRemove(classFinished);
	}

	void HandleTestClassStarting(MessageHandlerArgs<TestClassStarting> args) =>
		metadataCache.Set(args.Message);

	void HandleTestCleanupFailure(MessageHandlerArgs<TestCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByCaseID.TryGetValue(cleanupFailure.TestCaseUniqueID, out var testCase))
			WriteError($"Test Cleanup Failure ({TestDisplayName(cleanupFailure)})", cleanupFailure, [testCase]);

		HandleCancellation(args);
	}

	void HandleTestCollectionCleanupFailure(MessageHandlerArgs<TestCollectionCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByCollectionID.TryGetValue(cleanupFailure.TestCollectionUniqueID, out var testCases))
			WriteError($"Test Collection Cleanup Failure ({TestCollectionDisplayName(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestCollectionFinished(MessageHandlerArgs<TestCollectionFinished> args)
	{
		var collectionFinished = args.Message;
		testCasesByCollectionID.TryRemove(collectionFinished.TestCollectionUniqueID, out _);

		metadataCache.TryRemove(collectionFinished);
	}

	void HandleTestCollectionStarting(MessageHandlerArgs<TestCollectionStarting> args) =>
		metadataCache.Set(args.Message);

	void HandleTestFailed(MessageHandlerArgs<TestFailed> args)
	{
		var testFailed = args.Message;
		var result = MakeVsTestResult(TestOutcome.Failed, testFailed);
		if (result is not null)
		{
			result.ErrorMessage = ExceptionUtility.CombineMessages(testFailed);
			result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(testFailed);

			TryAndReport("RecordResult (Fail)", testFailed, () => recorder.RecordResult(result));
		}
		else
			LogWarning(testFailed, "(Fail) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testFailed), testFailed.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestFinished(MessageHandlerArgs<TestFinished> args) =>
		metadataCache.TryRemove(args.Message);

	void HandleTestMethodCleanupFailure(MessageHandlerArgs<TestMethodCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (cleanupFailure.TestMethodUniqueID is not null && testCasesByMethodID.TryGetValue(cleanupFailure.TestMethodUniqueID, out var testCases))
			WriteError($"Test Method Cleanup Failure ({TestMethodName(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestMethodFinished(MessageHandlerArgs<TestMethodFinished> args)
	{
		var methodFinished = args.Message;
		if (methodFinished.TestMethodUniqueID is not null)
			testCasesByMethodID.TryRemove(methodFinished.TestMethodUniqueID, out _);

		metadataCache.TryRemove(methodFinished);
	}

	void HandleTestMethodStarting(MessageHandlerArgs<TestMethodStarting> args) =>
		metadataCache.Set(args.Message);

	void HandleTestNotRun(MessageHandlerArgs<TestNotRun> args)
	{
		var testNotRun = args.Message;
		var result = MakeVsTestResult(TestOutcome.None, testNotRun);
		if (result is not null)
			TryAndReport("RecordResult (None)", testNotRun, () => recorder.RecordResult(result));
		else
			LogWarning(testNotRun, "(NotRun) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testNotRun), testNotRun.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestPassed(MessageHandlerArgs<TestPassed> args)
	{
		var testPassed = args.Message;
		var result = MakeVsTestResult(TestOutcome.Passed, testPassed);
		if (result is not null)
			TryAndReport("RecordResult (Pass)", testPassed, () => recorder.RecordResult(result));
		else
			LogWarning(testPassed, "(Pass) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testPassed), testPassed.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestSkipped(MessageHandlerArgs<TestSkipped> args)
	{
		var testSkipped = args.Message;
		var result = MakeVsTestResult(TestOutcome.Skipped, testSkipped);
		if (result is not null)
			TryAndReport("RecordResult (Skip)", testSkipped, () => recorder.RecordResult(result));
		else
			LogWarning(testSkipped, "(Skip) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testSkipped), testSkipped.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestStarting(MessageHandlerArgs<TestStarting> args) =>
		metadataCache.Set(args.Message);

	//void LogError(
	//	TestAssemblyMessage msg,
	//	string format,
	//	params object?[] args) =>
	//		LogError(TestAssemblyPath(msg), format, args);

	void LogError(
		string assemblyPath,
		string format,
		params object?[] args) =>
			logger.SendMessage(TestMessageLevel.Error, assemblyPath, string.Format(format, args));

	public void LogWarning(
		TestAssemblyMessage msg,
		string format,
		params object?[] args) =>
			logger.SendMessage(TestMessageLevel.Warning, TestAssemblyPath(msg), string.Format(format, args));

	VsTestResult? MakeVsTestResult(
		TestOutcome outcome,
		XunitTestResultMessage testResult) =>
			MakeVsTestResult(outcome, testResult.TestCaseUniqueID, TestDisplayName(testResult), (double)testResult.ExecutionTime, testResult.Output);

	VsTestResult? MakeVsTestResult(
		TestOutcome outcome,
		TestSkipped skippedResult) =>
			MakeVsTestResult(outcome, skippedResult.TestCaseUniqueID, TestDisplayName(skippedResult), (double)skippedResult.ExecutionTime, errorMessage: skippedResult.Reason);

	VsTestResult? MakeVsTestResult(
		TestOutcome outcome,
		string testCaseUniqueID)
	{
		var testCaseMetadata = metadataCache.TryGetTestCaseMetadata(testCaseUniqueID);
		if (testCaseMetadata is null)
			return null;

		return MakeVsTestResult(outcome, testCaseUniqueID, testCaseMetadata.TestCaseDisplayName);
	}

	VsTestResult? MakeVsTestResult(
		TestOutcome outcome,
		string testCaseUniqueID,
		string displayName,
		double executionTime = 0.0,
		string? output = null,
		string? errorMessage = null)
	{
		var vsTestCase = FindTestCase(testCaseUniqueID);
		if (vsTestCase is null)
			return null;

		var result = new VsTestResult(vsTestCase)
		{
			ComputerName = Environment.MachineName,
			DisplayName = displayName,
			Duration = TimeSpan.FromSeconds(executionTime),
			Outcome = outcome,
		};

		// Work around VS considering a test "not run" when the duration is 0
		if (result.Duration.TotalMilliseconds == 0)
			result.Duration = TimeSpan.FromMilliseconds(1);

		if (!string.IsNullOrEmpty(output))
			result.Messages.Add(new VsTestResultMessage(VsTestResultMessage.StandardOutCategory, output));

		if (!string.IsNullOrEmpty(errorMessage))
			result.ErrorMessage = errorMessage;

		return result;
	}

	public override bool OnMessage(MessageSinkMessage message)
	{
		var result = innerSink.OnMessage(message);
		return base.OnMessage(message) && result;
	}

	string TestAssemblyPath(TestAssemblyMessage msg) =>
		metadataCache.TryGetAssemblyMetadata(msg)?.AssemblyPath ?? $"<unknown test assembly ID {msg.AssemblyUniqueID}>";

	string TestCaseDisplayName(TestCaseMessage msg) =>
		metadataCache.TryGetTestCaseMetadata(msg)?.TestCaseDisplayName ?? $"<unknown test case ID {msg.TestCaseUniqueID}>";

	string TestClassName(TestClassMessage msg) =>
		metadataCache.TryGetClassMetadata(msg)?.TestClassName ?? $"<unknown test class ID {msg.TestClassUniqueID}>";

	string TestCollectionDisplayName(TestCollectionMessage msg) =>
		metadataCache.TryGetCollectionMetadata(msg)?.TestCollectionDisplayName ?? $"<unknown test collection ID {msg.TestCollectionUniqueID}>";

	string TestDisplayName(TestMessage msg) =>
		metadataCache.TryGetTestMetadata(msg)?.TestDisplayName ?? $"<unknown test ID {msg.TestUniqueID}>";

	string TestMethodName(TestMethodMessage msg) =>
		TestClassName(msg) + "." + metadataCache.TryGetMethodMetadata(msg)?.MethodName ?? $"<unknown test method ID {msg.TestMethodUniqueID}>";

	void TryAndReport(
		string actionDescription,
		TestCaseMessage testCase,
		Action action)
	{
		var testCaseMetadata = metadataCache.TryGetTestCaseMetadata(testCase);
		var testCaseDisplayName = testCaseMetadata?.TestCaseDisplayName ?? $"<unknown test case ID {testCase.TestCaseUniqueID}>";

		var testAssemblyMetadata = metadataCache.TryGetAssemblyMetadata(testCase);
		var assemblyPath = testAssemblyMetadata?.AssemblyPath ?? $"<unknown assembly ID {testCase.AssemblyUniqueID}>";

		TryAndReport(actionDescription, testCaseDisplayName, assemblyPath, action);
	}

	void TryAndReport(
		string actionDescription,
		string testCaseDisplayName,
		string assemblyPath,
		Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			LogError(assemblyPath, "Error occured while {0} for test case {1}: {2}", actionDescription, testCaseDisplayName, ex);
		}
	}

	void WriteError(
		string failureName,
		IErrorMetadata errorMetadata,
		IEnumerable<TestCaseStarting> testCases)
	{
		foreach (var testCase in testCases)
		{
			var result = MakeVsTestResult(TestOutcome.Failed, testCase.TestCaseUniqueID);
			if (result is null)
				continue;

			result.ErrorMessage = $"[{failureName}]: {ExceptionUtility.CombineMessages(errorMetadata)}";
			result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(errorMetadata);

			TryAndReport("RecordEnd (Failure)", testCase, () => recorder.RecordEnd(result.TestCase, result.Outcome));
			TryAndReport("RecordResult (Failure)", testCase, () => recorder.RecordResult(result));
		}
	}
}
