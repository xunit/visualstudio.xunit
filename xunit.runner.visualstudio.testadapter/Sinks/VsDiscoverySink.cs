﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Xunit.Abstractions;

#if PLATFORM_DOTNET
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;

#elif DOTNET_CORE
using System.Reflection;
using System.Security.Cryptography;

#else
using System.Security.Cryptography;
#endif

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class VsDiscoverySink : TestMessageSink, IVsDiscoverySink, IDisposable
    {
        const string Ellipsis = "...";
        const int MaximumDisplayNameLength = 447;

        static readonly Action<TestCase, string, string> addTraitThunk = GetAddTraitThunk();
        static readonly Uri uri = new Uri(Constants.ExecutorUri);

        readonly Func<bool> cancelThunk;
        readonly ITestFrameworkDiscoverer discoverer;
        readonly ITestFrameworkDiscoveryOptions discoveryOptions;
        readonly ITestCaseDiscoverySink discoverySink;
        readonly List<ITestCase> lastTestClassTestCases = new List<ITestCase>();
        readonly LoggerHelper logger;
        readonly string source;

        string lastTestClass;

        public VsDiscoverySink(string source,
                               ITestFrameworkDiscoverer discoverer,
                               LoggerHelper logger,
                               ITestCaseDiscoverySink discoverySink,
                               ITestFrameworkDiscoveryOptions discoveryOptions,
                               Func<bool> cancelThunk)
        {
            this.source = source;
            this.discoverer = discoverer;
            this.logger = logger;
            this.discoverySink = discoverySink;
            this.discoveryOptions = discoveryOptions;
            this.cancelThunk = cancelThunk;

            TestCaseDiscoveryMessageEvent += HandleTestCaseDiscoveryMessage;
            DiscoveryCompleteMessageEvent += HandleDiscoveryCompleteMessage;
        }

        public ManualResetEvent Finished { get; } = new ManualResetEvent(initialState: false);

        public int TotalTests { get; private set; }

        public void Dispose()
            => ((IDisposable)Finished).Dispose();

        public static TestCase CreateVsTestCase(string source, ITestFrameworkDiscoverer discoverer, ITestCase xunitTestCase, bool forceUniqueNames, LoggerHelper logger, HashSet<string> knownTraitNames = null)
        {
            try
            {
                var serializedTestCase = discoverer.Serialize(xunitTestCase);
                var fqTestMethodName = $"{xunitTestCase.TestMethod.TestClass.Class.Name}.{xunitTestCase.TestMethod.Method.Name}";
                var uniqueName = forceUniqueNames ? $"{fqTestMethodName} ({xunitTestCase.UniqueID})" : fqTestMethodName;

                var result = new TestCase(uniqueName, uri, source) { DisplayName = Escape(xunitTestCase.DisplayName) };
                result.SetPropertyValue(VsTestRunner.SerializedTestCaseProperty, serializedTestCase);
                result.Id = GuidFromString(uri + xunitTestCase.UniqueID);

                if (addTraitThunk != null)
                {
                    foreach (var key in xunitTestCase.Traits.Keys)
                    {
                        if (knownTraitNames != null)
                            knownTraitNames.Add(key);

                        foreach (var value in xunitTestCase.Traits[key])
                            addTraitThunk(result, key, value);
                    }
                }

                result.CodeFilePath = xunitTestCase.SourceInformation.FileName;
                result.LineNumber = xunitTestCase.SourceInformation.LineNumber.GetValueOrDefault();

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(xunitTestCase, "Error creating Visual Studio test case for {0}: {1}", xunitTestCase.DisplayName, ex);
                return null;
            }
        }

        static string Escape(string value)
        {
            if (value == null)
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

        static Action<TestCase, string, string> GetAddTraitThunk()
        {
            try
            {
                var testCaseType = typeof(TestCase);
                var stringType = typeof(string);
#if PLATFORM_DOTNET || DOTNET_CORE
                var property = testCaseType.GetRuntimeProperty("Traits");
#else
                var property = testCaseType.GetProperty("Traits");
#endif

                if (property == null)
                    return null;

#if PLATFORM_DOTNET || DOTNET_CORE
                var method = property.PropertyType.GetRuntimeMethod("Add", new[] { typeof(string), typeof(string) });
#else
                var method = property.PropertyType.GetMethod("Add", new[] { typeof(string), typeof(string) });
#endif
                if (method == null)
                    return null;

                var thisParam = Expression.Parameter(testCaseType, "this");
                var nameParam = Expression.Parameter(stringType, "name");
                var valueParam = Expression.Parameter(stringType, "value");
                var instance = Expression.Property(thisParam, property);
                var body = Expression.Call(instance, method, new[] { nameParam, valueParam });

                return Expression.Lambda<Action<TestCase, string, string>>(body, thisParam, nameParam, valueParam).Compile();
            }
            catch (Exception)
            {
                return null;
            }
        }

        void HandleCancellation(MessageHandlerArgs args)
        {
            if (cancelThunk())
                args.Stop();
        }

        void HandleTestCaseDiscoveryMessage(MessageHandlerArgs<ITestCaseDiscoveryMessage> args)
        {
            var testCase = args.Message.TestCase;
            var testClass = $"{testCase.TestMethod.TestClass.Class.Name}.{testCase.TestMethod.Method.Name}";
            if (lastTestClass != testClass)
                SendExistingTestCases();

            lastTestClass = testClass;
            lastTestClassTestCases.Add(testCase);
            TotalTests++;

            HandleCancellation(args);
        }

        void HandleDiscoveryCompleteMessage(MessageHandlerArgs<IDiscoveryCompleteMessage> args)
        {
            SendExistingTestCases();

            Finished.Set();

            HandleCancellation(args);
        }

        private void SendExistingTestCases()
        {
            var forceUniqueNames = lastTestClassTestCases.Count > 1;

            foreach (var testCase in lastTestClassTestCases)
            {
                var vsTestCase = CreateVsTestCase(source, discoverer, testCase, forceUniqueNames, logger);
                if (vsTestCase != null)
                {
                    if (discoveryOptions.GetDiagnosticMessagesOrDefault())
                        logger.Log(testCase, "Discovered test case '{0}' (ID = '{1}', VS FQN = '{2}')", testCase.DisplayName, testCase.UniqueID, vsTestCase.FullyQualifiedName);

                    discoverySink.SendTestCase(vsTestCase);
                }
                else
                    logger.LogWarning(testCase, "Could not create VS test case for '{0}' (ID = '{1}', VS FQN = '{2}')", testCase.DisplayName, testCase.UniqueID, vsTestCase.FullyQualifiedName);
            }

            lastTestClassTestCases.Clear();
        }

        public static string fqTestMethodName { get; set; }

#if PLATFORM_DOTNET
        readonly static HashAlgorithmProvider Hasher = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);

        static Guid GuidFromString(string data)
        {
            var buffer = CryptographicBuffer.CreateFromByteArray(Encoding.Unicode.GetBytes(data));
            var hash = Hasher.HashData(buffer).ToArray();
            var b = new byte[16];
            Array.Copy((Array)hash, (Array)b, 16);
            return new Guid(b);
        }
#else

#if DOTNET_CORE
        readonly static HashAlgorithm Hasher = SHA1.Create();
#else
        readonly static HashAlgorithm Hasher = new SHA1Managed();
#endif

        static Guid GuidFromString(string data)
        {
            var hash = Hasher.ComputeHash(Encoding.Unicode.GetBytes(data));
            var b = new byte[16];
            Array.Copy((Array)hash, (Array)b, 16);
            return new Guid(b);
        }
#endif
    }
}
