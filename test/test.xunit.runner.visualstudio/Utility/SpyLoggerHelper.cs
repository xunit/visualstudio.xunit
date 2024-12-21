extern alias VSTestAdapter;

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using LoggerHelper = VSTestAdapter.Xunit.Runner.VisualStudio.LoggerHelper;

namespace Xunit.Runner.VisualStudio;

internal class SpyLoggerHelper(SpyMessageLogger logger, Stopwatch stopwatch) :
	LoggerHelper(logger, stopwatch)
{
	public IReadOnlyCollection<string> Messages => logger.Messages;

	public static SpyLoggerHelper Create() =>
		new(new(), new());
}
