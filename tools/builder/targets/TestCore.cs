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
		context.BuildStep("Running .NET Core tests");

		Directory.CreateDirectory(context.TestOutputFolder);
		File.Delete(Path.Combine(context.TestOutputFolder, "test.xunit.runner.visualstudio-netcore.trx"));

		await context.Exec("dotnet", $"test test/test.xunit.runner.visualstudio -tl:off --configuration {context.Configuration} --no-build --framework net8.0 --logger trx;LogFileName=test.xunit.runner.visualstudio-netcore.trx --results-directory \"{context.TestOutputFolder}\" --verbosity {context.Verbosity}");
		await context.Exec("dotnet", $"test test/test.v2 -tl:off --configuration {context.Configuration} --no-build --framework net472 --verbosity {context.Verbosity}");
		await context.Exec("dotnet", $"test test/test.v1 -tl:off --configuration {context.Configuration} --no-build --framework net472 --verbosity {context.Verbosity}");
	}
}
