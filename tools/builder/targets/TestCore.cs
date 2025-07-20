using System.IO;
using System.Threading.Tasks;
using Xunit.BuildTools.Models;

namespace Xunit.BuildTools.Targets;

[Target(
	BuildTarget.TestCore,
	BuildTarget.Build
)]
public class TestCore
{
	public static async Task OnExecute(BuildContext context)
	{
		context.BuildStep("Running .NET tests");

		Directory.CreateDirectory(context.TestOutputFolder);

		var testFolder = Path.Combine(context.BaseFolder, "test", "test.xunit.runner.visualstudio", "bin", context.ConfigurationText, "net8.0");
		var testPath = Path.Combine(testFolder, "test.xunit.runner.visualstudio.dll");
		var reportPath = Path.Combine(context.TestOutputFolder, "test.xunit.runner.visualstudio-netcore.ctrf");
		File.Delete(reportPath);

		await context.Exec("dotnet", $"exec {testPath} -ctrf {reportPath}");

		context.BuildStep("Running .NET VSTest integration tests");

		await context.Exec("dotnet", $"test test/test.v3 -tl:off --configuration {context.Configuration} --no-build --framework net8.0 --verbosity {context.Verbosity}");
		await context.Exec("dotnet", $"test test/test.v2 -tl:off --configuration {context.Configuration} --no-build --framework net8.0 --verbosity {context.Verbosity}");
	}
}
