using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Xunit.Runner.VisualStudio;

internal class TestCaseFilter
{
	const string DisplayNameString = "DisplayName";
	const string FullyQualifiedNameString = "FullyQualifiedName";

	readonly HashSet<string> knownTraits;
	List<string> supportedPropertyNames;
	readonly ITestCaseFilterExpression? filterExpression;
	readonly bool successfullyGotFilter;
	readonly bool isDiscovery;

	public TestCaseFilter(
		IRunContext runContext,
		LoggerHelper logger,
		string assemblyFileName,
		HashSet<string> knownTraits)
	{
		this.knownTraits = knownTraits;
		supportedPropertyNames = GetSupportedPropertyNames();

		successfullyGotFilter = GetTestCaseFilterExpression(runContext, logger, assemblyFileName, out filterExpression);
	}

	public TestCaseFilter(
		IDiscoveryContext discoveryContext,
		LoggerHelper logger)
	{
		// Traits are not known at discovery time because we load them from tests
		isDiscovery = true;
		knownTraits = [];
		supportedPropertyNames = GetSupportedPropertyNames();

		successfullyGotFilter = GetTestCaseFilterExpressionFromDiscoveryContext(discoveryContext, logger, out filterExpression);
	}

	public bool MatchTestCase(TestCase testCase)
	{
		// Had an error while getting filter, match no testcase to ensure discovered test list is empty
		if (!successfullyGotFilter)
			return false;

		// No filter specified, keep every testcase
		if (filterExpression is null)
			return true;

		return filterExpression.MatchTestCase(testCase, (p) => PropertyProvider(testCase, p));
	}

	public object? PropertyProvider(
		TestCase testCase,
		string name)
	{
		// Special case for "FullyQualifiedName" and "DisplayName"
		if (string.Equals(name, FullyQualifiedNameString, StringComparison.OrdinalIgnoreCase))
			return testCase.FullyQualifiedName;
		if (string.Equals(name, DisplayNameString, StringComparison.OrdinalIgnoreCase))
			return testCase.DisplayName;

		// Traits filtering
		if (isDiscovery || knownTraits.Contains(name))
		{
			var result = new List<string>();

			foreach (var trait in GetTraits(testCase))
				if (string.Equals(trait.Key, name, StringComparison.OrdinalIgnoreCase))
					result.Add(trait.Value);

			if (result.Count > 0)
				return result.ToArray();
		}

		return null;
	}

	bool GetTestCaseFilterExpression(
		IRunContext runContext,
		LoggerHelper logger,
		string assemblyFileName,
		out ITestCaseFilterExpression? filter)
	{
		filter = null;

		try
		{
			filter = runContext.GetTestCaseFilter(supportedPropertyNames, s => null);
			return true;
		}
		catch (TestPlatformFormatException e)
		{
			logger.LogWarning("{0}: Exception filtering tests: {1}", Path.GetFileNameWithoutExtension(assemblyFileName), e.Message);
			return false;
		}
	}

	bool GetTestCaseFilterExpressionFromDiscoveryContext(
		IDiscoveryContext discoveryContext,
		LoggerHelper logger,
		out ITestCaseFilterExpression? filter)
	{
		filter = null;

		if (discoveryContext is IRunContext runContext)
		{
			try
			{
				filter = runContext.GetTestCaseFilter(supportedPropertyNames, s => null);
				return true;
			}
			catch (TestPlatformException e)
			{
				logger.LogWarning("Exception filtering tests: {0}", e.Message);
				return false;
			}
		}
		else
		{
			try
			{
				static TestProperty? noop(string name) => null;

				// GetTestCaseFilter is present on DiscoveryContext but not in IDiscoveryContext interface
				var method = discoveryContext.GetType().GetRuntimeMethod("GetTestCaseFilter", [typeof(IEnumerable<string>), typeof(Func<string, TestProperty?>)]);
				filter = method?.Invoke(discoveryContext, [supportedPropertyNames, (object)noop]) as ITestCaseFilterExpression;
				return true;
			}
			catch (TargetInvocationException e)
			{
				if (e.InnerException is TestPlatformException ex)
				{
					logger.LogWarning("Exception filtering tests: {0}", ex.InnerException?.Message);
					return false;
				}

				(e.InnerException ?? e).Rethrow();

				// We will never reach this because of the line above, but we need to keep the compiler happy
				throw new InvalidOperationException("This line should never be executed");
			}
		}
	}

	List<string> GetSupportedPropertyNames()
	{
		// Returns the set of well-known property names usually used with the Test Plugins (Used Test Traits + DisplayName + FullyQualifiedName)
		supportedPropertyNames ??= [.. knownTraits, DisplayNameString, FullyQualifiedNameString];
		return supportedPropertyNames;
	}

	static IEnumerable<KeyValuePair<string, string>> GetTraits(TestCase testCase)
	{
		var traitProperty = TestProperty.Find("TestObject.Traits");
		if (traitProperty is not null)
			return testCase.GetPropertyValue(traitProperty, Array.Empty<KeyValuePair<string, string>>());

		return [];
	}
}
