using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit.Runner.Common;
using Xunit.Sdk;

namespace Xunit.Runner.VisualStudio;

public class AssemblyRunInfo
{
	AssemblyRunInfo(
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		AssemblyMetadata assemblyMetadata,
		IList<TestCase>? testCases,
		bool runExplicitTests)
	{
		Assembly = new XunitProjectAssembly(project, assemblyFileName, assemblyMetadata);
		TestCases = testCases;

		runSettings.CopyTo(Assembly.Configuration);
		Assembly.Configuration.ExplicitOption = runExplicitTests ? ExplicitOption.On : ExplicitOption.Off;
	}

	public XunitProjectAssembly Assembly { get; }

	public IList<TestCase>? TestCases { get; }

	public static AssemblyRunInfo? Create(
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		IList<TestCase>? testCases = null,
		bool runExplicitTests = false)
	{
		var metadata = AssemblyUtility.GetAssemblyMetadata(assemblyFileName);
		if (metadata is null || metadata.XunitVersion == 0)
			return null;

		return new(project, runSettings, assemblyFileName, metadata, testCases, runExplicitTests);
	}
}
