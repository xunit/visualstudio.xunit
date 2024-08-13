using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Xunit.v3;

namespace Xunit.Runner.VisualStudio;

internal class DebuggerProcessLauncher(IFrameworkHandle2 frameworkHandle2) :
	OutOfProcessTestProcessLauncherBase
{
	protected override ITestProcess? StartTestProcess(
		string executable,
		string executableArguments,
		string? responseFile)
	{
		var testProcess = LocalTestProcess.Start(executable, executableArguments, responseFile);
		if (testProcess is null)
			return null;

		frameworkHandle2.AttachDebuggerToProcess(testProcess.ProcessID);
		return testProcess;
	}
}
