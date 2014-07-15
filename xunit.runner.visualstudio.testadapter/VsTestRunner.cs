﻿using System;
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
using Xunit.Runner.VisualStudio.Settings;
using Xunit.Abstractions;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(Constants.ExecutorUri)]
    [ExtensionUri(Constants.ExecutorUri)]
    public class VsTestRunner : ITestDiscoverer, ITestExecutor
    {
        public static TestProperty SerializedTestCaseProperty = GetTestProperty();

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

            DiscoverTests(
                sources,
                logger,
                SettingsProvider.Load(),
                (source, discoverer) => new VsDiscoveryVisitor(source, discoverer, logger, discoveryContext, discoverySink, () => cancelled)
            );
        }

        void DiscoverTests<TVisitor>(IEnumerable<string> sources,
                                     IMessageLogger logger,
                                     XunitVisualStudioSettings settings,
                                     Func<string, ITestFrameworkDiscoverer, TVisitor> visitorFactory,
                                     Action<string, ITestFrameworkDiscoverer, TVisitor> visitComplete = null,
                                     Stopwatch stopwatch = null)
            where TVisitor : IVsDiscoveryVisitor
        {
            if (stopwatch == null)
                stopwatch = Stopwatch.StartNew();

            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                    logger.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Discovery started", stopwatch.Elapsed));

                string configurationFile = string.IsNullOrEmpty(settings.ConfigurationFile) ? null : Environment.ExpandEnvironmentVariables(settings.ConfigurationFile);

                using (AssemblyHelper.SubscribeResolve())
                {
                    foreach (string assemblyFileName in sources)
                    {
                        var fileName = Path.GetFileName(assemblyFileName);

                        try
                        {
                            if (cancelled)
                                break;

                            if (!IsXunitTestAssembly(assemblyFileName))
                            {
                                if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                                    logger.SendMessage(TestMessageLevel.Informational,
                                                       String.Format("[xUnit.net {0}] Skipping: {1} (no reference to xUnit.net)", stopwatch.Elapsed, fileName));
                            }
                            else
                            {
                                using (var framework = new XunitFrontController(assemblyFileName, configFileName: configurationFile, shadowCopy: !settings.DoNotShadowCopy))
                                using (var visitor = visitorFactory(assemblyFileName, framework))
                                {
                                    var targetFramework = framework.TargetFramework;
                                    if (targetFramework.StartsWith("MonoTouch", StringComparison.OrdinalIgnoreCase) ||
                                        targetFramework.StartsWith("MonoAndroid", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                                            logger.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Skipping: {1} (unsupported target framework '{2}')", stopwatch.Elapsed, fileName, targetFramework));
                                    }
                                    else
                                    {
                                        if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                                        {
                                            logger.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Discovery starting: {1}", stopwatch.Elapsed, fileName));
                                            logger.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Configuration File: {1}", stopwatch.Elapsed, configurationFile == null ? "*Default*" : configurationFile));
                                        }

                                        framework.Find(includeSourceInformation: true, messageSink: visitor, options: new TestFrameworkOptions());
                                        var totalTests = visitor.Finish();

                                        if (visitComplete != null)
                                            visitComplete(assemblyFileName, framework, visitor);

                                        if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                                            logger.SendMessage(TestMessageLevel.Informational,
                                                               String.Format("[xUnit.net {0}] Discovery finished: {1} ({2} tests)", stopwatch.Elapsed, fileName, totalTests));
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            var ex = e.Unwrap();
                            var fileNotFound = ex as FileNotFoundException;
                            var fileLoad = ex as FileLoadException;
                            if (fileNotFound != null)
                                logger.SendMessage(TestMessageLevel.Informational,
                                                   String.Format("[xUnit.net {0}] Skipping: {1} (could not find dependent assembly '{2}')", stopwatch.Elapsed, fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName)));
                            else if (fileLoad != null)
                                logger.SendMessage(TestMessageLevel.Informational,
                                                   String.Format("[xUnit.net {0}] Skipping: {1} (could not find dependent assembly '{2}')", stopwatch.Elapsed, fileName, Path.GetFileNameWithoutExtension(fileLoad.FileName)));
                            else
                                logger.SendMessage(TestMessageLevel.Error,
                                                   String.Format("[xUnit.net {0}] Exception discovering tests from {1}: {2}", stopwatch.Elapsed, fileName, ex));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.SendMessage(TestMessageLevel.Error,
                                   String.Format("[xUnit.net {0}] Exception discovering tests: {1}", stopwatch.Elapsed, e.Unwrap()));
            }

            stopwatch.Stop();

            if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                logger.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Discovery complete", stopwatch.Elapsed));
        }

        static TestProperty GetTestProperty()
        {
            return TestProperty.Register("XunitTestCase", "xUnit.net Test Case", typeof(string), typeof(VsTestRunner));
        }

        IEnumerable<IGrouping<string, TestCase>> GetTests(IEnumerable<string> sources, IRunContext runContext, IMessageLogger logger, XunitVisualStudioSettings settings, Stopwatch stopwatch)
        {
            var result = new List<IGrouping<string, TestCase>>();

            DiscoverTests(
                sources,
                logger,
                settings,
                (source, discoverer) => new VsExecutionDiscoveryVisitor(),
                (source, discoverer, visitor) =>
                {
                    HashSet<string> knownTraitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    IGrouping<string, TestCase> grouping = new Grouping<string, TestCase>(
                        source,
                        visitor.TestCases
                            .GroupBy(tc => String.Format("{0}.{1}", tc.TestMethod.TestClass.Class.Name, tc.TestMethod.Method.Name))
                            .SelectMany(group => group.Select(testCase => VsDiscoveryVisitor.CreateVsTestCase(source, discoverer, testCase, settings, forceUniqueNames: group.Count() > 1, knownTraitNames: knownTraitNames)))
                            .ToList());

                    var filterHelper = new TestCaseFilterHelper(knownTraitNames);
                    grouping = filterHelper.GetFilteredTestList(grouping, runContext, logger, stopwatch, source);
                    result.Add(grouping);
                },
                stopwatch
            );

            return result;
        }

        static bool IsXunitTestAssembly(string assemblyFileName)
        {
            // Don't try to load ourselves, since we fail (issue #47). Also, Visual Studio Online is brain dead.
            var self = Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().GetLocalCodeBase());
            if (Path.GetFileNameWithoutExtension(assemblyFileName).Equals(self, StringComparison.OrdinalIgnoreCase))
                return false;

            var xunitPath = Path.Combine(Path.GetDirectoryName(assemblyFileName), "xunit.dll");
            var xunitExecutionPath = Path.Combine(Path.GetDirectoryName(assemblyFileName), "xunit.execution.dll");
            return File.Exists(xunitPath) || File.Exists(xunitExecutionPath);
        }

        public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("sources", sources);

            var stopwatch = Stopwatch.StartNew();
            RunTests(runContext, frameworkHandle, stopwatch, settings => GetTests(sources, runContext, frameworkHandle, settings, stopwatch));
            stopwatch.Stop();
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            Guard.ArgumentNotNull("tests", tests);

            var stopwatch = Stopwatch.StartNew();
            RunTests(runContext, frameworkHandle, stopwatch, settings => tests.GroupBy(testCase => testCase.Source));
            stopwatch.Stop();
        }

        void RunTests(IRunContext runContext, IFrameworkHandle frameworkHandle, Stopwatch stopwatch, Func<XunitVisualStudioSettings, IEnumerable<IGrouping<string, TestCase>>> testCaseAccessor)
        {
            Guard.ArgumentNotNull("runContext", runContext);
            Guard.ArgumentNotNull("frameworkHandle", frameworkHandle);

            var settings = SettingsProvider.Load();

            if (!runContext.KeepAlive || settings.ShutdownAfterRun)
                frameworkHandle.EnableShutdownAfterTestRun = true;

            var toDispose = new List<IDisposable>();

            if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                lock (stopwatch)
                {
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Execution started", stopwatch.Elapsed));
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Settings: MaxParallelThreads = {1}, NameDisplay = {2}, ParallelizeAssemblies = {3}, ParallelizeTestCollections = {4}, ShutdownAfterRun = {5}, DoNotShadowCopy = {6}",
                                                                                              stopwatch.Elapsed,
                                                                                              settings.MaxParallelThreads,
                                                                                              settings.NameDisplay,
                                                                                              settings.ParallelizeAssemblies,
                                                                                              settings.ParallelizeTestCollections,
                                                                                              settings.ShutdownAfterRun,
                                                                                              settings.DoNotShadowCopy));
                }

            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                cancelled = false;

                using (AssemblyHelper.SubscribeResolve())
                    if (settings.ParallelizeAssemblies)
                        testCaseAccessor(settings)
                            .Select(testCaseGroup => RunTestsInAssemblyAsync(runContext, frameworkHandle, toDispose, testCaseGroup.Key, testCaseGroup, settings, stopwatch))
                            .ToList()
                            .ForEach(@event => @event.WaitOne());
                    else
                        testCaseAccessor(settings)
                            .ToList()
                            .ForEach(testCaseGroup => RunTestsInAssembly(runContext, frameworkHandle, toDispose, testCaseGroup.Key, testCaseGroup, settings, stopwatch));
            }
            finally
            {
                if (settings.ShutdownAfterRun)
                    toDispose.ForEach(disposable => disposable.Dispose());
            }

            if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                lock (stopwatch)
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Execution complete", stopwatch.Elapsed));
        }

        void RunTestsInAssembly(IDiscoveryContext discoveryContext,
                                IFrameworkHandle frameworkHandle,
                                List<IDisposable> toDispose,
                                string assemblyFileName,
                                IEnumerable<TestCase> testCases,
                                XunitVisualStudioSettings settings,
                                Stopwatch stopwatch)
        {
            if (cancelled)
                return;

            string configurationFile = string.IsNullOrEmpty(settings.ConfigurationFile) ? null : Environment.ExpandEnvironmentVariables(settings.ConfigurationFile);

            if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                lock (stopwatch)
                {
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Execution starting: {1}", stopwatch.Elapsed, Path.GetFileName(assemblyFileName)));
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Configuration File: {1}", stopwatch.Elapsed, configurationFile == null ? "*Default*" : configurationFile));
                }

            var controller = new XunitFrontController(assemblyFileName, configFileName: configurationFile, shadowCopy: !settings.DoNotShadowCopy);

            lock (toDispose)
                toDispose.Add(controller);

            var xunitTestCases = testCases.ToDictionary(tc => controller.Deserialize(tc.GetPropertyValue<string>(SerializedTestCaseProperty, null)));

            using (var executionVisitor = new VsExecutionVisitor(discoveryContext, frameworkHandle, xunitTestCases, () => cancelled))
            {
                var executionOptions = new XunitExecutionOptions
                {
                    DisableParallelization = !settings.ParallelizeTestCollections,
                    MaxParallelThreads = settings.MaxParallelThreads
                };

                controller.RunTests(xunitTestCases.Keys.ToList(), executionVisitor, executionOptions);
                executionVisitor.Finished.WaitOne();
            }

            if (settings.MessageDisplay == MessageDisplay.Diagnostic)
                lock (stopwatch)
                    frameworkHandle.SendMessage(TestMessageLevel.Informational, String.Format("[xUnit.net {0}] Execution finished: {1}", stopwatch.Elapsed, Path.GetFileName(assemblyFileName)));
        }

        ManualResetEvent RunTestsInAssemblyAsync(IDiscoveryContext discoveryContext,
                                                 IFrameworkHandle frameworkHandle,
                                                 List<IDisposable> toDispose,
                                                 string assemblyFileName,
                                                 IEnumerable<TestCase> testCases,
                                                 XunitVisualStudioSettings settings,
                                                 Stopwatch stopwatch)
        {
            var @event = new ManualResetEvent(initialState: false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RunTestsInAssembly(discoveryContext, frameworkHandle, toDispose, assemblyFileName, testCases, settings, stopwatch);
                }
                finally
                {
                    @event.Set();
                }
            });

            return @event;
        }
    }
}
