using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    [FileExtension(".appx")]
    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(Constants.ExecutorUri)]
    [ExtensionUri(Constants.ExecutorUri)]
    public class VsTestRunner : ITestDiscoverer, ITestExecutor
    {
        public static TestProperty SerializedTestCaseProperty = GetTestProperty();

#if PLATFORM_DOTNET
        AppDomainSupport AppDomain = AppDomainSupport.Denied;
#else
        AppDomainSupport AppDomain = AppDomainSupport.Required;
#endif

        static readonly HashSet<string> platformAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft.visualstudio.testplatform.unittestframework.dll",
            "microsoft.visualstudio.testplatform.core.dll",
            "microsoft.visualstudio.testplatform.testexecutor.core.dll",
            "microsoft.visualstudio.testplatform.extensions.msappcontaineradapter.dll",
            "microsoft.visualstudio.testplatform.objectmodel.dll",
            "microsoft.visualstudio.testplatform.utilities.dll",
            "vstest.executionengine.appcontainer.exe",
            "vstest.executionengine.appcontainer.x86.exe",
            "xunit.execution.desktop.dll",
            "xunit.execution.dotnet.dll",
            "xunit.execution.win8.dll",
            "xunit.execution.universal.dll",
            "xunit.runner.utility.desktop.dll",
            "xunit.runner.utility.dotnet.dll",
            "xunit.runner.visualstudio.testadapter.dll",
            "xunit.runner.visualstudio.uwp.dll",
            "xunit.runner.visualstudio.win81.dll",
            "xunit.runner.visualstudio.wpa81.dll",
            "xunit.core.dll",
            "xunit.assert.dll",
            "xunit.dll"
        };

        bool cancelled;

        public void Cancel()
        {
            cancelled = true;
        }

        void ITestDiscoverer.DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            Guard.ArgumentNotNull("sources", sources);
            Guard.ArgumentNotNull("logger", logger);
            Guard.ArgumentNotNull("discoverySink", discoverySink);
            Guard.ArgumentValid("sources", "AppX not supported for discovery", !ContainsAppX(sources));

            var stopwatch = Stopwatch.StartNew();
            var loggerHelper = new LoggerHelper(logger, stopwatch);

            DiscoverTests(
                sources,
                loggerHelper,
                (source, discoverer, discoveryOptions) => new VsDiscoveryVisitor(source, discoverer, loggerHelper, discoverySink, discoveryOptions, () => cancelled)
            );
        }

        void ITestExecutor.RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("sources", sources);

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);

            // In this case, we need to go thru the files manually
            if (ContainsAppX(sources))
            {
#if PLATFORM_DOTNET
                var sourcePath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
#else
                var sourcePath = Environment.CurrentDirectory;
#endif
                sources = Directory.GetFiles(sourcePath, "*.dll")
                                   .Where(file => !platformAssemblies.Contains(Path.GetFileName(file)))
                                   .ToList();
            }

            RunTests(runContext, frameworkHandle, logger, () => GetTests(sources, logger, runContext));
        }

        void ITestExecutor.RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("tests", tests);
            Guard.ArgumentValid("tests", "AppX not supported in this overload", !ContainsAppX(tests.Select(t => t.Source)));

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);

            RunTests(
                runContext, frameworkHandle, logger,
                () => tests.GroupBy(testCase => testCase.Source)
                           .Select(group => new AssemblyRunInfo { AssemblyFileName = group.Key, Configuration = LoadConfiguration(group.Key), TestCases = group.ToList() })
                           .ToList()
            );
        }

        // Helpers

        static bool ContainsAppX(IEnumerable<string> sources)
            => sources.Any(s => string.Compare(Path.GetExtension(s), ".appx", StringComparison.OrdinalIgnoreCase) == 0);

        static ITestCase Deserialize(LoggerHelper logger, ITestFrameworkExecutor executor, TestCase testCase)
        {
            try
            {
                return executor.Deserialize(testCase.GetPropertyValue<string>(SerializedTestCaseProperty, null));
            }
            catch (Exception ex)
            {
                logger.LogError("Unable to de-serialize test case {0}: {1}", testCase.DisplayName, ex);
                return null;
            }
        }

        void DiscoverTests<TVisitor>(IEnumerable<string> sources,
                                     LoggerHelper logger,
                                     Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
                                     Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitComplete = null)
            where TVisitor : IVsDiscoveryVisitor, IDisposable
        {
            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                using (AssemblyHelper.SubscribeResolve())
                {
                    var reporterMessageHandler = new DefaultRunnerReporter().CreateMessageHandler(new VisualStudioRunnerLogger(logger));

                    foreach (var assemblyFileName in sources)
                    {
                        var assembly = new XunitProjectAssembly { AssemblyFilename = assemblyFileName };
                        var configuration = LoadConfiguration(assemblyFileName);
                        var fileName = Path.GetFileNameWithoutExtension(assemblyFileName);
                        var shadowCopy = configuration.ShadowCopyOrDefault;

                        try
                        {
                            if (cancelled)
                                break;

                            if (!IsXunitTestAssembly(assemblyFileName))
                            {
                                if (configuration.DiagnosticMessagesOrDefault)
                                    logger.Log("Skipping: {0} (no reference to xUnit.net)", fileName);
                            }
                            else
                            {
                                var diagnosticMessageVisitor = new DiagnosticMessageVisitor(logger, fileName, configuration.DiagnosticMessagesOrDefault);

                                using (var framework = new XunitFrontController(AppDomain, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: shadowCopy, diagnosticMessageSink: diagnosticMessageVisitor))
                                {
                                    var targetFramework = framework.TargetFramework;
                                    if (targetFramework.StartsWith("MonoTouch", StringComparison.OrdinalIgnoreCase) ||
                                        targetFramework.StartsWith("MonoAndroid", StringComparison.OrdinalIgnoreCase) ||
                                        targetFramework.StartsWith("Xamarin.iOS", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (configuration.DiagnosticMessagesOrDefault)
                                            logger.Log("Skipping: {0} (unsupported target framework '{1}')", fileName, targetFramework);
                                    }
                                    else
                                    {
                                        var discoveryOptions = TestFrameworkOptions.ForDiscovery(configuration);

                                        using (var visitor = visitorFactory(assemblyFileName, framework, discoveryOptions))
                                        {
                                            reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting(assembly, framework.CanUseAppDomains && AppDomain != AppDomainSupport.Denied, shadowCopy, discoveryOptions));

                                            framework.Find(includeSourceInformation: true, messageSink: visitor, discoveryOptions: discoveryOptions);
                                            var totalTests = visitor.Finish();

                                            if (visitComplete != null)
                                                visitComplete(assemblyFileName, framework, discoveryOptions, visitor);

                                            reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryFinished(assembly, discoveryOptions, totalTests, totalTests));
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            var ex = e.Unwrap();
                            var fileNotFound = ex as FileNotFoundException;
#if !PLATFORM_DOTNET
                            var fileLoad = ex as FileLoadException;
#endif
                            if (ex is InvalidOperationException)
                                logger.LogWarning("Skipping: {0} ({1})", fileName, ex.Message);
                            else if (fileNotFound != null)
                                logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName));
#if !PLATFORM_DOTNET
                            else if (fileLoad != null)
                                logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileLoad.FileName));
#endif
                            else
                                logger.LogWarning("Exception discovering tests from {0}: {1}", fileName, ex);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Exception discovering tests: {0}", e.Unwrap());
            }
        }

        static Stream GetConfigurationStreamForAssembly(string assemblyName)
        {
            // See if there's a directory with the assm name. this might be the case for appx
            if (Directory.Exists(assemblyName))
            {
                if (File.Exists(Path.Combine(assemblyName, $"{assemblyName}.xunit.runner.json")))
                    return File.OpenRead(Path.Combine(assemblyName, $"{assemblyName}.xunit.runner.json"));

                if (File.Exists(Path.Combine(assemblyName, "xunit.runner.json")))
                    return File.OpenRead(Path.Combine(assemblyName, "xunit.runner.json"));
            }

            // Fallback to working dir
            if (File.Exists($"{assemblyName}.xunit.runner.json"))
                return File.OpenRead($"{assemblyName}.xunit.runner.json");

            if (File.Exists("xunit.runner.json"))
                return File.OpenRead("xunit.runner.json");

            return null;
        }

        static TestProperty GetTestProperty()
            => TestProperty.Register("XunitTestCase", "xUnit.net Test Case", typeof(string), typeof(VsTestRunner));

        List<AssemblyRunInfo> GetTests(IEnumerable<string> sources, LoggerHelper logger, IRunContext runContext)
        {
            // For store apps, the files are copied to the AppX dir, we need to load it from there
#if PLATFORM_DOTNET
            sources = sources.Select(s => Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(s)));
#endif

            var result = new List<AssemblyRunInfo>();
            var knownTraitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            DiscoverTests(
                sources,
                logger,
                (source, discoverer, discoveryOptions) => new VsExecutionDiscoveryVisitor(),
                (source, discoverer, discoveryOptions, visitor) =>
                {
                    var vsFilteredTestCases = visitor.TestCases.Select(testCase => VsDiscoveryVisitor.CreateVsTestCase(source, discoverer, testCase, false, logger: logger, knownTraitNames: knownTraitNames)).ToList();

                    // Apply any filtering
                    var filterHelper = new TestCaseFilterHelper(knownTraitNames);
                    vsFilteredTestCases = filterHelper.GetFilteredTestList(vsFilteredTestCases, runContext, logger, source).ToList();

                    // Re-create testcases with unique names if there is more than 1
                    var testCases = visitor.TestCases.Where(tc => vsFilteredTestCases.Any(vsTc => vsTc.DisplayName == tc.DisplayName)).GroupBy(tc => $"{tc.TestMethod.TestClass.Class.Name}.{tc.TestMethod.Method.Name}")
                                                        .SelectMany(group => group.Select(testCase =>
                                                                                                    VsDiscoveryVisitor.CreateVsTestCase(
                                                                                                        source,
                                                                                                        discoverer,
                                                                                                        testCase,
                                                                                                        forceUniqueNames: group.Count() > 1,
                                                                                                        logger: logger,
                                                                                                        knownTraitNames: knownTraitNames))
                                                        .Where(vsTestCase => vsTestCase != null)).ToList(); // pre-enumerate these as it populates the known trait names collection

                    var runInfo = new AssemblyRunInfo
                    {
                        AssemblyFileName = source,
                        Configuration = LoadConfiguration(source),
                        TestCases = testCases
                    };
                    result.Add(runInfo);
                }
            );

            return result;
        }

        static bool IsXunitTestAssembly(string assemblyFileName)
        {
            // Don't try to load ourselves (or any test framework assemblies), since we fail (issue #47 in xunit/xunit).
            if (platformAssemblies.Contains(Path.GetFileName(assemblyFileName)))
                return false;

            var assemblyFolder = Path.GetDirectoryName(assemblyFileName);
            return File.Exists(Path.Combine(assemblyFolder, "xunit.dll"))
                || Directory.GetFiles(assemblyFolder, "xunit.execution.*.dll").Length > 0;
        }

        static TestAssemblyConfiguration LoadConfiguration(string assemblyName)
        {
#if PLATFORM_DOTNET
            var stream = GetConfigurationStreamForAssembly(assemblyName);
            return stream == null ? new TestAssemblyConfiguration() : ConfigReader.Load(stream);
#else
            return ConfigReader.Load(assemblyName);
#endif
        }

        void RunTests(IRunContext runContext, IFrameworkHandle frameworkHandle, LoggerHelper logger, Func<List<AssemblyRunInfo>> testCaseAccessor)
        {
            Guard.ArgumentNotNull("runContext", runContext);
            Guard.ArgumentNotNull("frameworkHandle", frameworkHandle);

            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                cancelled = false;

                var assemblies = testCaseAccessor();
                var parallelizeAssemblies = assemblies.All(runInfo => runInfo.Configuration.ParallelizeAssemblyOrDefault);
                var reporterMessageHandler = new DefaultRunnerReporter().CreateMessageHandler(new VisualStudioRunnerLogger(logger));

                using (AssemblyHelper.SubscribeResolve())
                    if (parallelizeAssemblies)
                        assemblies
                            .Select(runInfo => RunTestsInAssemblyAsync(frameworkHandle, logger, reporterMessageHandler, runInfo))
                            .ToList()
                            .ForEach(@event => @event.WaitOne());
                    else
                        assemblies
                            .ForEach(runInfo => RunTestsInAssembly(frameworkHandle, logger, reporterMessageHandler, runInfo));
            }
            catch (Exception ex)
            {
                logger.LogError("Catastrophic failure: {0}", ex);
            }
        }

        void RunTestsInAssembly(IFrameworkHandle frameworkHandle,
                                LoggerHelper logger,
                                IMessageSink reporterMessageHandler,
                                AssemblyRunInfo runInfo)
        {
            if (cancelled)
                return;

            var assembly = new XunitProjectAssembly { AssemblyFilename = runInfo.AssemblyFileName };
            var assemblyFileName = runInfo.AssemblyFileName;
            var assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
            var shadowCopy = assembly.Configuration.ShadowCopyOrDefault;

            try
            {
#if PLATFORM_DOTNET
                // For AppX Apps, use the package location
                assemblyFileName = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(assemblyFileName));
#endif

                var diagnosticMessageVisitor = new DiagnosticMessageVisitor(logger, assemblyDisplayName, runInfo.Configuration.DiagnosticMessagesOrDefault);
                using (var controller = new XunitFrontController(AppDomain, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: shadowCopy, diagnosticMessageSink: diagnosticMessageVisitor))
                {
                    var xunitTestCases = runInfo.TestCases.Select(tc => new { vs = tc, xunit = Deserialize(logger, controller, tc) })
                                                          .Where(tc => tc.xunit != null)
                                                          .ToDictionary(tc => tc.xunit, tc => tc.vs);
                    var executionOptions = TestFrameworkOptions.ForExecution(runInfo.Configuration);

                    reporterMessageHandler.OnMessage(new TestAssemblyExecutionStarting(assembly, executionOptions));

                    using (var executionVisitor = new VsExecutionVisitor(frameworkHandle, logger, xunitTestCases, executionOptions, () => cancelled))
                    {
                        controller.RunTests(xunitTestCases.Keys.ToList(), executionVisitor, executionOptions);
                        executionVisitor.Finished.WaitOne();

                        reporterMessageHandler.OnMessage(new TestAssemblyExecutionFinished(assembly, executionOptions, executionVisitor.ExecutionSummary));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("{0}: Catastrophic failure: {1}", assemblyDisplayName, ex);
            }
        }

        ManualResetEvent RunTestsInAssemblyAsync(IFrameworkHandle frameworkHandle,
                                                 LoggerHelper logger,
                                                 IMessageSink reporterMessageHandler,
                                                 AssemblyRunInfo runInfo)
        {
            var @event = new ManualResetEvent(initialState: false);
            Action handler = () =>
            {
                try
                {
                    RunTestsInAssembly(frameworkHandle, logger, reporterMessageHandler, runInfo);
                }
                finally
                {
                    @event.Set();
                }
            };

#if PLATFORM_DOTNET
            var fireAndForget = Windows.System.Threading.ThreadPool.RunAsync(_ => handler());
#else
            ThreadPool.QueueUserWorkItem(_ => handler());
#endif

            return @event;
        }


        internal class Grouping<TKey, TElement> : IGrouping<TKey, TElement>
        {
            readonly IEnumerable<TElement> elements;

            public Grouping(TKey key, IEnumerable<TElement> elements)
            {
                Key = key;
                this.elements = elements;
            }

            public TKey Key { get; private set; }

            public IEnumerator<TElement> GetEnumerator()
            {
                return elements.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return elements.GetEnumerator();
            }
        }
    }
}
