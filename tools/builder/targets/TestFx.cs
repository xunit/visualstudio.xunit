using System.IO;
using System.Threading.Tasks;
using Xunit.BuildTools.Models;

namespace Xunit.BuildTools.Targets;

[Target(
	BuildTarget.TestFx,
	BuildTarget.Build
)]
public class TestFx
{
	public static async Task OnExecute(BuildContext context)
	{
		context.BuildStep("Running .NET Framework tests");

		Directory.CreateDirectory(context.TestOutputFolder);

		var testFolder = Path.Combine(context.BaseFolder, "test", "test.xunit.runner.visualstudio", "bin", context.ConfigurationText, "net472");
		var testPath = Path.Combine(testFolder, "test.xunit.runner.visualstudio.exe");
		var reportPath = Path.Combine(context.TestOutputFolder, "test.xunit.runner.visualstudio-netfx.ctrf");
		File.Delete(reportPath);

		await context.Exec(testPath, $"-ctrf {reportPath}", testFolder);

		context.BuildStep("Running .NET Framework VSTest integration tests");

		await context.Exec("dotnet", $"test test/test.v3 -tl:off --configuration {context.Configuration} --no-build --framework net472 --verbosity {context.Verbosity}");
		await context.Exec("dotnet", $"test test/test.v2 -tl:off --configuration {context.Configuration} --no-build --framework net472 --verbosity {context.Verbosity}");
		await context.Exec("dotnet", $"test test/test.v1 -tl:off --configuration {context.Configuration} --no-build --framework net472 --verbosity {context.Verbosity}");
	}
}
