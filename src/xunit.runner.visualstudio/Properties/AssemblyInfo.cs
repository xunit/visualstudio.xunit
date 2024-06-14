// This is not targeted for assemblies in .NET Framework, but when it's seen on an assembly for code coverage
// purposes it'll still be honored, so we redefine it here and disable CS0436 at the project level so that
// the project will still build properly.

[assembly: System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

#if NETFRAMEWORK
namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	internal sealed class ExcludeFromCodeCoverageAttribute : Attribute { }
}
#endif
