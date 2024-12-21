using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

internal class VisualStudioRunnerLogger(LoggerHelper loggerHelper) :
	IRunnerLogger
{
	static readonly object lockObject = new();

	public object LockObject => lockObject;

	public void LogError(
		StackFrameInfo stackFrame,
		string message)
	{
		loggerHelper.LogError("{0}", message);
	}

	public void LogImportantMessage(
		StackFrameInfo stackFrame,
		string message)
	{
		loggerHelper.Log("{0}", message);
	}

	public void LogMessage(
		StackFrameInfo stackFrame,
		string message)
	{
		loggerHelper.Log("{0}", message);
	}

	public void LogRaw(string message)
	{
		loggerHelper.Log("{0}", message);
	}

	public void LogWarning(
		StackFrameInfo stackFrame,
		string message)
	{
		loggerHelper.LogWarning("{0}", message);
	}

	public void WaitForAcknowledgment()
	{ }
}
