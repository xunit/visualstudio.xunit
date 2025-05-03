using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit.BuildTools.Models;

namespace Xunit.BuildTools.Targets;

public static partial class SignAssemblies
{
	public static Task OnExecute(BuildContext context)
	{
		// Check early because we don't need to make copies or show the banner for non-signed scenarios
		if (!context.CanSign)
			return Task.CompletedTask;

		context.BuildStep("Signing binaries");

		// Note that any changes to .nuspec files means this list needs to be updated
		var binaries =
			new[] {
				Path.Combine(context.BaseFolder, "src", "xunit.runner.visualstudio", "bin", context.ConfigurationText, "net472", "merged", "xunit.runner.visualstudio.testadapter.dll"),
				Path.Combine(context.BaseFolder, "src", "xunit.runner.visualstudio", "bin", context.ConfigurationText, "net8.0", "merged", "xunit.runner.visualstudio.testadapter.dll"),
			}.Select(unsignedPath =>
			{
				var unsignedFolder = Path.GetDirectoryName(unsignedPath) ?? throw new InvalidOperationException($"Path '{unsignedPath}' did not have a folder");
				var signedFolder = Path.Combine(unsignedFolder, "signed");
				Directory.CreateDirectory(signedFolder);

				var signedPath = Path.Combine(signedFolder, Path.GetFileName(unsignedPath));
				File.Copy(unsignedPath, signedPath, overwrite: true);

				return signedPath;
			}).ToArray();

		return context.SignFiles(context.BaseFolder, binaries);
	}
}
