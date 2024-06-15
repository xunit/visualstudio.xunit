using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

public class AssemblyRunInfo
{
	AssemblyRunInfo(
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		AssemblyMetadata assemblyMetadata,
		IList<TestCase>? testCases)
	{
		Assembly = new XunitProjectAssembly(project, assemblyFileName, assemblyMetadata);
		TestCases = testCases;

		runSettings.CopyTo(Assembly.Configuration);
	}

	public XunitProjectAssembly Assembly { get; }

	public IList<TestCase>? TestCases { get; }

	public static AssemblyRunInfo? Create(
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		IList<TestCase>? testCases = null)
	{
		var metadata = AssemblyUtility.GetAssemblyMetadata(assemblyFileName);
		if (metadata is null || metadata.XunitVersion == 0)
			return null;

		return new(project, runSettings, assemblyFileName, metadata, testCases);
	}
}
