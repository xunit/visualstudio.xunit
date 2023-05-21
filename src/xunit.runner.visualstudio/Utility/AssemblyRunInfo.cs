using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace Xunit.Runner.VisualStudio;

public class AssemblyRunInfo
{
	public AssemblyRunInfo(
		string assemblyFileName,
		TestAssemblyConfiguration configuration,
		IList<TestCase>? testCases)
	{
		AssemblyFileName = assemblyFileName;
		Configuration = configuration;
		TestCases = testCases;
	}

	public string AssemblyFileName { get; }

	public TestAssemblyConfiguration Configuration { get; }

	public IList<TestCase>? TestCases { get; }
}
