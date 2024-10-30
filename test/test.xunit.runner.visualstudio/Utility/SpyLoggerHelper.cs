using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Xunit.Runner.VisualStudio;

public class SpyLoggerHelper(SpyMessageLogger logger, Stopwatch stopwatch) :
	LoggerHelper(logger, stopwatch)
{
	public IReadOnlyCollection<string> Messages => logger.Messages;

	public static SpyLoggerHelper Create() =>
		new(new(), new());
}
