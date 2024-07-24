using System;
using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

/// <summary>
/// Used to discover tests before running when VS says "run everything in the assembly".
/// </summary>
internal class VsExecutionDiscoverySink(Func<bool> cancelThunk) :
	TestDiscoverySink(cancelThunk), IVsDiscoverySink
{
	public int Finish()
	{
		Finished.WaitOne();
		return TestCases.Count;
	}
}
