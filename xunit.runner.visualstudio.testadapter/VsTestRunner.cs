using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
            "xunit.runner.utility.win8.dll",
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

        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
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
                stopwatch,
                (source, discoverer, discoveryOptions) => new VsDiscoveryVisitor(source, discoverer, loggerHelper, discoverySink, discoveryOptions, () => cancelled)
            );
        }

        public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("sources", sources);

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

            RunTests(runContext, frameworkHandle, logger, stopwatch, () => GetTests(sources, logger, stopwatch));
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("tests", tests);
            Guard.ArgumentValid("tests", "AppX not supported in this overload", !ContainsAppX(tests.Select(t => t.Source)));

            var stopwatch = Stopwatch.StartNew();
            var logger = new LoggerHelper(frameworkHandle, stopwatch);

            RunTests(
                runContext, frameworkHandle, logger, stopwatch,
                () => tests.GroupBy(testCase => testCase.Source)
                           .Select(group => new AssemblyRunInfo { AssemblyFileName = group.Key, Configuration = ConfigReader.Load(group.Key), TestCases = group })
                           .ToList()
            );
        }

        // Helpers

        static bool ContainsAppX(IEnumerable<string> sources)
        {
            return sources.Any(s => string.Compare(Path.GetExtension(s), ".appx", StringComparison.OrdinalIgnoreCase) == 0);
        }

        void DiscoverTests<TVisitor>(IEnumerable<string> sources,
                                     LoggerHelper logger,
                                     Stopwatch stopwatch,
                                     Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
                                     Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitComplete = null)
            where TVisitor : IVsDiscoveryVisitor, IDisposable
        {
            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                using (AssemblyHelper.SubscribeResolve())
                {
                    foreach (var assemblyFileName in sources)
                    {
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
                                var diagnosticMessageVisitor = new DiagnosticMessageVisitor(logger.InnerLogger, fileName, configuration.DiagnosticMessagesOrDefault, stopwatch);

                                using (var framework = new XunitFrontController(assemblyFileName, configFileName: null, shadowCopy: true, diagnosticMessageSink: diagnosticMessageVisitor))
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

                                        if (configuration.DiagnosticMessagesOrDefault)
                                            logger.Log("Discovering: {0} (method display = {1})", fileName, discoveryOptions.GetMethodDisplayOrDefault());

                                        using (var visitor = visitorFactory(assemblyFileName, framework, discoveryOptions))
                                        {
                                            framework.Find(includeSourceInformation: true, messageSink: visitor, discoveryOptions: discoveryOptions);
                                            var totalTests = visitor.Finish();

                                            if (visitComplete != null)
                                                visitComplete(assemblyFileName, framework, discoveryOptions, visitor);

                                            if (configuration.DiagnosticMessagesOrDefault)
                                                logger.Log("Discovered: {0} ({1} tests)", fileName, totalTests);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            var ex = e.Unwrap();
                            var fileNotFound = ex as FileNotFoundException;
#if !WINDOWS_PHONE_APP && !WINDOWS_PHONE
                            var fileLoad = ex as FileLoadException;
#endif
                            if (fileNotFound != null)
                                logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName));
#if !WINDOWS_PHONE_APP && !WINDOWS_PHONE
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

            stopwatch.Stop();
        }

        static TestProperty GetTestProperty()
        {
            return TestProperty.Register("XunitTestCase", "xUnit.net Test Case", typeof(string), typeof(VsTestRunner));
        }

        List<AssemblyRunInfo> GetTests(IEnumerable<string> sources, LoggerHelper logger, Stopwatch stopwatch)
        {
#if WIN8_STORE
            // For store apps, the files are copied to the AppX dir, we need to load it from there
            sources = sources.Select(s => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Path.GetFileName(s)));
#elif WINDOWS_PHONE_APP
            sources = sources.Select(s => Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(s)));
#endif

            var result = new List<AssemblyRunInfo>();

            DiscoverTests(
                sources,
                logger,
                stopwatch,
                (source, discoverer, discoveryOptions) => new VsExecutionDiscoveryVisitor(),
                (source, discoverer, discoveryOptions, visitor) =>
                    result.Add(
                        new AssemblyRunInfo
                        {
                            AssemblyFileName = source,
                            Configuration = ConfigReader.Load(source),
                            TestCases = visitor.TestCases
                                   .GroupBy(tc => string.Format("{0}.{1}", tc.TestMethod.TestClass.Class.Name, tc.TestMethod.Method.Name))
                                   .SelectMany(group => group.Select(testCase => VsDiscoveryVisitor.CreateVsTestCase(source,
                                                                                                                     discoverer,
                                                                                                                     testCase,
                                                                                                                     forceUniqueNames: group.Count() > 1,
                                                                                                                     logger: logger))
                                                             .Where(vsTestCase => vsTestCase != null))
                                   .ToList()
                        })
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

        void RunTests(IRunContext runContext, IFrameworkHandle frameworkHandle, LoggerHelper logger, Stopwatch stopwatch, Func<List<AssemblyRunInfo>> testCaseAccessor)
        {
            Guard.ArgumentNotNull("runContext", runContext);
            Guard.ArgumentNotNull("frameworkHandle", frameworkHandle);

            Debugger.Break();

            var toDispose = new List<IDisposable>();

            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                cancelled = false;

                var assemblies = testCaseAccessor();
                var parallelizeAssemblies = assemblies.All(runInfo => runInfo.Configuration.ParallelizeAssemblyOrDefault);

                using (AssemblyHelper.SubscribeResolve())
                    if (parallelizeAssemblies)
                        assemblies
                            .Select(runInfo => RunTestsInAssemblyAsync(frameworkHandle, logger, toDispose, runInfo, stopwatch))
                            .ToList()
                            .ForEach(@event => @event.WaitOne());
                    else
                        assemblies
                            .ForEach(runInfo => RunTestsInAssembly(frameworkHandle, logger, toDispose, runInfo, stopwatch));
            }
            finally
            {
                toDispose.ForEach(disposable => disposable.Dispose());
            }
        }

        void RunTestsInAssembly(IFrameworkHandle frameworkHandle,
                                LoggerHelper logger,
                                List<IDisposable> toDispose,
                                AssemblyRunInfo runInfo,
                                Stopwatch stopwatch)
        {
            if (cancelled)
                return;

            var assemblyFileName = runInfo.AssemblyFileName;
            var assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);

            if (runInfo.Configuration.DiagnosticMessagesOrDefault)
                lock (stopwatch)
                    logger.Log("Starting: {0} (parallel test collections = {1}, max threads = {2})",
                               assemblyDisplayName,
                               runInfo.Configuration.ParallelizeTestCollectionsOrDefault ? "on" : "off",
                               runInfo.Configuration.MaxParallelThreadsOrDefault);

#if WIN8_STORE
            // For store apps, the files are copied to the AppX dir, we need to load it from there
            assemblyFileName = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Path.GetFileName(assemblyFileName));
#elif WINDOWS_PHONE_APP
            // For WPA Apps, use the package location
            assemblyFileName = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(assemblyFileName));
#endif

            var diagnosticMessageVisitor = new DiagnosticMessageVisitor(frameworkHandle, assemblyDisplayName, runInfo.Configuration.DiagnosticMessagesOrDefault, stopwatch);
            var controller = new XunitFrontController(assemblyFileName, configFileName: null, shadowCopy: true, diagnosticMessageSink: diagnosticMessageVisitor);

            lock (toDispose)
                toDispose.Add(controller);

            var xunitTestCases = runInfo.TestCases.ToDictionary(tc => controller.Deserialize(tc.GetPropertyValue<string>(SerializedTestCaseProperty, null)));
            var executionOptions = TestFrameworkOptions.ForExecution(runInfo.Configuration);

            using (var executionVisitor = new VsExecutionVisitor(frameworkHandle, logger, xunitTestCases, executionOptions, () => cancelled))
            {
                controller.RunTests(xunitTestCases.Keys.ToList(), executionVisitor, executionOptions);
                executionVisitor.Finished.WaitOne();
            }

            if (runInfo.Configuration.DiagnosticMessagesOrDefault)
                logger.Log("Finished: {0}", assemblyDisplayName);
        }

        ManualResetEvent RunTestsInAssemblyAsync(IFrameworkHandle frameworkHandle,
                                                 LoggerHelper logger,
                                                 List<IDisposable> toDispose,
                                                 AssemblyRunInfo runInfo,
                                                 Stopwatch stopwatch)
        {
            var @event = new ManualResetEvent(initialState: false);
            Action handler = () =>
            {
                try
                {
                    RunTestsInAssembly(frameworkHandle, logger, toDispose, runInfo, stopwatch);
                }
                finally
                {
                    @event.Set();
                }
            };

#if WINDOWS_PHONE_APP
            var fireAndForget = Windows.System.Threading.ThreadPool.RunAsync(_ => handler());
#else
            ThreadPool.QueueUserWorkItem(_ => handler());
#endif

            return @event;
        }


        class Grouping<TKey, TElement> : IGrouping<TKey, TElement>
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
