using System.Reflection;

using Microsoft.Testing.Platform.Builder;

namespace Xunit.Runner.VisualStudio;

public static class TestingPlatformBuilderHook
{
#pragma warning disable IDE0060 // Remove unused parameter
	public static void AddExtensions(ITestApplicationBuilder testApplicationBuilder, string[] arguments)
#pragma warning restore IDE0060 // Remove unused parameter
	{
		testApplicationBuilder.AddXunit(() => [Assembly.GetEntryAssembly()!]);
	}
}
