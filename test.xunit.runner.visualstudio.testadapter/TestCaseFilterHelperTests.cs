using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    // We are built against V11, but support v12 features.
    // Using Match signatues to test our functionality on v11.
    public interface IV12RunContext : IRunContext
    {
        ITestCaseFilterExpression GetTestCaseFilter(IEnumerable<string> supportedProperties, Func<string, object> propertyProvider);
    }

    public class TestCaseFilterHelperTests
    {
        private HashSet<string> dummyKnownTraits = new HashSet<string>(new string[2] { "Platform", "Product" });

        private IEnumerable<TestCase> GetDummyTestCases()
        {
            List<TestCase> testCaseList = new List<TestCase>();

            for (int i = 0; i < 10; i++)
            {
                testCaseList.Add(new TestCase("Test" + i.ToString(), new Uri(Constants.ExecutorUri), "DummyTestSource"));
            }

            return testCaseList;
        }

        private LoggerHelper GetLoggerHelper()
        {
            return new LoggerHelper(Substitute.For<IMessageLogger>(), new Stopwatch());
        }

        [Fact]
        public void TestCaseFilter_SingleMatch()
        {
            TestCaseFilterHelper filterHelper = new TestCaseFilterHelper(dummyKnownTraits);
            var dummyTestCaseList = GetDummyTestCases();
            string dummyTestCaseDisplayNamefilterString = "Test4";
            var context = Substitute.For<IRunContext, IV12RunContext>();
            var filterExpression = Substitute.For<ITestCaseFilterExpression>();

            // The matching should return a single testcase
            filterExpression.MatchTestCase(Arg.Any<TestCase>(), Arg.Any<Func<string, object>>()).Returns(x => ((TestCase)x[0]).FullyQualifiedName.Equals(dummyTestCaseDisplayNamefilterString));
            ((IV12RunContext)context).GetTestCaseFilter(null, null).ReturnsForAnyArgs(filterExpression);

            var result = filterHelper.GetFilteredTestList(dummyTestCaseList, context, GetLoggerHelper(), new Stopwatch(), "dummyTestAssembly");

            Assert.Equal(1, result.Count());
            Assert.Equal("Test4", result.First().FullyQualifiedName);
        }

        [Fact]
        public void TestCaseFilter_NoFilterString()
        {
            TestCaseFilterHelper filterHelper = new TestCaseFilterHelper(dummyKnownTraits);
            IEnumerable<TestCase> dummyTestCaseList = GetDummyTestCases();
            var context = Substitute.For<IRunContext, IV12RunContext>();
            ((IV12RunContext)context).GetTestCaseFilter(null, null).ReturnsForAnyArgs((ITestCaseFilterExpression)null);
            var result = filterHelper.GetFilteredTestList(dummyTestCaseList, context, GetLoggerHelper(), new Stopwatch(), "dummyTestAssembly");
            // Make sure we run the whole set since there is not filtering string specified
            Assert.Equal(dummyTestCaseList.Count(), result.Count());
        }

        [Fact]

        public void TestCaseFilter_ErrorParsingFilterString()
        {

            TestCaseFilterHelper filterHelper = new TestCaseFilterHelper(dummyKnownTraits);
            var dummyTestCaseList = GetDummyTestCases();
            var context = Substitute.For<IRunContext, IV12RunContext>();
            ((IV12RunContext)context).GetTestCaseFilter(null, null).ReturnsForAnyArgs(x => { throw new TestPlatformFormatException(); });
            var result = filterHelper.GetFilteredTestList(dummyTestCaseList, context, GetLoggerHelper(), new Stopwatch(), "dummyTestAssembly");
            // Make sure we don't run anything due to the filtering string parse error
            Assert.Equal(0, result.Count());

        }
    }

    // Fake TestPlatformFormatException type to test against V11
    public class TestPlatformFormatException : Exception
    {
        
    }
}
