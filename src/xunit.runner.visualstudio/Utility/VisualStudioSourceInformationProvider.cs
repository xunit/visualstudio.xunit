using Xunit.Abstractions;
using Xunit.Runner.VisualStudio.Utility;
using Xunit.Sdk;

namespace Xunit.Runner.VisualStudio;

/// <summary>
/// An implementation of <see cref="ISourceInformationProvider"/> that will provide source information
/// when running inside of Visual Studio (via the DiaSession class).
/// </summary>
public class VisualStudioSourceInformationProvider : LongLivedMarshalByRefObject, ISourceInformationProvider
{
	static readonly SourceInformation EmptySourceInformation = new();

	readonly DiaSessionWrapper session;

	/// <summary>
	/// Initializes a new instance of the <see cref="VisualStudioSourceInformationProvider" /> class.
	/// </summary>
	/// <param name="assemblyFileName">The assembly file name.</param>
	public VisualStudioSourceInformationProvider(string assemblyFileName)
	{
		session = new DiaSessionWrapper(assemblyFileName);
	}

	/// <inheritdoc/>
	public ISourceInformation GetSourceInformation(ITestCase testCase)
	{
		var navData = session.GetNavigationData(testCase.TestMethod.TestClass.Class.Name, testCase.TestMethod.Method.Name);
		if (navData is null || navData.FileName is null)
			return EmptySourceInformation;

		return new SourceInformation
		{
			FileName = navData.FileName,
			LineNumber = navData.MinLineNumber
		};
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		session.Dispose();
	}
}
