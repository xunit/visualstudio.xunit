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
using System.Reflection;
using Xunit.Sdk;
using Newtonsoft.Json;

#if !PLATFORM_DOTNET
using System.Xml;
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
        public static TestProperty SerializedTestCaseArgumentProperty = GetTestArgumentProperty();
        public static TestProperty TheoryAgrumentProperty = GetTheoryAgrumentProperty();

#if PLATFORM_DOTNET
        internal static AppDomainSupport AppDomain = AppDomainSupport.Denied;
#else
        internal static AppDomainSupport AppDomain = AppDomainSupport.Required;
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

            Dictionary<ITestCase, TestCase> testCaseMap = null;
            if (AppDomain == AppDomainSupport.Denied)
            {
                testCaseMap = GetXunitTestCaseMap(tests);
            }

            RunTests(
                runContext, frameworkHandle, logger,
                () => tests.GroupBy(testCase => testCase.Source)
                           .Select(group => new AssemblyRunInfo { AssemblyFileName = group.Key, Configuration = LoadConfiguration(group.Key), TestCases = group.ToList(), TestCaseMap = testCaseMap })
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
            where TVisitor : IVsDiscoverySink, IDisposable
        {
            try
            {
                RemotingUtility.CleanUpRegisteredChannels();

                using (AssemblyHelper.SubscribeResolve())
                {
                    var reporterMessageHandler = new DefaultRunnerReporterWithTypes().CreateMessageHandler(new VisualStudioRunnerLogger(logger));

                    foreach (var assemblyFileNameCanBeWithoutAbsolutePath in sources)
                    {
                        var assemblyFileName = assemblyFileNameCanBeWithoutAbsolutePath;
#if !PLATFORM_DOTNET
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
                                var diagnosticMessageVisitor = new DiagnosticMessageSink(logger, fileName, configuration.DiagnosticMessagesOrDefault);

                                using (var framework = new XunitFrontController(AppDomainDefaultBehavior, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: shadowCopy, diagnosticMessageSink: diagnosticMessageVisitor))
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

                                            framework.Find(includeSourceInformation: true, messageSink: visitor, discoveryOptions: discoveryOptions);
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

        static TestProperty GetTestArgumentProperty()
          => TestProperty.Register("XunitTestCaseArgument", "xUnit.net Test Case Argument", typeof(string), typeof(object[]));

        static TestProperty GetTheoryAgrumentProperty()
            => TestProperty.Register("TheoryAgrument", "xUnit.net Theory Argument", typeof(string), typeof(object[]));

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
                (source, discoverer, discoveryOptions) => new VsExecutionDiscoverySink(() => cancelled),
                (source, discoverer, discoveryOptions, visitor) =>
                {
                    var vsFilteredTestCases = visitor.TestCases.Select(testCase => VsDiscoverySink.CreateVsTestCase(source, discoverer, testCase, false, logger: logger, knownTraitNames: knownTraitNames)).ToList();

                    // Apply any filtering
                    var filterHelper = new TestCaseFilterHelper(knownTraitNames);
                    vsFilteredTestCases = filterHelper.GetFilteredTestList(vsFilteredTestCases, runContext, logger, source).ToList();

                    // Re-create testcases with unique names if there is more than 1
                    Dictionary<ITestCase, TestCase> testCaseMap = null;
                    List<TestCase> testCases = null;

                    if (AppDomain == AppDomainSupport.Denied)
                    {
                        testCaseMap = visitor.TestCases.Where(tc => vsFilteredTestCases.Any(vsTc => vsTc.DisplayName == tc.DisplayName)).GroupBy(tc => $"{tc.TestMethod.TestClass.Class.Name}.{tc.TestMethod.Method.Name}")
                                                           .SelectMany(group => group.Select((testCase, i) => new
                                                           {
                                                               testCase,
                                                               v = VsDiscoverySink.CreateVsTestCase(
                                                                                                           source,
                                                                                                           discoverer,
                                                                                                           testCase,
                                                                                                           forceUniqueNames: group.Count() > 1,
                                                                                                           logger: logger,
                                                                                                           knownTraitNames: knownTraitNames)
                                                           })).ToDictionary(x => x.testCase, x => x.v);

                    }
                    else
                    {
                        testCases = visitor.TestCases.Where(tc => vsFilteredTestCases.Any(vsTc => vsTc.DisplayName == tc.DisplayName)).GroupBy(tc => $"{tc.TestMethod.TestClass.Class.Name}.{tc.TestMethod.Method.Name}")
                                                          .SelectMany(group => group.Select(testCase =>
                                                                                                      VsDiscoverySink.CreateVsTestCase(
                                                                                                          source,
                                                                                                          discoverer,
                                                                                                          testCase,
                                                                                                          forceUniqueNames: group.Count() > 1,
                                                                                                          logger: logger,
                                                                                                          knownTraitNames: knownTraitNames))
                                                          .Where(vsTestCase => vsTestCase != null)).ToList(); // pre-enumerate these as it populates the known trait names collection
                    }
                    //if (AppDomain == AppDomainSupport.Denied)
                    //{
                    //    testCaseMap =
                    //       testCases.Select(
                    //           (k, i) => new { k, v = visitor.TestCases.Where(e => VsDiscoverySink.GuidFromString(string.Concat(VsDiscoverySink.uri,e.UniqueID)).Equals(k.Id)).First() })
                    //           .ToDictionary(x => x.v, x => x.k);
                    //}

                    var runInfo = new AssemblyRunInfo
                    {
                        AssemblyFileName = source,
                        Configuration = LoadConfiguration(source),
                        TestCases = testCases,
                        TestCaseMap = testCaseMap
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
                var reporterMessageHandler = new DefaultRunnerReporterWithTypes().CreateMessageHandler(new VisualStudioRunnerLogger(logger));

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
                                IMessageSink reporterMessageHandler,
                                AssemblyRunInfo runInfo)
        {
            if (cancelled)
                return;

            var assembly = new XunitProjectAssembly { AssemblyFilename = runInfo.AssemblyFileName };
            var assemblyFileName = runInfo.AssemblyFileName;
            var assemblyDisplayName = Path.GetFileNameWithoutExtension(assemblyFileName);
            var shadowCopy = assembly.Configuration.ShadowCopyOrDefault;
            var appDomain = assembly.Configuration.AppDomain ?? AppDomainDefaultBehavior;
            if (DisableAppDomainRequestedInRunContext(runContext.RunSettings.SettingsXml))
                appDomain = AppDomainSupport.Denied;

            try
            {
#if PLATFORM_DOTNET
                // For AppX Apps, use the package location
                assemblyFileName = Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, Path.GetFileName(assemblyFileName));
#endif

                var diagnosticMessageVisitor = new DiagnosticMessageSink(logger, assemblyDisplayName, runInfo.Configuration.DiagnosticMessagesOrDefault);
                using (var controller = new XunitFrontController(appDomain, assemblyFileName: assemblyFileName, configFileName: null, shadowCopy: shadowCopy, diagnosticMessageSink: diagnosticMessageVisitor))
                {
                    Dictionary<ITestCase, TestCase> xunitTestCases;
                    if (AppDomain == AppDomainSupport.Denied)
                    {
                        xunitTestCases = runInfo.TestCaseMap;
                    }
                    else
                    {
                        xunitTestCases = runInfo.TestCases.Select(tc => new { vs = tc, xunit = Deserialize(logger, controller, tc) })
                                                              .Where(tc => tc.xunit != null)
                                                              .ToDictionary(tc => tc.xunit, tc => tc.vs);
                    }


                    var executionOptions = TestFrameworkOptions.ForExecution(runInfo.Configuration);
                    executionOptions.SetSynchronousMessageReporting(true);

                    reporterMessageHandler.OnMessage(new TestAssemblyExecutionStarting(assembly, executionOptions));

                    using (var executionSink = new VsExecutionSink(frameworkHandle, logger, xunitTestCases, executionOptions, () => cancelled))
                    {
                        controller.RunTests(xunitTestCases.Keys.ToList(), executionSink, executionOptions);
                        executionSink.Finished.WaitOne();

                        reporterMessageHandler.OnMessage(new TestAssemblyExecutionFinished(assembly, executionOptions, executionSink.ExecutionSummary));
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
                                                 IMessageSink reporterMessageHandler,
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

        Dictionary<ITestCase, TestCase> GetXunitTestCaseMap(IEnumerable<TestCase> tests)
        {
            // -- Get the xunit ITestCase from TestCase
            Dictionary<ITestCase, TestCase> testcaselist = new Dictionary<ITestCase, TestCase>();

            var dict = new Dictionary<string, Assembly>();
            foreach (TestCase test in tests)
            {
                var asmName = test.Source.Substring(test.Source.LastIndexOf('\\') + 1, test.Source.LastIndexOf('.') - test.Source.LastIndexOf('\\') - 1);
                Assembly asm = null;
                if (!dict.ContainsKey(asmName))
                {
#if PLATFORM_DOTNET
                    // todo : revisit this
                    asm = Assembly.Load(new AssemblyName(asmName));
#else
                    asm = Assembly.LoadFrom(test.Source);
#endif
                    dict.Add(asmName, asm);
                }
                else
                {
                    asm = dict[asmName];
                }

                IAssemblyInfo assemblyInfo = new ReflectionAssemblyInfo(asm);
                ITestAssembly testAssembly = new TestAssembly(assemblyInfo);

                var lastIndexOfDot = test.FullyQualifiedName.LastIndexOf('.');
                var testname = test.FullyQualifiedName.Split(' ')[0].Substring(lastIndexOfDot + 1);
                var classname = test.FullyQualifiedName.Substring(0, lastIndexOfDot);

                var methodClass = asm.GetType(classname);

                ITypeInfo typeInfo = new ReflectionTypeInfo(methodClass);
                TestCollection testCollection = new TestCollection(testAssembly, typeInfo, "Test collection for " + classname);
                TestClass tc = new TestClass(testCollection, typeInfo);

                //IMethodInfo
#if PLATFORM_DOTNET
                var methodinfos = methodClass.GetRuntimeMethods().ToList();
#else
                var methodinfos = methodClass.GetMethods().ToList();
#endif

                XunitTestCase xunitTestCase = null;
                var serializedTestArgs = test.GetPropertyValue<string>(SerializedTestCaseArgumentProperty, null);


                Object[] testarguments = serializedTestArgs == null ? null : JsonConvert.DeserializeObject<Object[]>(serializedTestArgs, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto
                });

                if (testarguments != null)
                {
                    testname = testname.Split('(')[0];
                }


                MethodInfo methodinfo = null;
                for (int i = 0; i < methodinfos.Count(); i++)
                {
                    if (methodinfos[i].Name.Equals(testname))
                    {
                        methodinfo = methodinfos[i];
                        break;
                    }
                }

                ReflectionMethodInfo refMethodInfo = new ReflectionMethodInfo(methodinfo);
                ITestMethod method = new TestMethod(tc, refMethodInfo);
                NullMessageSink sink = new NullMessageSink();

                if (testarguments == null && !string.IsNullOrEmpty(test.GetPropertyValue<string>(TheoryAgrumentProperty, null)))
                {
#if !PLATFORM_DOTNET
                    Type type = typeof(XunitTheoryTestCase);
                    ConstructorInfo ctor = (ConstructorInfo)type.GetConstructors().GetValue(1);
                    xunitTestCase = (XunitTheoryTestCase)ctor.Invoke(new object[] { sink, 1, method });
#else
                    xunitTestCase = new XunitTheoryTestCase(sink, Xunit.Sdk.TestMethodDisplay.ClassAndMethod, method);

#endif
                }
                else
                {
#if !PLATFORM_DOTNET
                    Type type = typeof(XunitTestCase);
                    ConstructorInfo ctor = (ConstructorInfo)type.GetConstructors().GetValue(1);
                    xunitTestCase = (XunitTestCase)ctor.Invoke(new object[] { sink, 1, method, testarguments });
#else
                    xunitTestCase = new XunitTestCase(sink, Xunit.Sdk.TestMethodDisplay.ClassAndMethod, method, testarguments);
#endif
                }

                xunitTestCase.SourceInformation = new SourceInformation();
                xunitTestCase.SourceInformation.FileName = test.CodeFilePath;
                xunitTestCase.SourceInformation.LineNumber = test.LineNumber;

                testcaselist.Add(xunitTestCase, test);
            }
            return testcaselist;
        }

        bool DisableAppDomainRequestedInRunContext(string settingsXml)
        {
#if !PLATFORM_DOTNET
            if (string.IsNullOrEmpty(settingsXml))
                return false;

            var disableAppDomain = false;
            var stringReader = new StringReader(settingsXml);
            var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true };
            var reader = XmlReader.Create(stringReader, settings);

            if (reader.ReadToFollowing("DisableAppDomain"))
                bool.TryParse(reader.ReadInnerXml(), out disableAppDomain);

            return disableAppDomain;
#else
            return false;
#endif
        }
    }
}
