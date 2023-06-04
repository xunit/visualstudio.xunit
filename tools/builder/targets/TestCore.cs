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
		File.Delete(Path.Combine(context.TestOutputFolder, "test.xunit.runner.visualstudio-net6.0.trx"));

		await context.Exec("dotnet", $"test test/test.xunit.runner.visualstudio --configuration {context.Configuration} --no-build --framework net6.0 --logger trx;LogFileName=test.xunit.runner.visualstudio-net6.0.trx --results-directory \"{context.TestOutputFolder}\" --verbosity {context.Verbosity}");
	}
}
