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
            "xunit.execution.win8.dll",
            "xunit.execution.universal.dll",
            "xunit.runner.utility.desktop.dll",
            "xunit.runner.utility.universal.dll",
            "xunit.runner.visualstudio.testadapter.dll",
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

#if WINDOWS_UAP
            ConfigReader_Json.FileOpenRead = File.OpenRead;
#endif

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

#if WINDOWS_UAP
            ConfigReader_Json.FileOpenRead = File.OpenRead;
#endif

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);

            // In this case, we need to go thru the files manually
            if (ContainsAppX(sources))
            {
#if WINDOWS_PHONE_APP || WINDOWS_APP
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

#if WINDOWS_UAP
            ConfigReader_Json.FileOpenRead = File.OpenRead;
#endif

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);

            RunTests(
                runContext, frameworkHandle, logger,
                () => tests.GroupBy(testCase => testCase.Source)
                           .Select(group => new AssemblyRunInfo { AssemblyFileName = group.Key, Configuration = ConfigReader.Load(group.Key), TestCases = group.ToList() })
                           .ToList()
            );
        }

        // Helpers

        static bool ContainsAppX(IEnumerable<string> sources)
        {
            return sources.Any(s => string.Compare(Path.GetExtension(s), ".appx", StringComparison.OrdinalIgnoreCase) == 0);
        }

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
                        var configuration = ConfigReader.Load(assemblyFileName);
                        var fileName = Path.GetFileNameWithoutExtension(assemblyFileName);

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

                                using (var framework = new XunitFrontController(useAppDomain: true, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: true, diagnosticMessageSink: diagnosticMessageVisitor))
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
                                            reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting(assembly, configuration.UseAppDomainOrDefault, discoveryOptions));

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
#if !WINDOWS_PHONE_APP && !WINDOWS_PHONE && !WINDOWS_APP
                            var fileLoad = ex as FileLoadException;
#endif
                            if (fileNotFound != null)
                                logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName));
#if !WINDOWS_PHONE_APP && !WINDOWS_PHONE && !WINDOWS_APP
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

        static TestProperty GetTestProperty()
        {
            return TestProperty.Register("XunitTestCase", "xUnit.net Test Case", typeof(string), typeof(VsTestRunner));
        }

        List<AssemblyRunInfo> GetTests(IEnumerable<string> sources, LoggerHelper logger, IRunContext runContext)
        {
            // For store apps, the files are copied to the AppX dir, we need to load it from there
#if WINDOWS_PHONE_APP || WINDOWS_APP
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
                    var testCases = visitor.TestCases
                                           .GroupBy(tc => string.Format("{0}.{1}", tc.TestMethod.TestClass.Class.Name, tc.TestMethod.Method.Name))
                                           .SelectMany(group => group.Select(testCase => VsDiscoveryVisitor.CreateVsTestCase(source, discoverer, testCase,
                                                                                                                             forceUniqueNames: group.Count() > 1,
                                                                                                                             logger: logger,
                                                                                                                             knownTraitNames: knownTraitNames))
                                                                     .Where(vsTestCase => vsTestCase != null))
                                           .ToList(); // pre-enumerate these as it populates the known trait names collection

                    // Apply any filtering
                    var filterHelper = new TestCaseFilterHelper(knownTraitNames);
                    testCases = filterHelper.GetFilteredTestList(testCases, runContext, logger, source).ToList();

                    var runInfo = new AssemblyRunInfo
                    {
                        AssemblyFileName = source,
                        Configuration = ConfigReader.Load(source),
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

            var xunitPath = Path.Combine(Path.GetDirectoryName(assemblyFileName), "xunit.dll");
            var xunitExecutionPath = Path.Combine(Path.GetDirectoryName(assemblyFileName), ExecutionHelper.AssemblyName);
            return File.Exists(xunitPath) || File.Exists(xunitExecutionPath);
        }

        void RunTests(IRunContext runContext, IFrameworkHandle frameworkHandle, LoggerHelper logger, Func<List<AssemblyRunInfo>> testCaseAccessor)
        {
            Guard.ArgumentNotNull("runContext", runContext);
            Guard.ArgumentNotNull("frameworkHandle", frameworkHandle);

            var toDispose = new List<IDisposable>();

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
                            .Select(runInfo => RunTestsInAssemblyAsync(frameworkHandle, logger, reporterMessageHandler, toDispose, runInfo))
                            .ToList()
                            .ForEach(@event => @event.WaitOne());
                    else
                        assemblies
                            .ForEach(runInfo => RunTestsInAssembly(frameworkHandle, logger, reporterMessageHandler, toDispose, runInfo));
            }
            catch (Exception ex)
            {
                logger.LogError("Catastrophic failure: {0}", ex);
            }
            finally
            {
                toDispose.ForEach(disposable => disposable.Dispose());
            }
        }

        void RunTestsInAssembly(IFrameworkHandle frameworkHandle,
                                LoggerHelper logger,
                                IMessageSink reporterMessageHandler,
                                List<IDisposable> toDispose,
                                AssemblyRunInfo runInfo)
        {
            if (cancelled)
                return;

            var assembly = new XunitProjectAssembly { AssemblyFilename = runInfo.AssemblyFileName };
            var assemblyFileName = runInfo.AssemblyFileName;
            var assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);

            try
            {
#if WINDOWS_PHONE_APP || WINDOWS_APP
                // For AppX Apps, use the package location
                assemblyFileName = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(assemblyFileName));
#endif

                var diagnosticMessageVisitor = new DiagnosticMessageVisitor(logger, assemblyDisplayName, runInfo.Configuration.DiagnosticMessagesOrDefault);
                var controller = new XunitFrontController(useAppDomain: true, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: true, diagnosticMessageSink: diagnosticMessageVisitor);

                lock (toDispose)
                    toDispose.Add(controller);

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
            catch (Exception ex)
            {
                logger.LogError("{0}: Catastrophic failure: {1}", assemblyDisplayName, ex);
            }
        }

        ManualResetEvent RunTestsInAssemblyAsync(IFrameworkHandle frameworkHandle,
                                                 LoggerHelper logger,
                                                 IMessageSink reporterMessageHandler,
                                                 List<IDisposable> toDispose,
                                                 AssemblyRunInfo runInfo)
        {
            var @event = new ManualResetEvent(initialState: false);
            Action handler = () =>
            {
                try
                {
                    RunTestsInAssembly(frameworkHandle, logger, reporterMessageHandler, toDispose, runInfo);
                }
                finally
                {
                    @event.Set();
                }
            };

#if WINDOWS_PHONE_APP || WINDOWS_APP
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
