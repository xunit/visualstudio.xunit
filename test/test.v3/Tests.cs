using Xunit;

public class Tests
{
	[Fact]
	public void Passing() { }

	[Fact]
	public void Failing() => Assert.Fail("This is a failing test");

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ConditionalSkip(bool value) => Assert.SkipWhen(value, "Conditionally skipped");
}
