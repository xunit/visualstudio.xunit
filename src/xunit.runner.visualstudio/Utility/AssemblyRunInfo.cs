using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

public class AssemblyRunInfo
{
	public AssemblyRunInfo(
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		IList<TestCase>? testCases = null)
	{
		Assembly = new XunitProjectAssembly(project)
		{
			AssemblyFileName = assemblyFileName,
			AssemblyMetadata = AssemblyUtility.GetAssemblyMetadata(assemblyFileName),
		};
		TestCases = testCases;

		runSettings.CopyTo(Assembly.Configuration);
	}

	public XunitProjectAssembly Assembly { get; }

	public IList<TestCase>? TestCases { get; }
}
