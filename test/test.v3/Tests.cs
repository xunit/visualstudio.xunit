using Xunit;

public class Tests
{
	[Fact]
	public void Passing() { }

	[Fact(Skip = "Unconditionally skipped")]
	public void Skipped() => Assert.Fail("This does not run");

	[Fact(Explicit = true)]
	public void Explicit() => Assert.Fail("This only runs explicitly.");
}
