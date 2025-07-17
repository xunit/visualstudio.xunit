using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit.Runner.Common;
using Xunit.Sdk;
using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;
using VsTestExecutionRecorder = Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter.ITestExecutionRecorder;
using VsTestMessageLevel = Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging.TestMessageLevel;
using VsTestOutcome = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestOutcome;
using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;
using VsTestResultMessage = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResultMessage;
using XunitTestResultMessage = Xunit.Sdk.ITestResultMessage;

namespace Xunit.Runner.VisualStudio;

internal sealed class VsExecutionSink : TestMessageSink
{
	static readonly HashSet<char> InvalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

	readonly Func<bool> cancelledThunk;
	readonly LoggerHelper logger;
	readonly IMessageSink innerSink;
	readonly ConcurrentDictionary<string, MessageMetadataCache> metadataCacheByAssemblyID = [];
	readonly VsTestExecutionRecorder recorder;
	readonly ConcurrentDictionary<string, DateTimeOffset> startTimeByTestID = [];
	readonly ConcurrentDictionary<string, List<ITestCaseStarting>> testCasesByAssemblyID = [];
	readonly ConcurrentDictionary<string, ITestCaseStarting> testCasesByCaseID = [];
	readonly ConcurrentDictionary<string, (string actionDescription, ITestMessage test, VsTestResult testResult)> testResultByCaseID = [];
	readonly ConcurrentDictionary<string, List<ITestCaseStarting>> testCasesByClassID = [];
	readonly ConcurrentDictionary<string, List<ITestCaseStarting>> testCasesByCollectionID = [];
	readonly ConcurrentDictionary<string, List<ITestCaseStarting>> testCasesByMethodID = [];
	readonly IReadOnlyDictionary<string, VsTestCase> testCasesMap;
	static readonly Uri uri = new(Constants.ExecutorUri);

	public VsExecutionSink(
		IMessageSink innerSink,
		VsTestExecutionRecorder recorder,
		LoggerHelper logger,
		IReadOnlyDictionary<string, VsTestCase> testCasesMap,
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

	VsTestCase? FindTestCase(
		string testCaseUniqueID,
		string testAssemblyUniqueID)
	{
		if (testCasesMap.TryGetValue(testCaseUniqueID, out var result))
			return result;

		var testCaseMetadata = MetadataCache(testAssemblyUniqueID)?.TryGetTestCaseMetadata(testCaseUniqueID);
		var testCaseDisplayName = testCaseMetadata?.TestCaseDisplayName ?? "<unknown test case>";

		LogError(testCaseUniqueID, "Result reported for unknown test case: {0}", testCaseDisplayName);
		return null;
	}

	static VsTestOutcome GetAggregatedTestOutcome(ITestCaseFinished testCaseFinished) =>
		testCaseFinished switch
		{
			{ TestsTotal: 0 } => VsTestOutcome.NotFound,
			{ TestsFailed: > 0 } => VsTestOutcome.Failed,
			{ TestsSkipped: > 0 } => VsTestOutcome.Skipped,
			_ => VsTestOutcome.Passed,
		};

	void HandleCancellation(MessageHandlerArgs args)
	{
		if (cancelledThunk())
			args.Stop();
	}

	void HandleErrorMessage(MessageHandlerArgs<IErrorMessage> args)
	{
		ExecutionSummary.Errors++;

		logger.LogError("Catastrophic failure: {0}", ExceptionUtility.CombineMessages(args.Message));

		HandleCancellation(args);
	}

	void HandleTestAssemblyCleanupFailure(MessageHandlerArgs<ITestAssemblyCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByAssemblyID.TryGetValue(cleanupFailure.AssemblyUniqueID, out var testCases))
			WriteError($"Test Assembly Cleanup Failure ({TestAssemblyPath(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestAssemblyFinished(MessageHandlerArgs<ITestAssemblyFinished> args)
	{
		var assemblyFinished = args.Message;

		testCasesByAssemblyID.TryRemove(assemblyFinished.AssemblyUniqueID, out _);

		try
		{
			ExecutionSummary.Failed = assemblyFinished.TestsFailed;
			ExecutionSummary.Skipped = assemblyFinished.TestsSkipped;
			ExecutionSummary.Time = assemblyFinished.ExecutionTime;
			ExecutionSummary.Total = assemblyFinished.TestsTotal;

			HandleCancellation(args);
		}
		finally
		{
			metadataCacheByAssemblyID.TryRemove(assemblyFinished.AssemblyUniqueID, out _);
		}
	}

	void HandleTestAssemblyStarting(MessageHandlerArgs<ITestAssemblyStarting> args)
	{
		var assemblyStarting = args.Message;
		var cache = new MessageMetadataCache();

		metadataCacheByAssemblyID[assemblyStarting.AssemblyUniqueID] = cache;
		cache.Set(args.Message);
	}

	void HandleTestCaseCleanupFailure(MessageHandlerArgs<ITestCaseCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByCaseID.TryGetValue(cleanupFailure.TestCaseUniqueID, out var testCase))
			WriteError($"Test Case Cleanup Failure ({TestCaseDisplayName(cleanupFailure)})", cleanupFailure, [testCase]);

		HandleCancellation(args);
	}

	void HandleTestCaseFinished(MessageHandlerArgs<ITestCaseFinished> args)
	{
		var testCaseFinished = args.Message;
		testCasesByCaseID.TryRemove(testCaseFinished.TestCaseUniqueID, out _);

		try
		{
			var vsTestCase = FindTestCase(testCaseFinished.TestCaseUniqueID, testCaseFinished.AssemblyUniqueID);
			if (vsTestCase is not null)
				TryAndReport("RecordEnd", testCaseFinished, () => recorder.RecordEnd(vsTestCase, GetAggregatedTestOutcome(testCaseFinished)));
			else
				LogWarning(testCaseFinished, "(Finished) Could not find VS test case for {0} (ID = {1})", TestCaseDisplayName(testCaseFinished), testCaseFinished.TestCaseUniqueID);

			HandleCancellation(args);
		}
		finally
		{
			MetadataCache(testCaseFinished)?.TryRemove(testCaseFinished);
		}
	}

	void HandleTestCaseStarting(MessageHandlerArgs<ITestCaseStarting> args)
	{
		var testCaseStarting = args.Message;

		MetadataCache(testCaseStarting)?.Set(testCaseStarting);

		testCasesByAssemblyID.Add(testCaseStarting.AssemblyUniqueID, testCaseStarting);
		testCasesByCollectionID.Add(testCaseStarting.TestCollectionUniqueID, testCaseStarting);
		if (testCaseStarting.TestClassUniqueID is not null)
			testCasesByClassID.Add(testCaseStarting.TestClassUniqueID, testCaseStarting);
		if (testCaseStarting.TestMethodUniqueID is not null)
			testCasesByMethodID.Add(testCaseStarting.TestMethodUniqueID, testCaseStarting);
		testCasesByCaseID[testCaseStarting.TestCaseUniqueID] = testCaseStarting;

		var vsTestCase = FindTestCase(testCaseStarting.TestCaseUniqueID, testCaseStarting.AssemblyUniqueID);
		if (vsTestCase is not null)
			TryAndReport("RecordStart", testCaseStarting, () => recorder.RecordStart(vsTestCase));
		else
			LogWarning(testCaseStarting, "(Starting) Could not find VS test case for {0} (ID = {1})", TestCaseDisplayName(testCaseStarting), testCaseStarting.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestClassCleanupFailure(MessageHandlerArgs<ITestClassCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (cleanupFailure.TestClassUniqueID is not null && testCasesByClassID.TryGetValue(cleanupFailure.TestClassUniqueID, out var testCases))
			WriteError($"Test Class Cleanup Failure ({TestClassName(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestClassFinished(MessageHandlerArgs<ITestClassFinished> args)
	{
		var classFinished = args.Message;
		if (classFinished.TestClassUniqueID is not null)
			testCasesByClassID.TryRemove(classFinished.TestClassUniqueID, out _);

		MetadataCache(classFinished)?.TryRemove(classFinished);
	}

	void HandleTestClassStarting(MessageHandlerArgs<ITestClassStarting> args) =>
		MetadataCache(args.Message)?.Set(args.Message);

	void HandleTestCleanupFailure(MessageHandlerArgs<ITestCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByCaseID.TryGetValue(cleanupFailure.TestCaseUniqueID, out var testCase))
			WriteError($"Test Cleanup Failure ({TestDisplayName(cleanupFailure)})", cleanupFailure, [testCase]);

		HandleCancellation(args);
	}

	void HandleTestCollectionCleanupFailure(MessageHandlerArgs<ITestCollectionCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (testCasesByCollectionID.TryGetValue(cleanupFailure.TestCollectionUniqueID, out var testCases))
			WriteError($"Test Collection Cleanup Failure ({TestCollectionDisplayName(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestCollectionFinished(MessageHandlerArgs<ITestCollectionFinished> args)
	{
		var collectionFinished = args.Message;
		testCasesByCollectionID.TryRemove(collectionFinished.TestCollectionUniqueID, out _);

		MetadataCache(collectionFinished)?.TryRemove(collectionFinished);
	}

	void HandleTestCollectionStarting(MessageHandlerArgs<ITestCollectionStarting> args) =>
		MetadataCache(args.Message)?.Set(args.Message);

	void HandleTestFailed(MessageHandlerArgs<ITestFailed> args)
	{
		var testFailed = args.Message;
		startTimeByTestID.TryRemove(testFailed.TestUniqueID, out var startTime);

		var result = MakeVsTestResult(VsTestOutcome.Failed, testFailed, startTime);
		if (result is not null)
		{
			result.ErrorMessage = ExceptionUtility.CombineMessages(testFailed);
			result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(testFailed);

			DeferReportUntilTestFinished("RecordResult (Fail)", testFailed, result);
		}
		else
			LogWarning(testFailed, "(Fail) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testFailed), testFailed.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestFinished(MessageHandlerArgs<ITestFinished> args)
	{
		var testUniqueID = args.Message.TestUniqueID;

		if (testResultByCaseID.TryRemove(testUniqueID, out var testResultEntry))
		{
			var (actionDescription, test, testResult) = testResultEntry;
			var attachments = args.Message.Attachments;

			if (attachments.Count != 0)
				try
				{
					var basePath = Path.Combine(Path.GetTempPath(), testUniqueID);
					Directory.CreateDirectory(basePath);

					var attachmentSet = new AttachmentSet(uri, "xUnit.net");

					foreach (var kvp in attachments)
					{
						var localFilePath = Path.Combine(basePath, SanitizeFileName(kvp.Key));

						try
						{
							var attachmentType = kvp.Value.AttachmentType;

							if (attachmentType == TestAttachmentType.String)
							{
								localFilePath += ".txt";
								File.WriteAllText(localFilePath, kvp.Value.AsString());
							}
							else if (attachmentType == TestAttachmentType.ByteArray)
							{
								var (byteArray, mediaType) = kvp.Value.AsByteArray();
								localFilePath += MediaTypeUtility.GetFileExtension(mediaType);
								File.WriteAllBytes(localFilePath, byteArray);
							}
							else
							{
								LogWarning(test, "Unknown test attachment type '{0}' for attachment '{1}' [test case ID '{2}']", attachmentType, kvp.Key, testUniqueID);
								localFilePath = null;
							}

							if (localFilePath is not null)
								attachmentSet.Attachments.Add(UriDataAttachment.CreateFrom(localFilePath, kvp.Key));
						}
						catch (Exception ex)
						{
							LogWarning(test, "Exception while adding attachment '{0}' in '{1}' [test case ID '{2}']: {3}", kvp.Key, localFilePath, testUniqueID, ex);
						}
					}

					testResult.Attachments.Add(attachmentSet);
				}
				catch (Exception ex)
				{
					LogWarning(test, "Exception while adding attachments [test case ID '{0}']: {1}", testUniqueID, ex);
				}

			TryAndReport(actionDescription, test, () => recorder.RecordResult(testResult));
		}

		MetadataCache(args.Message)?.TryRemove(args.Message);
	}

	void HandleTestMethodCleanupFailure(MessageHandlerArgs<ITestMethodCleanupFailure> args)
	{
		ExecutionSummary.Errors++;

		var cleanupFailure = args.Message;

		if (cleanupFailure.TestMethodUniqueID is not null && testCasesByMethodID.TryGetValue(cleanupFailure.TestMethodUniqueID, out var testCases))
			WriteError($"Test Method Cleanup Failure ({TestMethodName(cleanupFailure)})", cleanupFailure, testCases);

		HandleCancellation(args);
	}

	void HandleTestMethodFinished(MessageHandlerArgs<ITestMethodFinished> args)
	{
		var methodFinished = args.Message;
		if (methodFinished.TestMethodUniqueID is not null)
			testCasesByMethodID.TryRemove(methodFinished.TestMethodUniqueID, out _);

		MetadataCache(methodFinished)?.TryRemove(methodFinished);
	}

	void HandleTestMethodStarting(MessageHandlerArgs<ITestMethodStarting> args) =>
		MetadataCache(args.Message)?.Set(args.Message);

	void HandleTestNotRun(MessageHandlerArgs<ITestNotRun> args)
	{
		var testNotRun = args.Message;
		startTimeByTestID.TryRemove(testNotRun.TestUniqueID, out var startTime);

		var result = MakeVsTestResult(VsTestOutcome.None, testNotRun, startTime);
		if (result is not null)
			DeferReportUntilTestFinished("RecordResult (None)", testNotRun, result);
		else
			LogWarning(testNotRun, "(NotRun) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testNotRun), testNotRun.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestPassed(MessageHandlerArgs<ITestPassed> args)
	{
		var testPassed = args.Message;
		startTimeByTestID.TryRemove(testPassed.TestUniqueID, out var startTime);

		var result = MakeVsTestResult(VsTestOutcome.Passed, testPassed, startTime);
		if (result is not null)
			DeferReportUntilTestFinished("RecordResult (Pass)", testPassed, result);
		else
			LogWarning(testPassed, "(Pass) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testPassed), testPassed.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestSkipped(MessageHandlerArgs<ITestSkipped> args)
	{
		var testSkipped = args.Message;
		startTimeByTestID.TryRemove(testSkipped.TestUniqueID, out var startTime);

		var result = MakeVsTestResult(VsTestOutcome.Skipped, testSkipped, startTime);
		if (result is not null)
			DeferReportUntilTestFinished("RecordResult (Skip)", testSkipped, result);
		else
			LogWarning(testSkipped, "(Skip) Could not find VS test case for {0} (ID = {1})", TestDisplayName(testSkipped), testSkipped.TestCaseUniqueID);

		HandleCancellation(args);
	}

	void HandleTestStarting(MessageHandlerArgs<ITestStarting> args)
	{
		var testStarting = args.Message;
		MetadataCache(testStarting)?.Set(testStarting);

		startTimeByTestID.TryAdd(testStarting.TestUniqueID, testStarting.StartTime);
	}

	void LogError(
		string assemblyPath,
		string format,
		params object?[] args) =>
			logger.SendMessage(VsTestMessageLevel.Error, assemblyPath, format, args);

	public void LogWarning(
		ITestAssemblyMessage msg,
		string format,
		params object?[] args) =>
			logger.SendMessage(VsTestMessageLevel.Warning, TestAssemblyPath(msg), format, args);

	VsTestResult? MakeVsTestResult(
		VsTestOutcome outcome,
		XunitTestResultMessage testResult,
		DateTimeOffset? startTime) =>
			MakeVsTestResult(outcome, testResult.TestCaseUniqueID, testResult.AssemblyUniqueID, TestDisplayName(testResult), (double)testResult.ExecutionTime, testResult.Output, startTime: startTime, finishTime: testResult.FinishTime);

	VsTestResult? MakeVsTestResult(
		VsTestOutcome outcome,
		ITestSkipped skippedResult,
		DateTimeOffset? startTime) =>
			MakeVsTestResult(outcome, skippedResult.TestCaseUniqueID, skippedResult.AssemblyUniqueID, TestDisplayName(skippedResult), (double)skippedResult.ExecutionTime, errorMessage: skippedResult.Reason, startTime: startTime, finishTime: skippedResult.FinishTime);

	VsTestResult? MakeVsTestResult(
		VsTestOutcome outcome,
		string testCaseUniqueID,
		string testAssemblyUniqueID)
	{
		var testCaseMetadata = MetadataCache(testAssemblyUniqueID)?.TryGetTestCaseMetadata(testCaseUniqueID);
		if (testCaseMetadata is null)
			return null;

		return MakeVsTestResult(outcome, testCaseUniqueID, testAssemblyUniqueID, testCaseMetadata.TestCaseDisplayName);
	}

	VsTestResult? MakeVsTestResult(
		VsTestOutcome outcome,
		string testCaseUniqueID,
		string testAssemblyUniqueID,
		string displayName,
		double executionTime = 0.0,
		string? output = null,
		string? errorMessage = null,
		DateTimeOffset? startTime = null,
		DateTimeOffset? finishTime = null)
	{
		var vsTestCase = FindTestCase(testCaseUniqueID, testAssemblyUniqueID);
		if (vsTestCase is null)
			return null;

		var result = new VsTestResult(vsTestCase)
		{
			ComputerName = Environment.MachineName,
			DisplayName = displayName,
			Duration = TimeSpan.FromSeconds(executionTime),
			Outcome = outcome,
		};

		if (startTime.HasValue && finishTime.HasValue)
		{
			result.StartTime = startTime.Value;
			result.EndTime = finishTime.Value;
		}

		// Work around VS considering a test "not run" when the duration is 0
		if (result.Duration.TotalMilliseconds == 0)
			result.Duration = TimeSpan.FromMilliseconds(1);

		if (!string.IsNullOrEmpty(output))
			result.Messages.Add(new VsTestResultMessage(VsTestResultMessage.StandardOutCategory, output));

		if (!string.IsNullOrEmpty(errorMessage))
			result.ErrorMessage = errorMessage;

		return result;
	}

	MessageMetadataCache? MetadataCache(ITestAssemblyMessage testAssemblyMessage) =>
		MetadataCache(testAssemblyMessage.AssemblyUniqueID);

	MessageMetadataCache? MetadataCache(string testAssemblyUniqueID)
	{
		metadataCacheByAssemblyID.TryGetValue(testAssemblyUniqueID, out var metadataCache);
		return metadataCache;
	}

	public override bool OnMessage(IMessageSinkMessage message)
	{
		var result = innerSink.OnMessage(message);
		return base.OnMessage(message) && result;
	}

	string TestAssemblyPath(ITestAssemblyMessage msg) =>
		MetadataCache(msg)?.TryGetAssemblyMetadata(msg)?.AssemblyPath ?? $"<unknown test assembly ID {msg.AssemblyUniqueID}>";

	string TestCaseDisplayName(ITestCaseMessage msg) =>
		MetadataCache(msg)?.TryGetTestCaseMetadata(msg)?.TestCaseDisplayName ?? $"<unknown test case ID {msg.TestCaseUniqueID}>";

	string TestClassName(ITestClassMessage msg) =>
		MetadataCache(msg)?.TryGetClassMetadata(msg)?.TestClassName ?? $"<unknown test class ID {msg.TestClassUniqueID}>";

	string TestCollectionDisplayName(ITestCollectionMessage msg) =>
		MetadataCache(msg)?.TryGetCollectionMetadata(msg)?.TestCollectionDisplayName ?? $"<unknown test collection ID {msg.TestCollectionUniqueID}>";

	string TestDisplayName(ITestMessage msg) =>
		MetadataCache(msg)?.TryGetTestMetadata(msg)?.TestDisplayName ?? $"<unknown test ID {msg.TestUniqueID}>";

	string TestMethodName(ITestMethodMessage msg) =>
		TestClassName(msg) + "." + MetadataCache(msg)?.TryGetMethodMetadata(msg)?.MethodName ?? $"<unknown test method ID {msg.TestMethodUniqueID}>";

	void DeferReportUntilTestFinished(
		string actionDescription,
		ITestMessage test,
		VsTestResult testResult) =>
			testResultByCaseID.TryAdd(test.TestUniqueID, (actionDescription, test, testResult));

	static string SanitizeFileName(string fileName)
	{
		var result = new StringBuilder(fileName.Length);

		foreach (var c in fileName)
			if (InvalidFileNameChars.Contains(c))
				result.Append('_');
			else
				result.Append(c);

		return result.ToString();
	}

	void TryAndReport(
		string actionDescription,
		ITestCaseMessage testCase,
		Action action)
	{
		var metadataCache = MetadataCache(testCase);
		var testCaseMetadata = metadataCache?.TryGetTestCaseMetadata(testCase);
		var testCaseDisplayName = testCaseMetadata?.TestCaseDisplayName ?? $"<unknown test case ID {testCase.TestCaseUniqueID}>";

		var testAssemblyMetadata = metadataCache?.TryGetAssemblyMetadata(testCase);
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
		IEnumerable<ITestCaseStarting> testCases)
	{
		foreach (var testCase in testCases)
		{
			var result = MakeVsTestResult(VsTestOutcome.Failed, testCase.TestCaseUniqueID, testCase.AssemblyUniqueID);
			if (result is null)
				continue;

			result.ErrorMessage = $"[{failureName}]: {ExceptionUtility.CombineMessages(errorMetadata)}";
			result.ErrorStackTrace = ExceptionUtility.CombineStackTraces(errorMetadata);

			TryAndReport("RecordEnd (Failure)", testCase, () => recorder.RecordEnd(result.TestCase, result.Outcome));
			TryAndReport("RecordResult (Failure)", testCase, () => recorder.RecordResult(result));
		}
	}
}
