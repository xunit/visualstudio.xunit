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
		File.Delete(Path.Combine(context.TestOutputFolder, "test.xunit.runner.visualstudio-netfx.trx"));

		await context.Exec("dotnet", $"test test/test.xunit.runner.visualstudio --configuration {context.Configuration} --no-build --framework net472 --logger trx;LogFileName=test.xunit.runner.visualstudio-netfx.trx --results-directory \"{context.TestOutputFolder}\" --verbosity {context.Verbosity}");
	}
}
