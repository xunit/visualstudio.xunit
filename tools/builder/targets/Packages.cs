using System.Threading.Tasks;
using Xunit.BuildTools.Models;

namespace Xunit.BuildTools.Targets;

public static partial class Packages
{
	public static async Task OnExecute(BuildContext context)
	{
		context.BuildStep("Creating NuGet packages");

		var packArgs = $"pack --nologo --no-build --configuration {context.ConfigurationText} --output {context.PackageOutputFolder} --verbosity {context.Verbosity} src/xunit.runner.visualstudio -p:NuspecFile=xunit.runner.visualstudio.nuspec";
		await context.Exec("dotnet", packArgs);
	}
}
