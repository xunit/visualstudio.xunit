using System;

namespace Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

public class MockTestCaseFilterExpression(Func<TestCase, bool>? matchSelector = null) :
	ITestCaseFilterExpression
{
	public string TestCaseFilterValue { get; set; } = "<unset>";

	public bool MatchTestCase(
		TestCase testCase,
		Func<string, object?> propertyValueProvider) =>
			matchSelector?.Invoke(testCase) ?? false;
}
