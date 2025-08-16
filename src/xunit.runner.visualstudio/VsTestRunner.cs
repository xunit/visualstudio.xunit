using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Internal;
using Xunit.Runner.Common;
using Xunit.Sdk;
using Xunit.v3;

namespace Xunit.Runner.VisualStudio
{
	[FileExtension(".dll")]
	[FileExtension(".exe")]
	[DefaultExecutorUri(Constants.ExecutorUri)]
	[ExtensionUri(Constants.ExecutorUri)]
	[Category("managed")]
	public class VsTestRunner : ITestDiscoverer, ITestExecutor
	{
		bool cancelled;
		static int printedHeader = 0;

#if NETCOREAPP
		static readonly AppDomainSupport AppDomainDefaultBehavior = AppDomainSupport.Denied;
#else
		static readonly AppDomainSupport AppDomainDefaultBehavior = AppDomainSupport.Required;
#endif

		static readonly HashSet<string> PlatformAssemblies = new(StringComparer.OrdinalIgnoreCase)
		{
			// VSTest
			"microsoft.visualstudio.testplatform.unittestframework.dll",
			"microsoft.visualstudio.testplatform.core.dll",
			"microsoft.visualstudio.testplatform.testexecutor.core.dll",
			"microsoft.visualstudio.testplatform.extensions.msappcontaineradapter.dll",
			"microsoft.visualstudio.testplatform.objectmodel.dll",
			"microsoft.visualstudio.testplatform.utilities.dll",
			"vstest.executionengine.appcontainer.exe",
			"vstest.executionengine.appcontainer.x86.exe",

			// xUnit.net VSTest adapter
			"xunit.runner.visualstudio.testadapter.dll",
			"xunit.runner.visualstudio.dotnetcore.testadapter.dll",

			// xUnit.net runner (console)
			"xunit.console.clr4.exe",           // v1
			"xunit.console.clr4.x86.exe",       // v1
			"xunit.console.exe",                // v1, v2
			"xunit.console.x86.exe",            // v1, v2
			"xunit.v3.runner.console.exe",      // v3
			"xunit.v3.runner.console.x86.exe",  // v3

			// xUnit.net runner (GUI)
			"xunit.gui.clr4.exe",      // v1
			"xunit.gui.clr4.x86.exe",  // v1
			"xunit.gui.exe",           // v1
			"xunit.gui.x86.exe",       // v1

			// xUnit.net runner (MSBuild)
			"xunit.runner.msbuild.dll",     // v1, v2
			"xunit.v3.runner.msbuild.dll",  // v3

			// xUnit.net runner (TestDriven.net)
			"xunit.runner.tdnet.dll",  // v1, v2

			// xUnit.net v1
			"xunit.dll",
			"xunit.extensions.dll",
			"xunit.runner.utility.dll",

			// xUnit.net v2
			"xunit.abstractions.dll",                    // 2.0.0+
			"xunit.assert.dll",                          // 2.0.0+
			"xunit.core.dll",                            // 2.0.0+
			"xunit.execution.desktop.dll",               // 2.0.0+
			"xunit.execution.iOS-Universal.dll",         // 2.0.0
			"xunit.execution.MonoAndroid.dll",           // 2.0.0
			"xunit.execution.MonoTouch.dll",             // 2.0.0
			"xunit.execution.universal.dll",             // 2.0.0
			"xunit.execution.win8.dll",                  // 2.0.0
			"xunit.execution.wp8.dll",                   // 2.0.0
			"xunit.execution.dotnet.dll",                // 2.1.0+
			"xunit.runner.reporters.desktop.dll",        // 2.1.0
			"xunit.runner.reporters.dotnet.dll",         // 2.1.0
			"xunit.runner.reporters.net452.dll",         // 2.2.0+
			"xunit.runner.reporters.netstandard11.dll",  // 2.2.0+
			"xunit.runner.reporters.netstandard15.dll",  // 2.2.0+
			"xunit.runner.reporters.netstandard20.dll",  // 2.4.0
			"xunit.runner.reporters.netcoreapp10.dll",   // 2.3.0+
			"xunit.runner.utility.iOS-Universal.dll",    // 2.0.0
			"xunit.runner.utility.MonoAndroid.dll",      // 2.0.0
			"xunit.runner.utility.MonoTouch.dll",        // 2.0.0
			"xunit.runner.utility.universal.dll",        // 2.0.0
			"xunit.runner.utility.win8.dll",             // 2.0.0
			"xunit.runner.utility.wp8.dll",              // 2.0.0
			"xunit.runner.utility.desktop.dll",          // 2.1.0
			"xunit.runner.utility.dotnet.dll",           // 2.1.0
			"xunit.runner.utility.net35.dll",            // 2.2.0+
			"xunit.runner.utility.net452.dll",           // 2.2.0+
			"xunit.runner.utility.netstandard11.dll",    // 2.2.0+
			"xunit.runner.utility.netstandard15.dll",    // 2.2.0+
			"xunit.runner.utility.netcoreapp10.dll",     // 2.3.0+
			"xunit.runner.utility.netstandard20.dll",    // 2.4.0
			"xunit.runner.utility.uwp10.dll",            // 2.4.0-2.4.2

			// xUnit.net v3
			"xunit.v3.assert.dll",                  // 1.0.0+
			"xunit.v3.common.dll",                  // 1.0.0+
			"xunit.v3.core.dll",                    // 1.0.0+
			"xunit.v3.runner.common.dll",           // 1.0.0+
			"xunit.v3.runner.inproc.console.dll",   // 1.0.0+
			"xunit.v3.runner.utility.netfx.dll",    // 1.0.0+
			"xunit.v3.runner.utility.netcore.dll",  // 1.0.0+
		};

		internal static TestProperty ManagedMethodProperty { get; } =
			TestProperty.Register("TestCase.ManagedMethod", "ManagedMethod", string.Empty, string.Empty, typeof(string), x => !string.IsNullOrWhiteSpace(x as string), TestPropertyAttributes.Hidden, typeof(TestCase));

		internal static TestProperty ManagedTypeProperty { get; } =
			TestProperty.Register("TestCase.ManagedType", "ManagedType", string.Empty, string.Empty, typeof(string), x => !string.IsNullOrWhiteSpace(x as string), TestPropertyAttributes.Hidden, typeof(TestCase));

		internal static TestProperty SkipReasonProperty { get; } =
			TestProperty.Register("XunitSkipReason", "xUnit.net Skip Reason", typeof(string), typeof(VsTestRunner));

		internal static TestProperty TestCaseExplicitProperty { get; } =
			TestProperty.Register("XunitTestCaseExplicit", "xUnit.net Test Case Explicit Flag", typeof(bool), typeof(VsTestRunner));

		internal static TestProperty TestCaseSerializationProperty { get; } =
			TestProperty.Register("XunitTestCaseSerialization", "xUnit.net Test Case Serialization", typeof(string), typeof(VsTestRunner));

		internal static TestProperty TestCaseUniqueIDProperty { get; } =
			TestProperty.Register("XunitTestCaseUniqueID", "xUnit.net Test Case Unique ID", typeof(string), typeof(VsTestRunner));

		public void Cancel() =>
			cancelled = true;

		public void DiscoverTests(
			IEnumerable<string> sources,
			IDiscoveryContext discoveryContext,
			IMessageLogger logger,
			ITestCaseDiscoverySink discoverySink)
		{
			Guard.ArgumentNotNull(sources);
			Guard.ArgumentNotNull(logger);
			Guard.ArgumentNotNull(discoverySink);

			var stopwatch = Stopwatch.StartNew();
			var loggerHelper = new LoggerHelper(logger, stopwatch);

			PrintHeader(loggerHelper);

			var runSettings = RunSettings.Parse(discoveryContext.RunSettings?.SettingsXml);
			if (!runSettings.IsMatchingTargetFramework())
				return;

			using var _ = AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), new DiagnosticMessageSink(loggerHelper, showInternalDiagnostics: runSettings.InternalDiagnosticMessages ?? false));

			// Force design mode to enable serialization because in either case (command line discovery or IDE discovery) we may be
			// asked later to run specific test cases, so we can't rely on DesignMode to make the decision.
			var testPlatformContext = new TestPlatformContext { DesignMode = true };

			var testCaseFilter = new TestCaseFilter(discoveryContext, loggerHelper);

			// We can't use await here because the contract from VSTest says we have to wait for everything to finish
			// before returning from this function.
			DiscoverTests(
				sources, loggerHelper, runSettings,
				(source, discoverer, discoveryOptions) => new VsDiscoverySink(source, loggerHelper, discoverySink, discoveryOptions, testPlatformContext, testCaseFilter, () => cancelled)
			).GetAwaiter().GetResult();
		}

		async Task DiscoverTests<TVisitor>(
			IEnumerable<string> sources,
			LoggerHelper logger,
			RunSettings runSettings,
			Func<string, IFrontControllerDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
			Action<string, IFrontControllerDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor>? visitComplete = null)
				where TVisitor : IVsDiscoverySink, IDisposable
		{
			try
			{
				RemotingUtility.CleanUpRegisteredChannels();

				var project = new XunitProject();

				foreach (var assemblyFileNameCanBeWithoutAbsolutePath in sources)
				{
					var assemblyFileName = Path.GetFullPath(assemblyFileNameCanBeWithoutAbsolutePath);
					var metadata = AssemblyUtility.GetAssemblyMetadata(assemblyFileName);
					// Silently ignore anything which doesn't look like a test project, because reporting it just throws
					// lots of warnings into the test output window as Test Explorer asks you to enumerate tests for every
					// assembly you build in your solution, not just the ones with references to this runner.
					if (metadata is null || metadata.XunitVersion == 0)
						return;

					var assembly = new XunitProjectAssembly(project, assemblyFileName, metadata);

					var configWarnings = new List<string>();
					ConfigReader.Load(assembly.Configuration, assembly.AssemblyFileName, assembly.ConfigFileName, configWarnings);
					runSettings.CopyTo(assembly.Configuration);

					// Pre-enumerate theories by default, so that we can see all traits, including those that come from
					// ITheoryDataRow in v3. See: https://github.com/xunit/visualstudio.xunit/issues/426
					assembly.Configuration.PreEnumerateTheories ??= true;

					foreach (var warning in configWarnings)
						logger.LogWarning("{0}", warning);

					var assemblyDisplayName = Path.GetFileNameWithoutExtension(assembly.AssemblyFileName);
					var diagnosticMessageSink = new DiagnosticMessageSink(logger, assemblyDisplayName, assembly.Configuration.DiagnosticMessagesOrDefault, assembly.Configuration.InternalDiagnosticMessagesOrDefault);

					await using var sourceInformationProvider = new VisualStudioSourceInformationProvider(assemblyFileName, diagnosticMessageSink);
					await using var controller = XunitFrontController.Create(assembly, sourceInformationProvider, diagnosticMessageSink);
					if (controller is null)
						return;

					var discoveryOptions = TestFrameworkOptions.ForDiscovery(assembly.Configuration);
					if (!await DiscoverTestsInAssembly(controller, logger, runSettings, visitorFactory, visitComplete, assembly, discoveryOptions))
						break;
				}
			}
			catch (Exception e)
			{
				logger.LogWarning("Exception discovering tests: {0}", e.Unwrap());
			}
		}

		async Task<bool> DiscoverTestsInAssembly<TVisitor>(
			IFrontControllerDiscoverer controller,
			LoggerHelper logger,
			RunSettings runSettings,
			Func<string, IFrontControllerDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
			Action<string, IFrontControllerDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor>? visitComplete,
			XunitProjectAssembly assembly,
			ITestFrameworkDiscoveryOptions discoveryOptions)
				where TVisitor : IVsDiscoverySink, IDisposable
		{
			if (cancelled || assembly.AssemblyFileName is null || assembly.AssemblyMetadata is null)
				return false;

			using var _ = AssemblyHelper.SubscribeResolveForAssembly(assembly.AssemblyFileName, new DiagnosticMessageSink(logger, showDiagnostics: assembly.Configuration.DiagnosticMessagesOrDefault, showInternalDiagnostics: assembly.Configuration.DiagnosticMessagesOrDefault));

			var fileName = "(unknown assembly)";

			try
			{
				var diagnosticMessageSink = new DiagnosticMessageSink(logger, fileName, showDiagnostics: assembly.Configuration.DiagnosticMessagesOrDefault, showInternalDiagnostics: assembly.Configuration.InternalDiagnosticMessagesOrDefault);
				var reporterMessageHandler = await GetRunnerReporter(logger, runSettings).CreateMessageHandler(new VisualStudioRunnerLogger(logger), diagnosticMessageSink);
				fileName = Path.GetFileNameWithoutExtension(assembly.AssemblyFileName);

				if (!PlatformAssemblies.Contains(Path.GetFileName(assembly.AssemblyFileName)))
				{
					using var visitor = visitorFactory(assembly.AssemblyFileName, controller, discoveryOptions);
					var totalTests = 0;
					var appDomain = assembly.Configuration.AppDomain ?? AppDomainDefaultBehavior;
					var usingAppDomains = controller.CanUseAppDomains && appDomain != AppDomainSupport.Denied;
					reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting
					{
						AppDomain = usingAppDomains ? AppDomainOption.Enabled : AppDomainOption.Disabled,
						Assembly = assembly,
						DiscoveryOptions = discoveryOptions,
						ShadowCopy = assembly.Configuration.ShadowCopyOrDefault,
						UniqueID = controller.TestAssemblyUniqueID,
					});

					try
					{
						var findSettings = new FrontControllerFindSettings(discoveryOptions);
						controller.Find(visitor, findSettings);

						totalTests = visitor.Finish();

						visitComplete?.Invoke(assembly.AssemblyFileName, controller, discoveryOptions, visitor);
					}
					finally
					{
						reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryFinished
						{
							Assembly = assembly,
							DiscoveryOptions = discoveryOptions,
							TestCasesToRun = totalTests,
							UniqueID = controller.TestAssemblyUniqueID,
						});
					}
				}
			}
			catch (Exception e)
			{
				var ex = e.Unwrap();

				if (ex is InvalidOperationException)
					logger.LogWarning("Skipping: {0} ({1})", fileName, ex.Message);
				else if (ex is FileNotFoundException fileNotFound)
					logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName));
				else if (ex is FileLoadException fileLoad)
					logger.LogWarning("Skipping: {0} (could not load dependent assembly '{1}'): {2}", fileName, Path.GetFileNameWithoutExtension(fileLoad.FileName), ex.Message);
				else
					logger.LogWarning("Exception discovering tests from {0}: {1}", fileName, ex);
			}

			return true;
		}

		internal static IReadOnlyList<IRunnerReporter> GetAvailableRunnerReporters(LoggerHelper? logger)
		{
			var result = RegisteredRunnerReporters.Get(typeof(VsTestRunner).Assembly, out var messages);

			if (logger is not null)
				foreach (var message in messages)
					logger.LogWarning("{0}", message);

			return result;
		}

		internal static IRunnerReporter GetRunnerReporter(
			LoggerHelper? logger,
			RunSettings runSettings)
		{
			var reporter = default(IRunnerReporter);
			var availableReporters = new Lazy<IReadOnlyList<IRunnerReporter>>(() => GetAvailableRunnerReporters(logger));

			try
			{
				if (!string.IsNullOrEmpty(runSettings.ReporterSwitch))
				{
					reporter = availableReporters.Value.FirstOrDefault(r => string.Equals(r.RunnerSwitch, runSettings.ReporterSwitch, StringComparison.OrdinalIgnoreCase));
					if (reporter is null && logger is not null)
						logger.LogWarning("Could not find requested reporter '{0}'", runSettings.ReporterSwitch);
				}

				if (reporter is null && !(runSettings.NoAutoReporters ?? false))
					reporter = availableReporters.Value.FirstOrDefault(r => r.IsEnvironmentallyEnabled);
			}
			catch { }

			return reporter ?? new DefaultRunnerReporter();
		}

		static List<DiscoveredTestCase> GetVsTestCases(
			string source,
			VsExecutionDiscoverySink visitor,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext)
		{
			var testCases = visitor.TestCases;
			var results = new List<DiscoveredTestCase>();

			for (var idx = 0; idx < testCases.Count; ++idx)
			{
				var testCase = new DiscoveredTestCase(source, testCases[idx], logger, testPlatformContext);
				if (testCase.VSTestCase is not null)
					results.Add(testCase);
			}

			return results;
		}

		static void PrintHeader(LoggerHelper loggerHelper)
		{
			if (Interlocked.Exchange(ref printedHeader, 1) == 0)
				loggerHelper.Log("xUnit.net VSTest Adapter v{0} ({1}-bit {2})", ThisAssembly.AssemblyInformationalVersion, IntPtr.Size * 8, RuntimeInformation.FrameworkDescription);
		}

		public void RunTests(
			IEnumerable<TestCase>? tests,
			IRunContext? runContext,
			IFrameworkHandle? frameworkHandle)
		{
			if (tests is null)
				return;

			var stopwatch = Stopwatch.StartNew();
			var logger = new LoggerHelper(frameworkHandle, stopwatch);
			var project = new XunitProject();
			var runSettings = RunSettings.Parse(runContext?.RunSettings?.SettingsXml);
			var runExplicitTests = tests.All(testCase => testCase.GetPropertyValue(TestCaseExplicitProperty, false));

			using var _ = AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), new DiagnosticMessageSink(logger, showInternalDiagnostics: runSettings.InternalDiagnosticMessages ?? false));

			PrintHeader(logger);

			var testPlatformContext = new TestPlatformContext { DesignMode = runSettings.DesignMode };

			// We can't use await here because the contract from VSTest says we have to wait for everything to finish
			// before returning from this function.
			RunTests(
				runContext, frameworkHandle, logger, testPlatformContext, runSettings,
				() =>
					[.. tests
						.Distinct(TestCaseUniqueIDComparer.Instance)
						.GroupBy(testCase => testCase.Source)
						.Select(group => AssemblyRunInfo.Create(logger, project, runSettings, group.Key, [.. group], runExplicitTests))
						.WhereNotNull()]
			).GetAwaiter().GetResult();
		}

		public void RunTests(
			IEnumerable<string>? sources,
			IRunContext? runContext,
			IFrameworkHandle? frameworkHandle)
		{
			if (sources is null)
				return;

			var stopwatch = Stopwatch.StartNew();
			var logger = new LoggerHelper(frameworkHandle, stopwatch);
			var project = new XunitProject();

			PrintHeader(logger);

			var runSettings = RunSettings.Parse(runContext?.RunSettings?.SettingsXml);
			if (!runSettings.IsMatchingTargetFramework())
				return;

			using var _ = AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), new DiagnosticMessageSink(logger, showInternalDiagnostics: runSettings.InternalDiagnosticMessages ?? false));

			var testPlatformContext = new TestPlatformContext { DesignMode = runSettings.DesignMode };

			// We can't use await here because the contract from VSTest says we have to wait for everything to finish
			// before returning from this function.
			RunTests(
				runContext, frameworkHandle, logger, testPlatformContext, runSettings,
				() => [.. sources.Select(source => AssemblyRunInfo.Create(logger, project, runSettings, Path.GetFullPath(source))).WhereNotNull()]
			).GetAwaiter().GetResult();
		}

		async Task RunTests(
			IRunContext? runContext,
			IFrameworkHandle? frameworkHandle,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext,
			RunSettings runSettings,
			Func<List<AssemblyRunInfo>> getRunInfos)
		{
			Guard.ArgumentNotNull(runContext);
			Guard.ArgumentNotNull(frameworkHandle);

			try
			{
				RemotingUtility.CleanUpRegisteredChannels();

				if (Debugger.IsAttached)
					logger.LogWarning("{0}", "* Note: Long running test detection and test timeouts are disabled due to an attached debugger *");

				cancelled = false;

				var runInfos = getRunInfos();
				var parallelizeAssemblies = runInfos.All(runInfo => runInfo.Assembly.Configuration.ParallelizeAssemblyOrDefault);
				var diagnosticMessages = runInfos.Any(runInfo => runInfo.Assembly.Configuration.DiagnosticMessagesOrDefault);
				var internalDiagnosticMessages = runInfos.Any(runInfo => runInfo.Assembly.Configuration.InternalDiagnosticMessagesOrDefault);
				var reporter = GetRunnerReporter(logger, runSettings);
				var diagnosticMessageSink = new DiagnosticMessageSink(logger, showDiagnostics: diagnosticMessages, showInternalDiagnostics: internalDiagnosticMessages);
				await using var reporterMessageHandler = await reporter.CreateMessageHandler(new VisualStudioRunnerLogger(logger), diagnosticMessageSink);

				if (parallelizeAssemblies)
					runInfos
						.Select(runInfo => RunTestsInAssemblyAsync(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo))
						.ToList()
						.ForEach(@event => @event.WaitOne());
				else
					runInfos.ForEach(runInfo => RunTestsInAssemblyAsync(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo).WaitOne());
			}
			catch (Exception ex)
			{
				logger.LogError("Catastrophic failure: {0}", ex);
			}
		}

		async Task RunTestsInAssembly(
			IRunContext runContext,
			IFrameworkHandle frameworkHandle,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext,
			RunSettings runSettings,
			IMessageSink reporterMessageHandler,
			AssemblyRunInfo runInfo)
		{
			if (cancelled)
				return;

			var assemblyDisplayName = "(unknown assembly)";

			try
			{
				var assemblyFileName = runInfo.Assembly.AssemblyFileName;
				if (assemblyFileName is null)
					return;

				// Pre-enumerate theories by default, so that we can see all traits, including those that come from
				// ITheoryDataRow in v3. See: https://github.com/xunit/visualstudio.xunit/issues/426
				runInfo.Assembly.Configuration.PreEnumerateTheories ??= true;

				var configuration = runInfo.Assembly.Configuration;

				using var _ = AssemblyHelper.SubscribeResolveForAssembly(assemblyFileName, new DiagnosticMessageSink(logger, showDiagnostics: configuration.DiagnosticMessagesOrDefault, showInternalDiagnostics: configuration.DiagnosticMessagesOrDefault));

				assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
				var longRunningSeconds = configuration.LongRunningTestSecondsOrDefault;

				var diagnosticSink = new DiagnosticMessageSink(logger, assemblyDisplayName, configuration.DiagnosticMessagesOrDefault, configuration.InternalDiagnosticMessagesOrDefault);
				var discoveryOptions = TestFrameworkOptions.ForDiscovery(configuration);

				var frameworkHandle2 = frameworkHandle as IFrameworkHandle2;
				var testProcessLauncher = default(ITestProcessLauncher);
				if (runContext.IsBeingDebugged && frameworkHandle2 is not null)
					testProcessLauncher = new DebuggerProcessLauncher(frameworkHandle2);

				await using var sourceInformationProvider = new VisualStudioSourceInformationProvider(assemblyFileName, diagnosticSink);
				await using var controller = XunitFrontController.Create(runInfo.Assembly, sourceInformationProvider, diagnosticSink, testProcessLauncher);
				if (controller is null)
					return;

				var testCasesMap = new ConcurrentDictionary<string, TestCase>();
				var testCaseSerializations = new ConcurrentBag<string>();
				if (runInfo.TestCases is null || runInfo.TestCases.Count == 0)
				{
					// Discover tests
					var assemblyDiscoveredInfo = default(AssemblyDiscoveredInfo);
					await DiscoverTestsInAssembly(
						controller,
						logger,
						runSettings,
						(source, discoverer, discoveryOptions) => new VsExecutionDiscoverySink(() => cancelled),
						(source, discoverer, discoveryOptions, visitor) =>
						{
							if (configuration.InternalDiagnosticMessagesOrDefault)
								foreach (var testCase in visitor.TestCases)
									logger.LogWithSource(assemblyFileName, "Discovered [execution] test case '{0}' (ID = '{1}')", testCase.TestCaseDisplayName, testCase.TestCaseUniqueID);

							assemblyDiscoveredInfo = new AssemblyDiscoveredInfo(source, GetVsTestCases(source, visitor, logger, testPlatformContext));
						},
						runInfo.Assembly,
						discoveryOptions
					);

					if (assemblyDiscoveredInfo is null || assemblyDiscoveredInfo.DiscoveredTestCases is null || assemblyDiscoveredInfo.DiscoveredTestCases.Count == 0)
					{
						if (configuration.InternalDiagnosticMessagesOrDefault)
							logger.LogWarning("Skipping '{0}': no tests were found during pre-execution discovery", assemblyFileName);

						return;
					}

					// Filter tests
					var traitNames = new HashSet<string>(assemblyDiscoveredInfo.DiscoveredTestCases.SelectMany(testCase => testCase.TraitNames));
					var filter = new TestCaseFilter(runContext, logger, assemblyDiscoveredInfo.AssemblyFileName, traitNames);
					var filteredTestCases = assemblyDiscoveredInfo.DiscoveredTestCases.Where(dtc => dtc.VSTestCase is not null && filter.MatchTestCase(dtc.VSTestCase)).ToList();

					foreach (var filteredTestCase in filteredTestCases)
					{
						var uniqueID = filteredTestCase.UniqueID;
						if (string.IsNullOrEmpty(filteredTestCase.TestCase.Serialization))
							logger.LogWarningWithSource(assemblyFileName, "Skipping test case '{0}' (ID '{1}') without serialization", filteredTestCase.TestCase.TestCaseDisplayName, filteredTestCase.TestCase.TestCaseUniqueID);
						else if (filteredTestCase.VSTestCase is not null)
						{
							if (testCasesMap.TryAdd(uniqueID, filteredTestCase.VSTestCase))
								testCaseSerializations.Add(filteredTestCase.TestCase.Serialization);
							else
								logger.LogWarningWithSource(assemblyFileName, "Skipping test case with duplicate ID '{0}' ('{1}' and '{2}')", uniqueID, testCasesMap[uniqueID].DisplayName, filteredTestCase.VSTestCase?.DisplayName);
						}
					}
				}
				else
				{
					foreach (var testCase in runInfo.TestCases)
					{
						var uniqueID = testCase.GetPropertyValue<string>(TestCaseUniqueIDProperty, null);
						var serialization = testCase.GetPropertyValue<string>(TestCaseSerializationProperty, null);

						if (configuration.InternalDiagnosticMessagesOrDefault)
							logger.LogWithSource(assemblyFileName, "Selective execution requested for test case ID '{0}' (serialization = '{1}')", uniqueID ?? "(null)", serialization ?? "(null)");

						if (uniqueID is null)
							logger.LogWarningWithSource(assemblyFileName, "VSTestCase {0} did not have an associated unique ID", testCase.DisplayName);
						else if (string.IsNullOrEmpty(serialization))
							logger.LogWarningWithSource(assemblyFileName, "Skipping test case '{0}' (ID '{1}') without serialization", testCase.DisplayName, uniqueID);
						else
						{
							if (testCasesMap.TryAdd(uniqueID, testCase))
								testCaseSerializations.Add(serialization);
							else
								logger.LogWarningWithSource(assemblyFileName, "Skipping test case with duplicate ID '{0}' ('{1}' and '{2}')", uniqueID, testCasesMap[uniqueID].DisplayName, testCase.DisplayName);
						}
					}
				}

				// https://github.com/xunit/visualstudio.xunit/issues/417
				if (testCaseSerializations.IsEmpty)
				{
					if (configuration.InternalDiagnosticMessagesOrDefault)
						logger.LogWarning("Skipping '{0}': no tests passed the filter", assemblyFileName);

					return;
				}

				// Execute tests
				var executionOptions = TestFrameworkOptions.ForExecution(configuration);
				if (!configuration.ParallelizeTestCollectionsOrDefault)
				{
					executionOptions.SetSynchronousMessageReporting(true);
					executionOptions.SetDisableParallelization(true);
				}

				var vsExecutionSink = new VsExecutionSink(reporterMessageHandler, frameworkHandle, logger, testCasesMap, () => cancelled);
				var executionSinkOptions = new ExecutionSinkOptions
				{
					DiagnosticMessageSink = diagnosticSink,
					FailSkips = configuration.FailSkipsOrDefault,
					LongRunningTestTime = TimeSpan.FromSeconds(longRunningSeconds),
				};

				var appDomain = configuration.AppDomain ?? AppDomainDefaultBehavior;
				var appDomainOption = controller.CanUseAppDomains && appDomain != AppDomainSupport.Denied ? AppDomainOption.Enabled : AppDomainOption.Disabled;
				bool shadowCopy = configuration.ShadowCopyOrDefault;
				var resultsSink = new ExecutionSink(runInfo.Assembly, discoveryOptions, executionOptions, appDomainOption, shadowCopy, vsExecutionSink, executionSinkOptions);

				var frontControllerSettings = new FrontControllerRunSettings(executionOptions, testCaseSerializations);
				if (testProcessLauncher is not null)
					frontControllerSettings.LaunchOptions.WaitForDebugger = true;

				controller.Run(resultsSink, frontControllerSettings);
				resultsSink.Finished.WaitOne();

				if ((resultsSink.ExecutionSummary.Failed != 0 || resultsSink.ExecutionSummary.Errors != 0) && executionOptions.GetStopOnTestFailOrDefault())
				{
					logger.Log("{0}", "Canceling due to test failure...");
					cancelled = true;
				}
			}
			catch (Exception ex)
			{
				logger.LogError("{0}: Catastrophic failure: {1}", assemblyDisplayName, ex);
			}
		}

		ManualResetEvent RunTestsInAssemblyAsync(
			IRunContext runContext,
			IFrameworkHandle frameworkHandle,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext,
			RunSettings runSettings,
			IMessageSink reporterMessageHandler,
			AssemblyRunInfo runInfo)
		{
			var @event = new ManualResetEvent(initialState: false);

			ThreadPool.QueueUserWorkItem(async _ =>
			{
				try
				{
					await RunTestsInAssembly(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo);
				}
				finally
				{
					@event.Set();
				}
			});

			return @event;
		}

		class AssemblyDiscoveredInfo(
			string assemblyFileName,
			IList<DiscoveredTestCase> discoveredTestCases)
		{
			public string AssemblyFileName { get; } = assemblyFileName;

			public IList<DiscoveredTestCase> DiscoveredTestCases { get; } = discoveredTestCases;
		}

		class DiscoveredTestCase(
			string source,
			ITestCaseDiscovered testCase,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext)
		{
			public string Name { get; } = $"{testCase.TestClassName}.{testCase.TestMethodName} ({testCase.TestCaseUniqueID})";

			public IEnumerable<string> TraitNames { get; } = testCase.Traits.Keys;

			public TestCase? VSTestCase { get; } = VsDiscoverySink.CreateVsTestCase(source, testCase, logger, testPlatformContext);

			public ITestCaseDiscovered TestCase { get; } = testCase;

			public string UniqueID { get; } = testCase.TestCaseUniqueID;
		}

		class TestCaseUniqueIDComparer : IEqualityComparer<TestCase>
		{
			public static TestCaseUniqueIDComparer Instance = new();

			public bool Equals(TestCase? x, TestCase? y)
			{
				if (x is null)
					return y is null;
				if (y is null)
					return false;
				if (x.GetPropertyValue(TestCaseUniqueIDProperty) is not string xID)
					return false;
				if (y.GetPropertyValue(TestCaseUniqueIDProperty) is not string yID)
					return false;

				return xID == yID;
			}

			public int GetHashCode(TestCase obj)
			{
				if (obj is null)
					return 0;
				if (obj.GetPropertyValue(TestCaseUniqueIDProperty) is not string id)
					return 0;

				return id.GetHashCode();
			}
		}
	}
}
