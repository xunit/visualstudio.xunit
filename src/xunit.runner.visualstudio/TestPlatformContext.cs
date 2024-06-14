namespace Xunit.Runner.VisualStudio;

/// <summary>
/// Provides contextual information on a test run/discovery based on runsettings
/// or the invocation (execution, discovery).
/// </summary>
public struct TestPlatformContext
{
	/// <summary>
	/// Indicates if the test runner is running in design mode (meaning, inside the Visual Studio IDE).
	/// </summary>
	public bool DesignMode { get; set; }
}
