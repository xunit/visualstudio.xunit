using System;
using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

public class DiagnosticMessageSink : DiagnosticEventSink
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

	[Obsolete("Would like to see this collapsed")]
	public static DiagnosticMessageSink ForDiagnostics(
		LoggerHelper log,
		string assemblyDisplayName,
		bool showDiagnostics) =>
			new(log, assemblyDisplayName, showDiagnostics, showInternalDiagnostics: false);

	[Obsolete("Would like to see this collapsed")]
	public static DiagnosticMessageSink ForInternalDiagnostics(
		LoggerHelper log,
		bool showInternalDiagnostics) =>
			new(log, assemblyDisplayName: null, showDiagnostics: false, showInternalDiagnostics);
}
