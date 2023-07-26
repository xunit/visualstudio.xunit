namespace Xunit.Runner.VisualStudio;

/// <summary>
/// Provides contextual information on a test run/discovery based on runsettings
/// or the invocation (execution, discovery).
/// </summary>
public struct TestPlatformContext
{
	/// <summary>
	/// Indicates if the test case needs to be serialized in VSTestCase instance.
	/// </summary>
	public bool RequireSerialization { get; set; }

	/// <summary>
	/// Indicates if VSTestCase object must have FileName or LineNumber information.
	/// </summary>
	public bool RequireSourceInformation { get; set; }
}
