using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

internal class DiagnosticMessageSink : DiagnosticEventSink
{
	public DiagnosticMessageSink(
		LoggerHelper log,
		string? assemblyDisplayName = null,
		bool showDiagnostics = false,
		bool showInternalDiagnostics = false)
	{
		var header = assemblyDisplayName is null ? string.Empty : assemblyDisplayName + ": ";

		if (showDiagnostics)
			DiagnosticMessageEvent += args => log.LogWarning("{0}{1}", header, args.Message.Message);
		if (showInternalDiagnostics)
			InternalDiagnosticMessageEvent += args => log.Log("{0}", args.Message.Message);
	}
}
