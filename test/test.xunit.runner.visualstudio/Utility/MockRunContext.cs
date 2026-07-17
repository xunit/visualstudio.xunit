using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

public class MockRunContext(Func<ITestCaseFilterExpression?>? getTestCaseFilterSelector = null) :
	IRunContext
{
	public bool InIsolation { get; set; }
	public bool IsDataCollectionEnabled { get; set; }
	public bool IsBeingDebugged { get; set; }
	public bool KeepAlive { get; set; }
	public IRunSettings? RunSettings { get; set; }
	public string? SolutionDirectory { get; set; }
	public string? TestRunDirectory { get; set; }

	public ITestCaseFilterExpression? GetTestCaseFilter(
		IEnumerable<string>? supportedProperties,
		Func<string, TestProperty?> propertyProvider) =>
			getTestCaseFilterSelector?.Invoke();
}
