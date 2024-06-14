using Xunit;
using Xunit.Extensions;

public class Tests
{
	[Fact]
	public void Passing() { }

	// We did not support pre-enumerated theories in v1, so this will show up in Test Explorer
	// as a single test with multiple results.
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void ConditionallyFailing(int value) => Assert.Equal(0, value % 2);


	[Fact(Skip = "Unconditionally skipped")]
	public void Skipped() => Assert.True(false, "This does not run");
}
