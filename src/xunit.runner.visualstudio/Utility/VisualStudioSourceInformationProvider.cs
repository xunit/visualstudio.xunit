using System.Threading.Tasks;
using Xunit.Runner.Common;
using Xunit.Runner.VisualStudio.Utility;

namespace Xunit.Runner.VisualStudio;

/// <summary>
/// An implementation of <see cref="ISourceInformationProvider"/> that will provide source information
/// when running inside of Visual Studio (via the DiaSession class).
/// </summary>
/// <param name="assemblyFileName">The assembly file name.</param>
/// <param name="diagnosticMessageSink">The message sink to send diagnostic messages to.</param>
public sealed class VisualStudioSourceInformationProvider(
	string assemblyFileName,
	DiagnosticMessageSink diagnosticMessageSink) :
	ISourceInformationProvider
{
	readonly DiaSessionWrapper session = new(assemblyFileName, diagnosticMessageSink);

	/// <inheritdoc/>
	public (string? sourceFile, int? sourceLine) GetSourceInformation(
		string? testClassName,
		string? testMethodName)
	{
		if (testClassName is null || testMethodName is null)
			return (null, null);

		var navData = session.GetNavigationData(testClassName, testMethodName);
		if (navData is null || navData.FileName is null)
			return (null, null);

		return (navData.FileName, navData.MinLineNumber);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		session.Dispose();
		return default;
	}
}
