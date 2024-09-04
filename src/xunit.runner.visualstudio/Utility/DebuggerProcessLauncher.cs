using System.IO;
using System.Linq;
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

		var waitForDebugger = false;

		if (responseFile is not null)
		{
			try
			{
				var switches = File.ReadAllLines(responseFile);
				waitForDebugger = switches.Any(s => s.Equals("-waitForDebugger", System.StringComparison.OrdinalIgnoreCase));
			}
			catch { }
		}

		if (waitForDebugger)
			try
			{
				frameworkHandle2.AttachDebuggerToProcess(testProcess.ProcessID);
			}
			catch { }

		return testProcess;
	}
}
