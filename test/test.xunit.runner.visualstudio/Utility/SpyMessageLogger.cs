using System.Collections.Generic;

namespace Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

public class SpyMessageLogger : IMessageLogger
{
	public readonly List<string> Messages = [];

	public void SendMessage(
		TestMessageLevel testMessageLevel,
		string message) =>
			Messages.Add($"[{testMessageLevel}] {message}");
}
