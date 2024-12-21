using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Xunit.Runner.Common;
using Xunit.Sdk;
using VsTestCase = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestCase;
using VsTestCaseDiscoverySink = Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter.ITestCaseDiscoverySink;

#if NETCOREAPP
using System.Reflection;
#endif

namespace Xunit.Runner.VisualStudio;

internal sealed class VsDiscoverySink : IVsDiscoverySink, IDisposable
{
	static readonly string Ellipsis = new((char)183, 3);
	const int MaximumDisplayNameLength = 447;
	const int TestCaseBatchSize = 100;

	static readonly Uri uri = new(Constants.ExecutorUri);

	readonly Func<bool> cancelThunk;
	readonly ITestFrameworkDiscoveryOptions discoveryOptions;
	readonly VsTestCaseDiscoverySink discoverySink;
	readonly DiscoveryEventSink discoveryEventSink = new();
	readonly LoggerHelper logger;
	readonly string source;
	readonly List<ITestCaseDiscovered> testCaseBatch = [];
	readonly TestPlatformContext testPlatformContext;
	readonly TestCaseFilter testCaseFilter;

	public VsDiscoverySink(
		string source,
		LoggerHelper logger,
		VsTestCaseDiscoverySink discoverySink,
		ITestFrameworkDiscoveryOptions discoveryOptions,
		TestPlatformContext testPlatformContext,
		TestCaseFilter testCaseFilter,
		Func<bool> cancelThunk)
	{
		this.source = source;
		this.logger = logger;
		this.discoverySink = discoverySink;
		this.discoveryOptions = discoveryOptions;
		this.testPlatformContext = testPlatformContext;
		this.testCaseFilter = testCaseFilter;
		this.cancelThunk = cancelThunk;

		discoveryEventSink.TestCaseDiscoveredEvent += HandleTestCaseDiscoveredMessage;
		discoveryEventSink.DiscoveryCompleteEvent += HandleDiscoveryCompleteMessage;
	}

	public ManualResetEvent Finished { get; } = new ManualResetEvent(initialState: false);

	public int TotalTests { get; private set; }

	public void Dispose() =>
		Finished.Dispose();

	public static VsTestCase? CreateVsTestCase(
		string source,
		ITestCaseDiscovered testCase,
		LoggerHelper logger,
		TestPlatformContext testPlatformContext)
	{
		if (testCase.TestClassName is null)
		{
			logger.LogErrorWithSource(source, "Error creating Visual Studio test case for {0}: TestClassName is null", testCase.TestCaseDisplayName);
			return null;
		}

		try
		{
			var fqTestMethodName = $"{testCase.TestClassName}.{testCase.TestMethodName}";
			var result = new VsTestCase(fqTestMethodName, uri, source) { DisplayName = Escape(testCase.TestCaseDisplayName) };

			// TODO: Waiting for feedback from https://github.com/xunit/xunit/issues/3023 to understand how this actually supposed
			// to be done. Right now it appears that:
			//   (a) method lookups across projects absolutely requires parameter types
			//   (b) method lookups in the same project do not require parameter types
			//   (c) generic parameter types break lookup in both cases
			// which leads us to the convoluted logic here, which is that we'll add parameter types unless they contain generics, in
			// the hopes that that gives us the best possible coverage.
			var managedMethodName = testCase.TestMethodName;
			if (testCase.TestMethodParameterTypesVSTest is not null && testCase.TestMethodParameterTypesVSTest.Length > 0)
				managedMethodName = string.Format(CultureInfo.InvariantCulture, "{0}({1})", managedMethodName, string.Join(",", testCase.TestMethodParameterTypesVSTest));

			result.SetPropertyValue(VsTestRunner.TestCaseUniqueIDProperty, testCase.TestCaseUniqueID);
			result.SetPropertyValue(VsTestRunner.TestCaseExplicitProperty, testCase.Explicit);
			result.SetPropertyValue(VsTestRunner.ManagedTypeProperty, testCase.TestClassName);
			result.SetPropertyValue(VsTestRunner.ManagedMethodProperty, managedMethodName);

			if (testCase.SkipReason is not null)
				result.SetPropertyValue(VsTestRunner.SkipReasonProperty, testCase.SkipReason);

			if (testPlatformContext.DesignMode)
				result.SetPropertyValue(VsTestRunner.TestCaseSerializationProperty, testCase.Serialization);

			result.Id = GuidFromString(uri + testCase.TestCaseUniqueID);
			result.CodeFilePath = testCase.SourceFilePath;
			result.LineNumber = testCase.SourceLineNumber.GetValueOrDefault();

			var traits = testCase.Traits;
			foreach (var key in traits.Keys)
				foreach (var value in traits[key])
					result.Traits.Add(key, value);

			return result;
		}
		catch (Exception ex)
		{
			logger.LogErrorWithSource(source, "Error creating Visual Studio test case for {0}: {1}", testCase.TestCaseDisplayName, ex);
			return null;
		}
	}

	static string Escape(string value)
	{
		if (value is null)
			return string.Empty;

		return Truncate(value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t"));
	}

	static string Truncate(string value)
	{
		if (value.Length <= MaximumDisplayNameLength)
			return value;

		return value.Substring(0, MaximumDisplayNameLength - Ellipsis.Length) + Ellipsis;
	}

	public int Finish()
	{
		Finished.WaitOne();
		return TotalTests;
	}

	void HandleCancellation(MessageHandlerArgs args)
	{
		if (cancelThunk())
			args.Stop();
	}

	void HandleTestCaseDiscoveredMessage(MessageHandlerArgs<ITestCaseDiscovered> args)
	{
		testCaseBatch.Add(args.Message);
		TotalTests++;

		if (testCaseBatch.Count == TestCaseBatchSize)
			SendExistingTestCases();

		HandleCancellation(args);
	}

	void HandleDiscoveryCompleteMessage(MessageHandlerArgs<IDiscoveryComplete> args)
	{
		try
		{
			SendExistingTestCases();
		}
		finally
		{
			// Set test discovery complete despite any potential issues
			// with sending test cases over. This would avoid causing test discovery to hang for the entire session.
			Finished.Set();
		}

		HandleCancellation(args);
	}

	bool IMessageSink.OnMessage(IMessageSinkMessage message) =>
		discoveryEventSink.OnMessage(message);

	private void SendExistingTestCases()
	{
		if (testCaseBatch.Count == 0)
			return;

		foreach (var testCase in testCaseBatch)
		{
			var vsTestCase = CreateVsTestCase(source, testCase, logger, testPlatformContext);
			if (vsTestCase is not null && testCaseFilter.MatchTestCase(vsTestCase))
			{
				if (discoveryOptions.GetInternalDiagnosticMessagesOrDefault())
					logger.LogWithSource(source, "Discovered test case '{0}' (ID = '{1}', VS FQN = '{2}')", testCase.TestCaseDisplayName, testCase.TestCaseUniqueID, vsTestCase.FullyQualifiedName);

				discoverySink.SendTestCase(vsTestCase);
			}
		}

		testCaseBatch.Clear();
	}

	readonly static HashAlgorithm Hasher = SHA1.Create();

	static Guid GuidFromString(string data)
	{
		var hash = Hasher.ComputeHash(Encoding.Unicode.GetBytes(data));
		var b = new byte[16];
		Array.Copy(hash, b, 16);
		return new Guid(b);
	}
}
