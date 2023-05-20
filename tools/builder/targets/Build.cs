using System.Threading.Tasks;

[Target(
	BuildTarget.Build,
	BuildTarget.Restore
)]
public static class Build
{
	public static async Task OnExecute(BuildContext context)
	{
		context.BuildStep("Compiling binaries");

		await context.Exec("msbuild", $"visualstudio.xunit.sln /m /v:{context.Verbosity} /p:Configuration={context.ConfigurationText} /p:PackageOutputPath={context.PackageOutputFolder}");
	}
}
