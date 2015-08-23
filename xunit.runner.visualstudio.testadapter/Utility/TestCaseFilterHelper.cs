using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class TestCaseFilterHelper
    {
        const string DisplayNameString = "DisplayName";
        const string FullyQualifiedNameString = "FullyQualifiedName";

        readonly HashSet<string> knownTraits;
        List<string> supportedPropertyNames;

        public TestCaseFilterHelper(HashSet<string> knownTraits)
        {
            this.knownTraits = knownTraits;

            supportedPropertyNames = GetSupportedPropertyNames();
        }

        public IEnumerable<TestCase> GetFilteredTestList(IEnumerable<TestCase> testCases, IRunContext runContext, LoggerHelper logger, string assemblyFileName)
        {
            ITestCaseFilterExpression filter = null;

            if (GetTestCaseFilterExpression(runContext, logger, assemblyFileName, out filter))
            {
                if (filter != null)
                    return testCases.Where(testCase => filter.MatchTestCase(testCase, (p) => PropertyProvider(testCase, p)));
            }
            else
            {
                // Error while filtering, ensure discovered test list is empty
                return Enumerable.Empty<TestCase>();
            }

            // No filter is specified return the original list
            return testCases;
        }

        public object PropertyProvider(TestCase testCase, string name)
        {
            // Traits filtering
            if (knownTraits.Contains(name))
            {
                var result = new List<string>();

                foreach (var trait in GetTraits(testCase))
                    if (string.Equals(trait.Key, name, StringComparison.OrdinalIgnoreCase))
                        result.Add(trait.Value);

                if (result.Count > 0)
                    return result.ToArray();
            }
            else
            {
                // Handle the displayName and fullyQualifierNames independently
                if (string.Equals(name, FullyQualifiedNameString, StringComparison.OrdinalIgnoreCase))
                    return testCase.FullyQualifiedName;
                if (string.Equals(name, DisplayNameString, StringComparison.OrdinalIgnoreCase))
                    return testCase.DisplayName;
            }

            return null;
        }

        bool GetTestCaseFilterExpression(IRunContext runContext, LoggerHelper logger, string assemblyFileName, out ITestCaseFilterExpression filter)
        {
            filter = null;

            try
            {
                // In Microsoft.VisualStudio.TestPlatform.ObjectModel V11 IRunContext provides a TestCaseFilter property
                // GetTestCaseFilter only exists in V12+
#if PLATFORM_DOTNET
                var getTestCaseFilterMethod = runContext.GetType().GetRuntimeMethod("GetTestCaseFilter", new[] { typeof(IEnumerable<string>), typeof(Func<string, TestProperty>) });
#else
                var getTestCaseFilterMethod = runContext.GetType().GetMethod("GetTestCaseFilter");
#endif
                if (getTestCaseFilterMethod != null)
                    filter = (ITestCaseFilterExpression)getTestCaseFilterMethod.Invoke(runContext, new object[] { supportedPropertyNames, null });

                return true;
            }
            catch (TargetInvocationException e)
            {
                var innerExceptionType = e.InnerException.GetType();
                if (innerExceptionType.FullName.EndsWith("TestPlatformFormatException", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogError("{0}: Exception discovering tests: {1}", Path.GetFileNameWithoutExtension(assemblyFileName), e.InnerException.Message);
                    return false;
                }

                throw;
            }
        }

        List<string> GetSupportedPropertyNames()
        {
            // Returns the set of well-known property names usually used with the Test Plugins (Used Test Traits + DisplayName + FullyQualifiedName)
            if (supportedPropertyNames == null)
            {
                supportedPropertyNames = knownTraits.ToList();
                supportedPropertyNames.Add(DisplayNameString);
                supportedPropertyNames.Add(FullyQualifiedNameString);
            }

            return supportedPropertyNames;
        }

        static IEnumerable<KeyValuePair<string, string>> GetTraits(TestCase testCase)
        {
            var traitProperty = TestProperty.Find("TestObject.Traits");
            if (traitProperty != null)
                return testCase.GetPropertyValue(traitProperty, Enumerable.Empty<KeyValuePair<string, string>>().ToArray());

            return Enumerable.Empty<KeyValuePair<string, string>>();
        }
    }
}

