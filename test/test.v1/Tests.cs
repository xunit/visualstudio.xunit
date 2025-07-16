using Xunit;

public class Tests
{
	[Fact]
	public void Passing() { }

	[Fact(Skip = "Unconditionally skipped")]
	public void Skipped() => Assert.True(false, "This does not run");
}
