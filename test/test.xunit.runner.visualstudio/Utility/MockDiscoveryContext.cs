namespace Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

public class MockDiscoveryContext : IDiscoveryContext
{
	public IRunSettings? RunSettings { get; set; }
}
