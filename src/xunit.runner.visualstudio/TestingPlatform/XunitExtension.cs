using System.Threading.Tasks;
using Microsoft.Testing.Platform.Extensions;

namespace Xunit.Runner.VisualStudio;

internal sealed class XunitExtension : IExtension
{
	public string Uid => nameof(XunitExtension);

	public string DisplayName => "xUnit";

	public string Version => ThisAssembly.AssemblyVersion;

	public string Description => "xUnit Framework for Microsoft Testing Platform";

	public Task<bool> IsEnabledAsync() => Task.FromResult(true);
}
