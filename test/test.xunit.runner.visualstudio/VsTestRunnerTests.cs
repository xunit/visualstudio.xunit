extern alias VSTestAdapter;

using System.ComponentModel;
using System.Reflection;
using Xunit;
using VsTestRunner = VSTestAdapter.Xunit.Runner.VisualStudio.VsTestRunner;

public class VsTestRunnerTests
{
	[Fact]
	public void VSTestRunnerShouldHaveCategoryAttribute_WithValueManaged()
	{
		var attribute = typeof(VsTestRunner).GetCustomAttribute(typeof(CategoryAttribute));

		Assert.NotNull(attribute);
		Assert.Equal("managed", (attribute as CategoryAttribute)?.Category);
	}
}
