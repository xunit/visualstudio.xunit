using Xunit;

public class Tests
{
	[Fact]
	public void Passing() { }

	[Fact(Explicit = true)]
	public void Failing() => Assert.Fail("This is a failing test");

	[Theory]
	[InlineData(true, Explicit = true)]
	[InlineData(false)]
	public void ConditionalSkip(bool value) => Assert.SkipWhen(value, "Conditionally skipped");
}
