using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Xunit.Runner.VisualStudio;

public class AssemblyRunInfo
{
	public AssemblyRunInfo(
		RunSettings runSettings,
		string assemblyFileName,
		IList<TestCase>? testCases = null)
	{
		Assembly = new XunitProjectAssembly { AssemblyFilename = assemblyFileName };
		TestCases = testCases;

		runSettings.CopyTo(Assembly.Configuration);
	}

	public XunitProjectAssembly Assembly { get; }

	public IList<TestCase>? TestCases { get; }
}
