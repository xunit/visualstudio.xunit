using System.Collections.Generic;

namespace Xunit.BuildTools.Models;

public partial class BuildContext
{
	public partial IReadOnlyList<string> GetSkippedAnalysisFolders() =>
		new[]
		{
			"src/xunit.runner.visualstudio/Utility/AssemblyResolution/Microsoft.DotNet.PlatformAbstractions",
			"src/xunit.runner.visualstudio/Utility/AssemblyResolution/Microsoft.Extensions.DependencyModel"
		};
}
