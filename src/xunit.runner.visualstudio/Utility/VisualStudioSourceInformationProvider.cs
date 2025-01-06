using System.Threading.Tasks;
using Xunit.Runner.Common;
using Xunit.Sdk;

namespace Xunit.Runner.VisualStudio;

/// <summary>
/// An implementation of <see cref="ISourceInformationProvider"/> that will provide source information
/// when running inside of Visual Studio (via the DiaSession class).
/// </summary>
/// <param name="assemblyFileName">The assembly file name.</param>
/// <param name="diagnosticMessageSink">The message sink to send internal diagnostic messages to.</param>
internal class VisualStudioSourceInformationProvider(
	string assemblyFileName,
	DiagnosticMessageSink diagnosticMessageSink) :
		LongLivedMarshalByRefObject, ISourceInformationProvider
{
	static readonly SourceInformation EmptySourceInformation = new();

	readonly DiaSessionWrapper session = new DiaSessionWrapper(assemblyFileName, diagnosticMessageSink);

	/// <inheritdoc/>
	public SourceInformation GetSourceInformation(
		string? testClassName,
		string? testMethodName)
	{
		if (testClassName is null || testMethodName is null)
			return EmptySourceInformation;

		var navData = session.GetNavigationData(testClassName, testMethodName);
		if (navData is null || navData.FileName is null)
			return EmptySourceInformation;

		return new SourceInformation(navData.FileName, navData.MinLineNumber);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		session.Dispose();

		return default;
	}
}
