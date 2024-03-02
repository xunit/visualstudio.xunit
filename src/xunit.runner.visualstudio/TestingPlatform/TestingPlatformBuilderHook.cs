using System.Reflection;
using Microsoft.Testing.Platform.Builder;

namespace Xunit.Runner.VisualStudio;

public static class TestingPlatformBuilderHook
{
	public static void AddExtensions(
		ITestApplicationBuilder testApplicationBuilder,
		string[] _) =>
			testApplicationBuilder.AddXunit(() => [Assembly.GetEntryAssembly()!]);
}
