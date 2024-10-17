using System.Diagnostics.CodeAnalysis;
using Xunit.Runner.Common;

// This is the default list of runner reporters, copied from xunit.v3.core's package

[assembly: RegisterRunnerReporter(typeof(AppVeyorReporter))]
[assembly: RegisterRunnerReporter(typeof(DefaultRunnerReporter))]
[assembly: RegisterRunnerReporter(typeof(JsonReporter))]
[assembly: RegisterRunnerReporter(typeof(QuietReporter))]
[assembly: RegisterRunnerReporter(typeof(SilentReporter))]
[assembly: RegisterRunnerReporter(typeof(TeamCityReporter))]
[assembly: RegisterRunnerReporter(typeof(VerboseReporter))]
[assembly: RegisterRunnerReporter(typeof(VstsReporter))]

// This is not targeted for assemblies in .NET Framework, but when it's seen on an assembly for code coverage
// purposes it'll still be honored, so we redefine it here and disable CS0436 at the project level so that
// the project will still build properly.

[assembly: ExcludeFromCodeCoverage]

#if NETFRAMEWORK
namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	internal sealed class ExcludeFromCodeCoverageAttribute : Attribute { }
}
#endif
