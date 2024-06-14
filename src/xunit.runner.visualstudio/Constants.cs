namespace Xunit.Runner.VisualStudio;

public static class Constants
{
#if NETFRAMEWORK
	public const string ExecutorUri = "executor://xunit/VsTestRunner3/netfx/";
#elif NETCOREAPP
	public const string ExecutorUri = "executor://xunit/VsTestRunner3/netcore/";
#else
#error Unknown target platform
#endif
}
