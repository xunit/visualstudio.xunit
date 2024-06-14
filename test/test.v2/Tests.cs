using Xunit;

public class Tests
{
	[Fact]
	public void Passing() { }

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void ConditionallyFailing(int value) => Assert.Equal(0, value % 2);

	[Fact(Skip = "Unconditionally skipped")]
	public void Skipped() => Assert.Fail("This does not run");
}
