using System.IO;
using System.Threading.Tasks;

[Target(BuildTarget.Clean)]
public static class Clean
{
	public static async Task OnExecute(BuildContext context)
	{
		context.BuildStep("Cleaning build artifacts");

		await context.Exec("msbuild", $"visualstudio.xunit.sln /t:Clean /v:{context.Verbosity}");

		if (Directory.Exists(context.ArtifactsFolder))
			Directory.Delete(context.ArtifactsFolder, recursive: true);
	}
}
