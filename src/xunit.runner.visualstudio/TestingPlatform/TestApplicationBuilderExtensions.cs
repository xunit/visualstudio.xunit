using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Testing.Extensions.VSTestBridge.Capabilities;
using Microsoft.Testing.Extensions.VSTestBridge.Helpers;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Capabilities.TestFramework;

namespace Xunit.Runner.VisualStudio;

public static class TestApplicationBuilderExtensions
{
	public static void AddXunit(
		this ITestApplicationBuilder testApplicationBuilder,
		Func<IEnumerable<Assembly>> getTestAssemblies)
	{
		XunitExtension extension = new();
		testApplicationBuilder.AddRunSettingsService(extension);
		testApplicationBuilder.AddTestCaseFilterService(extension);
		testApplicationBuilder.RegisterTestFramework(
			_ => new TestFrameworkCapabilities(new VSTestBridgeExtensionBaseCapabilities()),
			(capabilities, serviceProvider) => new XunitBridgedTestFramework(extension, getTestAssemblies, serviceProvider, capabilities)
		);
	}
}
