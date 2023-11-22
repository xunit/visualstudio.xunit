using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;
using Xunit.Internal;

#if NETCOREAPP
using System.Text;
using Internal.Microsoft.Extensions.DependencyModel;
#endif

namespace Xunit.Runner.VisualStudio
{
	[FileExtension(".dll")]
	[FileExtension(".exe")]
	[DefaultExecutorUri(Constants.ExecutorUri)]
	[ExtensionUri(Constants.ExecutorUri)]
	[Category("managed")]
	public class VsTestRunner : ITestDiscoverer, ITestExecutor
	{
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

			// xUnit.net v3 (core)
			"xunit.v3.assert.dll",
			"xunit.v3.common.dll",
			"xunit.v3.core.dll",

			// xUnit.net v3 (runners)
			"xunit.v3.runner.common.dll",
			"xunit.v3.runner.inproc.console.dll",
			"xunit.v3.runner.tdnet.dll",
			"xunit.v3.runner.utility.net472.dll",
			"xunit.v3.runner.utility.netstandard20.dll",

			// xUnit.net v2 (core)
			"xunit.abstractions.dll",
			"xunit.assert.dll",
			"xunit.core.dll",
			"xunit.execution.desktop.dll",        // 2.0.0+
			"xunit.execution.iOS-Universal.dll",  // 2.0.0
			"xunit.execution.MonoAndroid.dll",    // 2.0.0
			"xunit.execution.MonoTouch.dll",      // 2.0.0
			"xunit.execution.universal.dll",      // 2.0.0
			"xunit.execution.win8.dll",           // 2.0.0
			"xunit.execution.wp8.dll",            // 2.0.0
			"xunit.execution.dotnet.dll",         // 2.1.0+

			// xUnit.net v2 (runners)
			"xunit.runner.reporters.desktop.dll",        // 2.1.0
			"xunit.runner.reporters.dotnet.dll",         // 2.1.0
			"xunit.runner.reporters.net452.dll",         // 2.2.0+
			"xunit.runner.reporters.netstandard11.dll",  // 2.2.0+
			"xunit.runner.reporters.netstandard15.dll",  // 2.2.0+
			"xunit.runner.reporters.netstandard20.dll",  // 2.4.0
			"xunit.runner.reporters.netcoreapp10.dll",   // 2.3.0+
			"xunit.runner.tdnet.dll",
			"xunit.runner.utility.iOS-Universal.dll",  // 2.0.0
			"xunit.runner.utility.MonoAndroid.dll",    // 2.0.0
			"xunit.runner.utility.MonoTouch.dll",      // 2.0.0
			"xunit.runner.utility.universal.dll",      // 2.0.0
			"xunit.runner.utility.win8.dll",           // 2.0.0
			"xunit.runner.utility.wp8.dll",            // 2.0.0
			"xunit.runner.utility.desktop.dll",        // 2.1.0
			"xunit.runner.utility.dotnet.dll",         // 2.1.0
			"xunit.runner.utility.net35.dll",          // 2.2.0+
			"xunit.runner.utility.net452.dll",         // 2.2.0+
			"xunit.runner.utility.netstandard11.dll",  // 2.2.0+
			"xunit.runner.utility.netstandard15.dll",  // 2.2.0+
			"xunit.runner.utility.netcoreapp10.dll",   // 2.3.0+
			"xunit.runner.utility.netstandard20.dll",  // 2.4.0
			"xunit.runner.utility.uwp10.dll",          // 2.4.0-2.4.2

			// xUnit.net v1
			"xunit.dll",
		};

		public static TestProperty SerializedTestCaseProperty { get; } = GetTestProperty();

		bool cancelled;

		public void Cancel()
		{
			cancelled = true;
		}

		void ITestDiscoverer.DiscoverTests(
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

			var testPlatformContext = new TestPlatformContext
			{
				// Force serialization because in either case (command line discovery or IDE discovery) we may be
				// asked later to run specific test cases, so we can't rely on DesignMode to make the decision.
				RequireSerialization = true,
				RequireSourceInformation = runSettings.CollectSourceInformation,
			};

			var testCaseFilter = new TestCaseFilter(discoveryContext, loggerHelper);
			DiscoverTests(
				sources, loggerHelper, testPlatformContext, runSettings,
				(source, discoverer, discoveryOptions) => new VsDiscoverySink(source, discoverer, loggerHelper, discoverySink, discoveryOptions, testPlatformContext, testCaseFilter, () => cancelled)
			);
		}

		static void PrintHeader(LoggerHelper loggerHelper)
		{
			if (Interlocked.Exchange(ref printedHeader, 1) == 0)
				loggerHelper.Log($"xUnit.net VSTest Adapter v{ThisAssembly.AssemblyInformationalVersion} ({IntPtr.Size * 8}-bit {RuntimeInformation.FrameworkDescription})");
		}

		void ITestExecutor.RunTests(
			IEnumerable<string>? sources,
			IRunContext? runContext,
			IFrameworkHandle? frameworkHandle)
		{
			if (sources is null)
				return;

			var stopwatch = Stopwatch.StartNew();
			var logger = new LoggerHelper(frameworkHandle, stopwatch);

			PrintHeader(logger);

			var runSettings = RunSettings.Parse(runContext?.RunSettings?.SettingsXml);
			if (!runSettings.IsMatchingTargetFramework())
				return;

			var testPlatformContext = new TestPlatformContext
			{
				RequireSerialization = !runSettings.DisableSerialization,
				RequireSourceInformation = runSettings.CollectSourceInformation,
			};

			RunTests(
				runContext, frameworkHandle, logger, testPlatformContext, runSettings,
				() => sources.Select(source => new AssemblyRunInfo(runSettings, GetAssemblyFileName(source))).ToList()
			);
		}

		void ITestExecutor.RunTests(
			IEnumerable<TestCase>? tests,
			IRunContext? runContext,
			IFrameworkHandle? frameworkHandle)
		{
			if (tests is null)
				return;

			var stopwatch = Stopwatch.StartNew();
			var logger = new LoggerHelper(frameworkHandle, stopwatch);
			var runSettings = RunSettings.Parse(runContext?.RunSettings?.SettingsXml);

			PrintHeader(logger);

			var testPlatformContext = new TestPlatformContext
			{
				RequireSerialization = !runSettings.DisableSerialization,
				RequireSourceInformation = runSettings.CollectSourceInformation,
			};

			RunTests(
				runContext, frameworkHandle, logger, testPlatformContext, runSettings,
				() =>
					tests
						.GroupBy(testCase => testCase.Source)
						.Select(group => new AssemblyRunInfo(runSettings, group.Key, group.ToList()))
						.ToList()
			);
		}

		// Helpers

		void DiscoverTests<TVisitor>(
			IEnumerable<string> sources,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext,
			RunSettings runSettings,
			Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
			Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor>? visitComplete = null)
				where TVisitor : IVsDiscoverySink, IDisposable
		{
			try
			{
				RemotingUtility.CleanUpRegisteredChannels();

				var internalDiagnosticsSinkLocal = DiagnosticMessageSink.ForInternalDiagnostics(logger, runSettings.InternalDiagnosticMessages ?? false);
				using var _ = AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), MessageSinkAdapter.Wrap(internalDiagnosticsSinkLocal));

				foreach (var assemblyFileNameCanBeWithoutAbsolutePath in sources)
				{
					var assembly = new XunitProjectAssembly { AssemblyFilename = GetAssemblyFileName(assemblyFileNameCanBeWithoutAbsolutePath) };
					runSettings.CopyTo(assembly.Configuration);

					var fileName = Path.GetFileNameWithoutExtension(assembly.AssemblyFilename);
					var shadowCopy = assembly.Configuration.ShadowCopyOrDefault;
					var diagnosticsSinkLocal = DiagnosticMessageSink.ForDiagnostics(logger, fileName, assembly.Configuration.DiagnosticMessagesOrDefault);
					var appDomain = assembly.Configuration.AppDomain ?? AppDomainDefaultBehavior;

					using var sourceInformationProvider = new VisualStudioSourceInformationProvider(assembly.AssemblyFilename, internalDiagnosticsSinkLocal);
					using var controller = new XunitFrontController(appDomain, assembly.AssemblyFilename, shadowCopy: shadowCopy, sourceInformationProvider: sourceInformationProvider, diagnosticMessageSink: MessageSinkAdapter.Wrap(diagnosticsSinkLocal));
					if (!DiscoverTestsInSource(controller, logger, testPlatformContext, runSettings, visitorFactory, visitComplete, assembly))
						break;
				}
			}
			catch (Exception e)
			{
				logger.LogWarning("Exception discovering tests: {0}", e.Unwrap());
			}
		}

		bool DiscoverTestsInSource<TVisitor>(
			XunitFrontController controller,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext,
			RunSettings runSettings,
			Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
			Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor>? visitComplete,
			XunitProjectAssembly assembly)
				where TVisitor : IVsDiscoverySink, IDisposable
		{
			if (cancelled)
				return false;

			var fileName = "(unknown assembly)";

			try
			{
				var reporterMessageHandler = GetRunnerReporter(logger, runSettings, new[] { assembly.AssemblyFilename }).CreateMessageHandler(new VisualStudioRunnerLogger(logger));
				fileName = Path.GetFileNameWithoutExtension(assembly.AssemblyFilename);

				if (!IsXunitTestAssembly(assembly.AssemblyFilename))
				{
					if (assembly.Configuration.DiagnosticMessagesOrDefault)
						logger.Log("Skipping: {0} (no reference to xUnit.net)", fileName);
				}
				else
				{
					var discoveryOptions = TestFrameworkOptions.ForDiscovery(assembly.Configuration);

					using var visitor = visitorFactory(assembly.AssemblyFilename, controller, discoveryOptions);
					var totalTests = 0;
					var appDomain = assembly.Configuration.AppDomain ?? AppDomainDefaultBehavior;
					var usingAppDomains = controller.CanUseAppDomains && appDomain != AppDomainSupport.Denied;
					reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting(assembly, usingAppDomains, assembly.Configuration.ShadowCopyOrDefault, discoveryOptions));

					try
					{
						controller.Find(testPlatformContext.RequireSourceInformation, visitor, discoveryOptions);

						totalTests = visitor.Finish();

						visitComplete?.Invoke(assembly.AssemblyFilename, controller, discoveryOptions, visitor);
					}
					finally
					{
						reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryFinished(assembly, discoveryOptions, totalTests, totalTests));
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

		static TestProperty GetTestProperty() =>
			TestProperty.Register("XunitTestCase", "xUnit.net Test Case", typeof(string), typeof(VsTestRunner));

		static bool IsXunitTestAssembly(string assemblyFileName)
		{
			// Don't try to load ourselves (or any test framework assemblies), since we fail (issue #47 in xunit/xunit).
			if (PlatformAssemblies.Contains(Path.GetFileName(assemblyFileName)))
				return false;

#if NETCOREAPP
			return IsXunitPackageReferenced(assemblyFileName);
#else
			var assemblyFolder = Path.GetDirectoryName(assemblyFileName);
			if (assemblyFolder is null)
				return false;

			return File.Exists(Path.Combine(assemblyFolder, "xunit.dll"))
				|| Directory.GetFiles(assemblyFolder, "xunit.execution.*.dll").Length > 0;
#endif
		}

#if NETCOREAPP
		static bool IsXunitPackageReferenced(string assemblyFileName)
		{
			var depsFile = assemblyFileName.Replace(".dll", ".deps.json");
			if (!File.Exists(depsFile))
				return false;

			try
			{
				using var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(depsFile)));
				var context = new DependencyContextJsonReader().Read(stream);
				var xunitLibrary = context.RuntimeLibraries.Where(lib => lib.Name.Equals("xunit") || lib.Name.Equals("xunit.core")).FirstOrDefault();
				return xunitLibrary is not null;
			}
			catch
			{
				return false;
			}
		}
#endif

		static string GetAssemblyFileName(string source) =>
			Path.GetFullPath(source);

		void RunTests(
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

				cancelled = false;

				var runInfos = getRunInfos();
				var parallelizeAssemblies = runInfos.All(runInfo => runInfo.Assembly.Configuration.ParallelizeAssemblyOrDefault);
				var reporter = GetRunnerReporter(logger, runSettings, runInfos.Select(ari => ari.Assembly.AssemblyFilename).ToList());
				using var reporterMessageHandler = MessageSinkWithTypesAdapter.Wrap(reporter.CreateMessageHandler(new VisualStudioRunnerLogger(logger)));
				using var internalDiagnosticsSinkLocal = DiagnosticMessageSink.ForInternalDiagnostics(logger, runSettings.InternalDiagnosticMessages ?? false);
				using var _ = AssemblyHelper.SubscribeResolveForAssembly(typeof(VsTestRunner), MessageSinkAdapter.Wrap(internalDiagnosticsSinkLocal));

				if (parallelizeAssemblies)
					runInfos
						.Select(runInfo => RunTestsInAssemblyAsync(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo))
						.ToList()
						.ForEach(@event => @event.WaitOne());
				else
					runInfos.ForEach(runInfo => RunTestsInAssembly(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo));
			}
			catch (Exception ex)
			{
				logger.LogError("Catastrophic failure: {0}", ex);
			}
		}

		void RunTestsInAssembly(
			IRunContext runContext,
			IFrameworkHandle frameworkHandle,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext,
			RunSettings runSettings,
			IMessageSinkWithTypes reporterMessageHandler,
			AssemblyRunInfo runInfo)
		{
			if (cancelled)
				return;

			var assemblyDisplayName = "(unknown assembly)";

			try
			{
				var assemblyFileName = runInfo.Assembly.AssemblyFilename;
				assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
				var configuration = runInfo.Assembly.Configuration;
				var shadowCopy = configuration.ShadowCopyOrDefault;
				var appDomain = configuration.AppDomain ?? AppDomainDefaultBehavior;
				var longRunningSeconds = configuration.LongRunningTestSecondsOrDefault;

				var diagnosticsSinkLocal = DiagnosticMessageSink.ForDiagnostics(logger, assemblyDisplayName, runInfo.Assembly.Configuration.DiagnosticMessagesOrDefault);
				var diagnosticsSinkRemote = MessageSinkAdapter.Wrap(diagnosticsSinkLocal);
				var internalDiagnosticsSinkLocal = DiagnosticMessageSink.ForInternalDiagnostics(logger, runInfo.Assembly.Configuration.InternalDiagnosticMessagesOrDefault);

				using var sourceInformationProvider = new VisualStudioSourceInformationProvider(assemblyFileName, internalDiagnosticsSinkLocal);
				using var controller = new XunitFrontController(appDomain, assemblyFileName, shadowCopy: shadowCopy, sourceInformationProvider: sourceInformationProvider, diagnosticMessageSink: diagnosticsSinkRemote);
				var testCasesMap = new Dictionary<string, TestCase>();
				var testCases = new List<ITestCase>();
				if (runInfo.TestCases is null || !runInfo.TestCases.Any())
				{
					// Discover tests
					var assemblyDiscoveredInfo = default(AssemblyDiscoveredInfo);
					DiscoverTestsInSource(controller, logger, testPlatformContext, runSettings,
						(source, discoverer, discoveryOptions) => new VsExecutionDiscoverySink(() => cancelled),
						(source, discoverer, discoveryOptions, visitor) =>
						{
							if (discoveryOptions.GetInternalDiagnosticMessagesOrDefault())
								foreach (var testCase in visitor.TestCases)
									logger.Log(testCase, "Discovered [execution] test case '{0}' (ID = '{1}')",
										testCase.DisplayName, testCase.UniqueID);

							assemblyDiscoveredInfo = new AssemblyDiscoveredInfo(source, GetVsTestCases(source, discoverer, visitor, logger, testPlatformContext));
						},
						runInfo.Assembly
					);

					if (assemblyDiscoveredInfo is null || assemblyDiscoveredInfo.DiscoveredTestCases is null || !assemblyDiscoveredInfo.DiscoveredTestCases.Any())
					{
						if (configuration.InternalDiagnosticMessagesOrDefault)
							logger.LogWarning("Skipping '{0}' since no tests were found during discovery [execution].", assemblyFileName);

						return;
					}

					// Filter tests
					var traitNames = new HashSet<string>(assemblyDiscoveredInfo.DiscoveredTestCases.SelectMany(testCase => testCase.TraitNames));
					var filter = new TestCaseFilter(runContext, logger, assemblyDiscoveredInfo.AssemblyFileName, traitNames);
					var filteredTestCases = assemblyDiscoveredInfo.DiscoveredTestCases.Where(dtc => dtc.VSTestCase is not null && filter.MatchTestCase(dtc.VSTestCase)).ToList();

					foreach (var filteredTestCase in filteredTestCases)
					{
						var uniqueID = filteredTestCase.UniqueID;
						if (testCasesMap.ContainsKey(uniqueID))
							logger.LogWarning(filteredTestCase.TestCase, "Skipping test case with duplicate ID '{0}' ('{1}' and '{2}')", uniqueID, testCasesMap[uniqueID].DisplayName, filteredTestCase.VSTestCase?.DisplayName);
						else if (filteredTestCase.VSTestCase is not null)
						{
							testCasesMap.Add(uniqueID, filteredTestCase.VSTestCase);
							testCases.Add(filteredTestCase.TestCase);
						}
					}
				}
				else
				{
					try
					{
						var serializations =
							runInfo
								.TestCases
								.Select(tc => tc.GetPropertyValue<string>(SerializedTestCaseProperty, null))
								.ToList();

						if (configuration.InternalDiagnosticMessagesOrDefault)
							logger.LogWithSource(
								runInfo.Assembly.AssemblyFilename,
								"Deserializing {0} test case(s):{1}{2}",
								serializations.Count,
								Environment.NewLine,
								string.Join(Environment.NewLine, serializations.Select(x => $"  {x}"))
							);

						var deserializedTestCasesByUniqueId = controller.BulkDeserialize(serializations);

						if (deserializedTestCasesByUniqueId is null)
							logger.LogErrorWithSource(assemblyFileName, "Received null response from BulkDeserialize");
						else
						{
							for (var idx = 0; idx < runInfo.TestCases.Count; ++idx)
							{
								try
								{
									var kvp = deserializedTestCasesByUniqueId[idx];
									var vsTestCase = runInfo.TestCases[idx];

									if (kvp.Value is null)
									{
										logger.LogErrorWithSource(assemblyFileName, "Test case {0} failed to deserialize: {1}", vsTestCase.DisplayName, kvp.Key);
									}
									else
									{
										testCasesMap.Add(kvp.Key, vsTestCase);
										testCases.Add(kvp.Value);
									}
								}
								catch (Exception ex)
								{
									logger.LogErrorWithSource(assemblyFileName, "Catastrophic error deserializing item #{0}: {1}", idx, ex);
								}
							}
						}
					}
					catch (Exception ex)
					{
						logger.LogWarningWithSource(assemblyFileName, "Catastrophic error during deserialization: {0}", ex);
					}
				}

				// Execute tests
				var executionOptions = TestFrameworkOptions.ForExecution(runInfo.Assembly.Configuration);
				if (!runInfo.Assembly.Configuration.ParallelizeTestCollectionsOrDefault)
				{
					executionOptions.SetSynchronousMessageReporting(true);
					executionOptions.SetDisableParallelization(true);
				}

				reporterMessageHandler.OnMessage(new TestAssemblyExecutionStarting(runInfo.Assembly, executionOptions));

				using var vsExecutionSink = new VsExecutionSink(reporterMessageHandler, frameworkHandle, logger, testCasesMap, () => cancelled);
				var executionSinkOptions = new ExecutionSinkOptions
				{
					DiagnosticMessageSink = diagnosticsSinkRemote,
					FailSkips = configuration.FailSkipsOrDefault,
					LongRunningTestTime = TimeSpan.FromSeconds(longRunningSeconds),
				};
				var resultsSink = new ExecutionSink(vsExecutionSink, executionSinkOptions);

				controller.RunTests(testCases, resultsSink, executionOptions);
				resultsSink.Finished.WaitOne();

				reporterMessageHandler.OnMessage(new TestAssemblyExecutionFinished(runInfo.Assembly, executionOptions, resultsSink.ExecutionSummary));
				if ((resultsSink.ExecutionSummary.Failed != 0 || resultsSink.ExecutionSummary.Errors != 0) && executionOptions.GetStopOnTestFailOrDefault())
				{
					logger.Log("Canceling due to test failure...");
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
			IMessageSinkWithTypes reporterMessageHandler,
			AssemblyRunInfo runInfo)
		{
			var @event = new ManualResetEvent(initialState: false);
			void handler()
			{
				try
				{
					RunTestsInAssembly(runContext, frameworkHandle, logger, testPlatformContext, runSettings, reporterMessageHandler, runInfo);
				}
				finally
				{
					@event.Set();
				}
			}

			ThreadPool.QueueUserWorkItem(_ => handler());
			return @event;
		}

		public static IRunnerReporter GetRunnerReporter(
			LoggerHelper? logger,
			RunSettings runSettings,
			IReadOnlyList<string> assemblyFileNames)
		{
			var reporter = default(IRunnerReporter);
			var availableReporters = new Lazy<IReadOnlyList<IRunnerReporter>>(() => GetAvailableRunnerReporters(logger, assemblyFileNames));

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

			return reporter ?? new DefaultRunnerReporterWithTypes();
		}

		public static IReadOnlyList<IRunnerReporter> GetAvailableRunnerReporters(
			LoggerHelper? logger,
			IReadOnlyList<string> sources)
		{
			var result = new List<IRunnerReporter>();
			var adapterAssembly = typeof(VsTestRunner).Assembly;

			// We need to combine the source folders with our folder to find all potential runners
			var folders =
				sources
					.Select(s => Path.GetDirectoryName(Path.GetFullPath(s)))
					.WhereNotNull()
					.Concat(new[] { Path.GetDirectoryName(adapterAssembly.GetLocalCodeBase()) })
					.Distinct();

			foreach (var folder in folders)
			{
				result.AddRange(RunnerReporterUtility.GetAvailableRunnerReporters(folder, out var messages));

				if (logger is not null)
					foreach (var message in messages)
						logger.LogWarning(message);
			}

			return result;
		}

		static IList<DiscoveredTestCase> GetVsTestCases(
			string source,
			ITestFrameworkDiscoverer discoverer,
			VsExecutionDiscoverySink visitor,
			LoggerHelper logger,
			TestPlatformContext testPlatformContext)
		{
			var descriptorProvider = (discoverer as ITestCaseDescriptorProvider) ?? new DefaultTestCaseDescriptorProvider(discoverer);
			var testCases = visitor.TestCases;
			var descriptors = descriptorProvider.GetTestCaseDescriptors(testCases, false);
			var results = new List<DiscoveredTestCase>(descriptors.Count);

			for (var idx = 0; idx < descriptors.Count; ++idx)
			{
				var testCase = new DiscoveredTestCase(source, descriptors[idx], testCases[idx], logger, testPlatformContext);
				if (testCase.VSTestCase is not null)
					results.Add(testCase);
			}

			return results;
		}

		class AssemblyDiscoveredInfo
		{
			public AssemblyDiscoveredInfo(
				string assemblyFileName,
				IList<DiscoveredTestCase> discoveredTestCases)
			{
				AssemblyFileName = assemblyFileName;
				DiscoveredTestCases = discoveredTestCases;
			}

			public string AssemblyFileName { get; }

			public IList<DiscoveredTestCase> DiscoveredTestCases { get; }
		}

		class DiscoveredTestCase
		{
			public string Name { get; }

			public IEnumerable<string> TraitNames { get; }

			public TestCase? VSTestCase { get; }

			public ITestCase TestCase { get; }

			public string UniqueID { get; }

			public DiscoveredTestCase(
				string source,
				TestCaseDescriptor descriptor,
				ITestCase testCase,
				LoggerHelper logger,
				TestPlatformContext testPlatformContext)
			{
				Name = $"{descriptor.ClassName}.{descriptor.MethodName} ({descriptor.UniqueID})";
				TestCase = testCase;
				UniqueID = descriptor.UniqueID;
				VSTestCase = VsDiscoverySink.CreateVsTestCase(source, descriptor, logger, testPlatformContext);
				TraitNames = descriptor.Traits.Keys;
			}
		}
	}
}
