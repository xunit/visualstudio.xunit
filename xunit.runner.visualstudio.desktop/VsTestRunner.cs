using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

#if NETCOREAPP1_0
using System.Reflection;
using System.Text;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.DependencyModel;
#endif

#if NET35
using System.Reflection;
#endif

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

#if WINDOWS_UAP || NETCOREAPP1_0
        static readonly AppDomainSupport AppDomainDefaultBehavior = AppDomainSupport.Denied;
#else
        static readonly AppDomainSupport AppDomainDefaultBehavior = AppDomainSupport.Required;
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
                discoveryContext?.RunSettings,
                sources,
                loggerHelper,
                (source, discoverer, discoveryOptions) => new VsDiscoverySink(source, discoverer, loggerHelper, discoverySink, discoveryOptions, () => cancelled)
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
#if WINDOWS_UAP
                var sourcePath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
#elif NETCOREAPP1_0
                var sourcePath = Directory.GetCurrentDirectory();
#else
                var sourcePath = Environment.CurrentDirectory;
#endif
                sources = Directory.GetFiles(sourcePath, "*.dll")
                                   .Where(file => !platformAssemblies.Contains(Path.GetFileName(file)))
                                   .ToList();

                ((List<string>)sources).AddRange(Directory.GetFiles(sourcePath, "*.exe")
                                       .Where(file => !platformAssemblies.Contains(Path.GetFileName(file))));
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

        void DiscoverTests<TVisitor>(IRunSettings runSettings,
                                     IEnumerable<string> sources,
                                     LoggerHelper logger,
                                     Func<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitorFactory,
                                     Action<string, ITestFrameworkDiscoverer, ITestFrameworkDiscoveryOptions, TVisitor> visitComplete = null)
            where TVisitor : IVsDiscoverySink, IDisposable
        {
            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                using (AssemblyHelper.SubscribeResolve())
                {

#if NET35 || NETCOREAPP1_0
                    // Reads settings like disabling appdomains, parallel etc.
                    // Do this first before invoking any thing else to ensure correct settings for the run
                    RunSettingsHelper.ReadRunSettings(runSettings?.SettingsXml);
#endif

                    var reporterMessageHandler = GetRunnerReporter(sources).CreateMessageHandler(new VisualStudioRunnerLogger(logger));

                    foreach (var assemblyFileNameCanBeWithoutAbsolutePath in sources)
                    {
                        var assemblyFileName = assemblyFileNameCanBeWithoutAbsolutePath;
#if !WINDOWS_UAP
                        assemblyFileName = Path.GetFullPath(assemblyFileNameCanBeWithoutAbsolutePath);
#endif
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
                                var diagnosticSink = new DiagnosticMessageSink(logger, fileName, configuration.DiagnosticMessagesOrDefault);

                                using (var framework = new XunitFrontController(AppDomainDefaultBehavior, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: shadowCopy, diagnosticMessageSink: MessageSinkAdapter.Wrap(diagnosticSink)))
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
                                            reporterMessageHandler.OnMessage(new TestAssemblyDiscoveryStarting(assembly, framework.CanUseAppDomains && AppDomainDefaultBehavior != AppDomainSupport.Denied, shadowCopy, discoveryOptions));

                                            framework.Find(includeSourceInformation: true, discoveryMessageSink: visitor, discoveryOptions: discoveryOptions);
                                            var totalTests = visitor.Finish();

                                            visitComplete?.Invoke(assemblyFileName, framework, discoveryOptions, visitor);

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
#if !WINDOWS_UAP
                            var fileLoad = ex as FileLoadException;
#endif
                            if (ex is InvalidOperationException)
                                logger.LogWarning("Skipping: {0} ({1})", fileName, ex.Message);
                            else if (fileNotFound != null)
                                logger.LogWarning("Skipping: {0} (could not find dependent assembly '{1}')", fileName, Path.GetFileNameWithoutExtension(fileNotFound.FileName));
#if !WINDOWS_UAP
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
#if WINDOWS_UAP
            sources = sources.Select(s => Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(s)));
#endif

            var assemblyDiscoveredInfos = new List<AssemblyDiscoveredInfo>();

            DiscoverTests(
                runContext?.RunSettings,
                sources,
                logger,
                (source, discoverer, discoveryOptions) => new VsExecutionDiscoverySink(() => cancelled),
                (source, discoverer, discoveryOptions, visitor) =>
                {
                    if (discoveryOptions.GetDiagnosticMessagesOrDefault())
                        foreach (var testCase in visitor.TestCases)
                            logger.Log(testCase, "Discovered [execution] test case '{0}' (ID = '{1}')", testCase.DisplayName, testCase.UniqueID);

                    assemblyDiscoveredInfos.Add(new AssemblyDiscoveredInfo
                    {
                        AssemblyFileName = source,
                        DiscoveredTestCases = visitor.TestCases.Select(testCase => new DiscoveredTestCase(source, discoverer, testCase, logger)).ToList()
                    });
                }
            );

            var traitNames = new HashSet<string>(assemblyDiscoveredInfos.SelectMany(assemblyInfo => assemblyInfo.DiscoveredTestCases.SelectMany(testCase => testCase.TraitNames)));

            return assemblyDiscoveredInfos.Select(assemblyInfo =>
            {
                // Apply any filtering
                var filter = new TestCaseFilter(runContext, logger, assemblyInfo.AssemblyFileName, traitNames);
                var filteredTestCases = assemblyInfo.DiscoveredTestCases.Where(dtc => filter.MatchTestCase(dtc.VSTestCase)).ToList();

                // Force unique names if there is more than 1 testcase with the same name
                foreach (var groupWithDuplicateNames in filteredTestCases.GroupBy(dtc => dtc.Name).Where(group => group.Count() > 1))
                    foreach (var discoveredTestCaseWithDuplicateName in groupWithDuplicateNames)
                        discoveredTestCaseWithDuplicateName.ForceUniqueName();

                return new AssemblyRunInfo
                {
                    AssemblyFileName = assemblyInfo.AssemblyFileName,
                    Configuration = LoadConfiguration(assemblyInfo.AssemblyFileName),
                    TestCases = filteredTestCases.Select(dtc => dtc.VSTestCase).ToList(),
                };
            }).ToList();
        }

        static bool IsXunitTestAssembly(string assemblyFileName)
        {
            // Don't try to load ourselves (or any test framework assemblies), since we fail (issue #47 in xunit/xunit).
            if (platformAssemblies.Contains(Path.GetFileName(assemblyFileName)))
                return false;

#if NETCOREAPP1_0
            return IsXunitPackageReferenced(assemblyFileName);
#else
            var assemblyFolder = Path.GetDirectoryName(assemblyFileName);
            return File.Exists(Path.Combine(assemblyFolder, "xunit.dll"))
                || Directory.GetFiles(assemblyFolder, "xunit.execution.*.dll").Length > 0;
#endif
        }

#if NETCOREAPP1_0
        static bool IsXunitPackageReferenced(string assemblyFileName)
        {
            var depsFile = assemblyFileName.Replace(".dll", ".deps.json");
            if (!File.Exists(depsFile))
                return false;

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(depsFile))))
                {
                    var context = new DependencyContextJsonReader().Read(stream);
                    var xunitLibrary = context.RuntimeLibraries.Where(lib => lib.Name.Equals("xunit")).FirstOrDefault();
                    return xunitLibrary != null;
                }
            }
            catch
            {
                return false;
            }
        }
#endif

        static TestAssemblyConfiguration LoadConfiguration(string assemblyName)
        {
#if WINDOWS_UAP
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

#if NET35 || NETCOREAPP1_0
                // Reads settings like disabling appdomains, parallel etc.
                // Do this first before invoking any thing else to ensure correct settings for the run
                RunSettingsHelper.ReadRunSettings(runContext?.RunSettings?.SettingsXml);
#endif

                cancelled = false;

                var assemblies = testCaseAccessor();
                var parallelizeAssemblies = !RunSettingsHelper.DisableParallelization && assemblies.All(runInfo => runInfo.Configuration.ParallelizeAssemblyOrDefault);


                var reporterMessageHandler = MessageSinkWithTypesAdapter.Wrap(GetRunnerReporter(assemblies.Select(ari => ari.AssemblyFileName))
                                                                        .CreateMessageHandler(new VisualStudioRunnerLogger(logger)));


                using (AssemblyHelper.SubscribeResolve())
                    if (parallelizeAssemblies)
                        assemblies
                            .Select(runInfo => RunTestsInAssemblyAsync(runContext, frameworkHandle, logger, reporterMessageHandler, runInfo))
                            .ToList()
                            .ForEach(@event => @event.WaitOne());
                    else
                        assemblies
                            .ForEach(runInfo => RunTestsInAssembly(runContext, frameworkHandle, logger, reporterMessageHandler, runInfo));
            }
            catch (Exception ex)
            {
                logger.LogError("Catastrophic failure: {0}", ex);
            }
        }

        void RunTestsInAssembly(IRunContext runContext,
                                IFrameworkHandle frameworkHandle,
                                LoggerHelper logger,
                                IMessageSinkWithTypes reporterMessageHandler,
                                AssemblyRunInfo runInfo)
        {
            if (cancelled)
                return;

            var assembly = new XunitProjectAssembly { AssemblyFilename = runInfo.AssemblyFileName };
            var assemblyFileName = runInfo.AssemblyFileName;
            var assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
            var shadowCopy = assembly.Configuration.ShadowCopyOrDefault;

            var appDomain = assembly.Configuration.AppDomain ?? AppDomainDefaultBehavior;
            var longRunningSeconds = assembly.Configuration.LongRunningTestSecondsOrDefault;

            if (RunSettingsHelper.DisableAppDomain)
                appDomain = AppDomainSupport.Denied;

            try
            {
#if WINDOWS_UAP
                // For AppX Apps, use the package location
                assemblyFileName = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(assemblyFileName));
#endif

                var diagnosticSink = new DiagnosticMessageSink(logger, assemblyDisplayName, runInfo.Configuration.DiagnosticMessagesOrDefault);
                using (var controller = new XunitFrontController(appDomain, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: shadowCopy, diagnosticMessageSink: MessageSinkAdapter.Wrap(diagnosticSink)))
                {
                    var xunitTestCases = runInfo.TestCases.Select(tc => new { vs = tc, xunit = Deserialize(logger, controller, tc) })
                                                          .Where(tc => tc.xunit != null)
                                                          .ToDictionary(tc => tc.xunit, tc => tc.vs);

                    var executionOptions = TestFrameworkOptions.ForExecution(runInfo.Configuration);
                    if (RunSettingsHelper.DisableParallelization)
                    {
                        executionOptions.SetSynchronousMessageReporting(true);
                        executionOptions.SetDisableParallelization(true);
                    }

                    reporterMessageHandler.OnMessage(new TestAssemblyExecutionStarting(assembly, executionOptions));



                    using (var vsExecutionSink = new VsExecutionSink(reporterMessageHandler, frameworkHandle, logger, xunitTestCases, executionOptions, () => cancelled))
                    {

                        IExecutionSink resultsSink = vsExecutionSink;
                        if (longRunningSeconds > 0)
                            resultsSink = new DelegatingLongRunningTestDetectionSink(resultsSink, TimeSpan.FromSeconds(longRunningSeconds), diagnosticSink);

                        controller.RunTests(xunitTestCases.Keys.ToList(), resultsSink, executionOptions);
                        resultsSink.Finished.WaitOne();

                        reporterMessageHandler.OnMessage(new TestAssemblyExecutionFinished(assembly, executionOptions, resultsSink.ExecutionSummary));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError("{0}: Catastrophic failure: {1}", assemblyDisplayName, ex);
            }
        }

        ManualResetEvent RunTestsInAssemblyAsync(IRunContext runContext,
                                                 IFrameworkHandle frameworkHandle,
                                                 LoggerHelper logger,
                                                 IMessageSinkWithTypes reporterMessageHandler,
                                                 AssemblyRunInfo runInfo)
        {
            var @event = new ManualResetEvent(initialState: false);
            Action handler = () =>
            {
                try
                {
                    RunTestsInAssembly(runContext, frameworkHandle, logger, reporterMessageHandler, runInfo);
                }
                finally
                {
                    @event.Set();
                }
            };

#if WINDOWS_UAP
            var fireAndForget = Windows.System.Threading.ThreadPool.RunAsync(_ => handler());
#else
            ThreadPool.QueueUserWorkItem(_ => handler());
#endif

            return @event;
        }

        static IRunnerReporter GetRunnerReporter(IEnumerable<string> assemblyFileNames)
        {
            var reporter = default(IRunnerReporter);
            try
            {
                reporter = GetAvailableRunnerReporters(assemblyFileNames).FirstOrDefault(r => r.IsEnvironmentallyEnabled);
            }
            catch { }

            return reporter ?? new DefaultRunnerReporterWithTypes();
        }

        static IEnumerable<IRunnerReporter> GetAvailableRunnerReporters(IEnumerable<string> sources)
        {
#if WINDOWS_UAP
            // No reporters on UWP
            return Enumerable.Empty<IRunnerReporter>();
#elif NETCOREAPP1_0
            // Combine all input libs and merge their contexts to find the potential reporters
            var result = new List<IRunnerReporter>();
            var dcjr = new DependencyContextJsonReader();
            var deps = sources.Select(Path.GetFullPath)
                              .Select(s => s.Replace(".dll", ".deps.json"))
                              .Where(File.Exists)
                              .Select(f => new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(f))))
                              .Select(dcjr.Read);
            var ctx = deps.Aggregate(DependencyContext.Default, (context, dependencyContext) => context.Merge(dependencyContext));
            dcjr.Dispose();

            var depsAssms = ctx.GetRuntimeAssemblyNames(RuntimeEnvironment.GetRuntimeIdentifier())
                               .ToList();

            // Make sure to also check assemblies within the directory of the sources
            var dllsInSources = sources.Select(Path.GetFullPath)
                                       .Select(Path.GetDirectoryName)
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .SelectMany(p => Directory.GetFiles(p, "*.dll").Select(f => Path.Combine(p, f)))
                                       .Select(f => new AssemblyName { Name = Path.GetFileNameWithoutExtension(f) })
                                       .ToList();

            foreach (var assemblyName in depsAssms.Concat(dllsInSources))
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    foreach (var type in assembly.DefinedTypes)
                    {
#pragma warning disable CS0618
                        if (type == null || type.IsAbstract || type == typeof(DefaultRunnerReporter).GetTypeInfo() || type == typeof(DefaultRunnerReporterWithTypes).GetTypeInfo() || type.ImplementedInterfaces.All(i => i != typeof(IRunnerReporter)))
                            continue;
#pragma warning restore CS0618

                        var ctor = type.DeclaredConstructors.FirstOrDefault(c => c.GetParameters().Length == 0);
                        if (ctor == null)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Type {type.FullName} in assembly {assembly} appears to be a runner reporter, but does not have an empty constructor.");
                            Console.ResetColor();
                            continue;
                        }

                        result.Add((IRunnerReporter)ctor.Invoke(new object[0]));
                    }
                }
                catch
                {
                    continue;
                }
            }

            return result;
#elif NET35
            var result = new List<IRunnerReporter>();
            var runnerPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetLocalCodeBase());

            foreach (var dllFile in Directory.GetFiles(runnerPath, "*.dll").Select(f => Path.Combine(runnerPath, f)))
            {
                Type[] types;

                try
                {
                    var assembly = Assembly.LoadFile(dllFile);
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
#pragma warning disable CS0618
                    if (type == null || type.IsAbstract || type == typeof(DefaultRunnerReporter) || type == typeof(DefaultRunnerReporterWithTypes) || !type.GetInterfaces().Any(t => t == typeof(IRunnerReporter)))
                        continue;
#pragma warning restore CS0618

                    var ctor = type.GetConstructor(new Type[0]);
                    if (ctor == null)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"Type {type.FullName} in assembly {dllFile} appears to be a runner reporter, but does not have an empty constructor.");
                        Console.ResetColor();
                        continue;
                    }

                    result.Add((IRunnerReporter)ctor.Invoke(new object[0]));
                }
            }

            return result;
#endif
        }

        class AssemblyDiscoveredInfo
        {
            public string AssemblyFileName;
            public IList<DiscoveredTestCase> DiscoveredTestCases;
        }

        class DiscoveredTestCase
        {
            public string Name { get; }

            public IEnumerable<string> TraitNames { get; }

            public TestCase VSTestCase { get; }

            string uniqueID;

            public DiscoveredTestCase(string source, ITestFrameworkDiscoverer discoverer, ITestCase testCase, LoggerHelper logger)
            {
                Name = $"{testCase.TestMethod.TestClass.Class.Name}.{testCase.TestMethod.Method.Name}";
                TraitNames = testCase.Traits.Keys;
                VSTestCase = VsDiscoverySink.CreateVsTestCase(source, discoverer, testCase, forceUniqueName: false, logger: logger);
                uniqueID = testCase.UniqueID;
            }

            public void ForceUniqueName()
            {
                VsDiscoverySink.ForceUniqueName(VSTestCase, uniqueID);
            }
        }
    }
}