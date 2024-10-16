using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit.Runner.Common;
using Xunit.Sdk;

namespace Xunit.Runner.VisualStudio;

public class AssemblyRunInfo
{
	AssemblyRunInfo(
		LoggerHelper logger,
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		AssemblyMetadata assemblyMetadata,
		IList<TestCase>? testCases,
		bool runExplicitTests)
	{
		Assembly = new XunitProjectAssembly(project, assemblyFileName, assemblyMetadata);
		TestCases = testCases;

		var configWarnings = new List<string>();
		ConfigReader.Load(Assembly.Configuration, Assembly.AssemblyFileName, Assembly.ConfigFileName, configWarnings);
		runSettings.CopyTo(Assembly.Configuration);

		// The Test Explorer UI doesn't give the user the ability to specify this, so if we haven't come along
		// and set it via the command line, we'll use our auto-calculation based on what we think the user wants
		Assembly.Configuration.ExplicitOption ??= runExplicitTests ? ExplicitOption.On : ExplicitOption.Off;

		foreach (var warning in configWarnings)
			logger.LogWarning("{0}", warning);
	}

	public XunitProjectAssembly Assembly { get; }

	public IList<TestCase>? TestCases { get; }

	public static AssemblyRunInfo? Create(
		LoggerHelper logger,
		XunitProject project,
		RunSettings runSettings,
		string assemblyFileName,
		IList<TestCase>? testCases = null,
		bool runExplicitTests = false)
	{
		var metadata = AssemblyUtility.GetAssemblyMetadata(assemblyFileName);
		if (metadata is null || metadata.XunitVersion == 0)
			return null;

		return new(logger, project, runSettings, assemblyFileName, metadata, testCases, runExplicitTests);
	}
}
