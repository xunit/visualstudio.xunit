extern alias VSTestAdapter;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit;
using Constants = VSTestAdapter.Xunit.Runner.VisualStudio.Constants;
using LoggerHelper = VSTestAdapter.Xunit.Runner.VisualStudio.LoggerHelper;
using TestCaseFilter = VSTestAdapter.Xunit.Runner.VisualStudio.TestCaseFilter;

public class TestCaseFilterTests
{
	readonly HashSet<string> dummyKnownTraits = new() { "Platform", "Product", "Priority" };

	static IReadOnlyList<TestCase> GetDummyTestCases()
	{
		var testCaseList = new List<TestCase>();

		for (var i = 0; i < 10; i++)
			testCaseList.Add(new TestCase("Test" + i, new Uri(Constants.ExecutorUri), "DummyTestSource"));

		return testCaseList;
	}

	static TestCase GetDummyTestCaseWithTraits()
	{
		return new TestCase("propertyProviderTestFullyName", new Uri(Constants.ExecutorUri), "DummyTestSource")
		{
			DisplayName = "propertyProviderTestDisplayName",
			Traits = { new Trait("Priority", "0"), new Trait("Category", "unit_0"), new Trait("Category", "unit_1") }
		};
	}

	static LoggerHelper GetLoggerHelper(IMessageLogger? messageLogger = null)
	{
		return new LoggerHelper(messageLogger ?? new SpyMessageLogger(), new Stopwatch());
	}

	[Fact]
	public void TestCaseFilter_SingleMatch()
	{
		var dummyTestCaseList = GetDummyTestCases();
		var dummyTestCaseDisplayNamefilterString = "Test4";
		// The matching should return a single testcase
		var filterExpression = new MockTestCaseFilterExpression(tc => tc.FullyQualifiedName.Equals(dummyTestCaseDisplayNamefilterString));
		var context = new MockRunContext(() => filterExpression);
		var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);

		var results = dummyTestCaseList.Where(filter.MatchTestCase);

		var result = Assert.Single(results);
		Assert.Equal("Test4", result.FullyQualifiedName);
	}

	[Fact]
	public void TestCaseFilter_NoFilterString()
	{
		var dummyTestCaseList = GetDummyTestCases();
		var context = new MockRunContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);

		var results = dummyTestCaseList.Where(filter.MatchTestCase);

		// Make sure we run the whole set since there is not filtering string specified
		Assert.Equal(dummyTestCaseList.Count, results.Count());
	}

	[Fact]
	public void TestCaseFilter_ErrorParsingFilterString()
	{
		var messageLogger = new SpyMessageLogger();
		var dummyTestCaseList = GetDummyTestCases();
		var context = new MockRunContext(() => throw new TestPlatformFormatException("Hello from the exception"));
		var filter = new TestCaseFilter(context, GetLoggerHelper(messageLogger), "dummyTestAssembly", dummyKnownTraits);

		var results = dummyTestCaseList.Where(filter.MatchTestCase);

		// Make sure we don't run anything due to the filtering string parse error
		Assert.Empty(results);
		var msg = Assert.Single(messageLogger.Messages);
		Assert.Equal("[Warning] [xUnit.net 00:00:00.00] dummyTestAssembly: Exception filtering tests: Hello from the exception", msg);
	}

	[Fact]
	public void TestCaseFilter_TestPropertyProviderWithDiscoveryContextForTraits()
	{
		var dummyTestCase = GetDummyTestCaseWithTraits();
		var context = new MockDiscoveryContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper());
		var targetTraitKey = "Category";

		var propertyResult = filter.PropertyProvider(dummyTestCase, targetTraitKey);

		Assert.NotNull(propertyResult);
		Assert.IsType<string[]>(propertyResult);
		Assert.Equal(dummyTestCase.Traits.Count(t => t.Name.Equals(targetTraitKey, StringComparison.OrdinalIgnoreCase)), ((string[])propertyResult).Count());
	}

	[Fact]
	public void TestCaseFilter_TestPropertyProviderWithDiscoveryContextForFullyQualifiedName()
	{
		var dummyTestCase = GetDummyTestCaseWithTraits();
		var context = new MockDiscoveryContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper());
		var targetName = "FullyQualifiedName";

		var propertyResult = filter.PropertyProvider(dummyTestCase, targetName);

		Assert.NotNull(propertyResult);
		Assert.IsType<string>(propertyResult);
		Assert.Equal("propertyProviderTestFullyName", propertyResult);
	}

	[Fact]
	public void TestCaseFilter_TestPropertyProviderWithDiscoveryContextForDisplayName()
	{
		var dummyTestCase = GetDummyTestCaseWithTraits();
		var context = new MockDiscoveryContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper());
		var targetName = "DisplayName";

		var propertyResult = filter.PropertyProvider(dummyTestCase, targetName);

		Assert.NotNull(propertyResult);
		Assert.IsType<string>(propertyResult);
		Assert.Equal("propertyProviderTestDisplayName", propertyResult);
	}

	[Fact]
	public void TestCaseFilter_TestPropertyProviderWithRunContextForTraits()
	{
		var dummyTestCase = GetDummyTestCaseWithTraits();
		var context = new MockRunContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);
		var targetTraitKey = "Priority";

		var propertyResult = filter.PropertyProvider(dummyTestCase, targetTraitKey);

		Assert.NotNull(propertyResult);
		Assert.IsType<string[]>(propertyResult);
		Assert.Equal(dummyTestCase.Traits.Where(t => t.Name.Equals(targetTraitKey, StringComparison.OrdinalIgnoreCase)).Count(), ((string[])propertyResult).Count());
	}

	[Fact]
	public void TestCaseFilter_TestPropertyProviderWithRunContextForFullyQualifiedName()
	{
		var dummyTestCase = GetDummyTestCaseWithTraits();
		var context = new MockRunContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);
		var targetName = "FullyQualifiedName";

		var propertyResult = filter.PropertyProvider(dummyTestCase, targetName);

		Assert.NotNull(propertyResult);
		Assert.IsType<string>(propertyResult);
		Assert.Equal("propertyProviderTestFullyName", propertyResult);
	}

	[Fact]
	public void TestCaseFilter_TestPropertyProviderWithRunContextForDisplayName()
	{
		var dummyTestCase = GetDummyTestCaseWithTraits();
		var context = new MockRunContext();
		var filter = new TestCaseFilter(context, GetLoggerHelper(), "dummyTestAssembly", dummyKnownTraits);
		var targetName = "DisplayName";

		var propertyResult = filter.PropertyProvider(dummyTestCase, targetName);

		Assert.NotNull(propertyResult);
		Assert.IsType<string>(propertyResult);
		Assert.Equal("propertyProviderTestDisplayName", propertyResult);
	}
}
