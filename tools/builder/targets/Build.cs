using System.Threading.Tasks;
using Xunit.BuildTools.Models;

namespace Xunit.BuildTools.Targets;

public static partial class Build
{
	public static partial Task PerformBuild(BuildContext context)
	{
		context.BuildStep("Compiling binaries");

		return context.Exec("dotnet", $"msbuild -nologo -maxCpuCount -restore:False -verbosity:{context.Verbosity} -p:Configuration={context.ConfigurationText} -p:PackageOutputPath={context.PackageOutputFolder}");
	}
}
