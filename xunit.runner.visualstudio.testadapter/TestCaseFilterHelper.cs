using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class TestCaseFilterHelper
    {
        private const string DisplayNameString = "DisplayName";
        private const string FullyQualifiedNameString = "FullyQualifiedName";
        private HashSet<string> knownTraits;
        private List<string> supportedPropertyNames;

        public TestCaseFilterHelper(HashSet<string> knownTraits)
        {
            this.knownTraits = knownTraits;
            this.supportedPropertyNames = this.GetSupportedPropertyNames();
        }

        public IEnumerable<TestCase> GetFilteredTestList(IEnumerable<TestCase> testCases, IRunContext runContext, LoggerHelper logger, Stopwatch stopwatch, string assemblyFileName)
        {
            ITestCaseFilterExpression filter = null;
            if (this.GetTestCaseFilterExpression(runContext, logger, stopwatch, assemblyFileName, out filter))
            {
                if (filter != null)
                {
                    return testCases.Where(testCase => filter.MatchTestCase(testCase, (p) => this.PropertyProvider(testCase, p)));
                }
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
            if (this.knownTraits.Contains(name))
            {
                List<string> result = new List<string>();
                foreach (var trait in TestCaseFilterHelper.GetTraits(testCase))
                {
                    if (String.Equals(trait.Key, name, StringComparison.OrdinalIgnoreCase))
                        result.Add(trait.Value);
                }

                if (result.Count > 0)
                {
                    return result.ToArray();
                }
            }
            else
            {
                // Handle the displayName and fullyQualifierNames independently
                if (String.Equals(name, FullyQualifiedNameString, StringComparison.OrdinalIgnoreCase))
                    return testCase.FullyQualifiedName;
                if (String.Equals(name, DisplayNameString, StringComparison.OrdinalIgnoreCase))
                    return testCase.DisplayName;
            }

            return null;
        }

        private bool GetTestCaseFilterExpression(IRunContext runContext, LoggerHelper logger, Stopwatch stopwatch, string assemblyFileName, out ITestCaseFilterExpression filter)
        {
            filter = null;
            try
            {
                // In Microsoft.VisualStudio.TestPlatform.ObjectModel V11 IRunContext provides a TestCaseFilter property
                // GetTestCaseFilter only exists in V12+
#if WINDOWS_APP || WINDOWS_PHONE_APP
                MethodInfo getTestCaseFilterMethod = runContext.GetType().GetRuntimeMethod("GetTestCaseFilter", new [] {typeof(IEnumerable<string>), typeof(Func<string, TestProperty>)});
#else
                MethodInfo getTestCaseFilterMethod = runContext.GetType().GetMethod("GetTestCaseFilter");
#endif
                if (getTestCaseFilterMethod != null)
                {
                    filter = (ITestCaseFilterExpression)getTestCaseFilterMethod.Invoke(runContext, new object[] { this.supportedPropertyNames, null });
                }
                return true;
            }
            catch (TargetInvocationException e)
            {
                Type innerExceptionType = e.InnerException.GetType();
                if (innerExceptionType.FullName.EndsWith("TestPlatformFormatException", StringComparison.OrdinalIgnoreCase))
                {
                        logger.LogError("[xUnit.net {0}] Exception discovering tests from {1}: {2}", stopwatch.Elapsed, Path.GetFileName(assemblyFileName), e.InnerException.Message);
                        return false;
                }

                throw;
            }
        }

        private List<string> GetSupportedPropertyNames()
        {
            // Returns the set of well-known property names usually used with the Test Plugins (Used Test Traits + DisplayName + FullyQualifiedName)
            if (this.supportedPropertyNames == null)
            {
                this.supportedPropertyNames = this.knownTraits.ToList();
                this.supportedPropertyNames.Add(DisplayNameString);
                this.supportedPropertyNames.Add(FullyQualifiedNameString);
            }
            return this.supportedPropertyNames;
        }

        private static IEnumerable<KeyValuePair<string, string>> GetTraits(TestCase testCase)
        {
            // private const string TraitPropertyId = ;
            TestProperty traitProperty = TestProperty.Find("TestObject.Traits");
            if (traitProperty != null)
            {
                return testCase.GetPropertyValue<KeyValuePair<string, string>[]>(
                    traitProperty,
                    Enumerable.Empty<KeyValuePair<string, string>>().ToArray());
            }

            return Enumerable.Empty<KeyValuePair<string, string>>();
        }
    }
}

