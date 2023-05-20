[assembly: System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]

#if NETFRAMEWORK
namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Constructor | AttributeTargets.Event | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
	internal sealed class ExcludeFromCodeCoverageAttribute : Attribute { }
}
#endif
