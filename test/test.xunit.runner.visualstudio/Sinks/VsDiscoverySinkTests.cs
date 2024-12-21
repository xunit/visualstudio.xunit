extern alias VSTestAdapter;

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Constants = VSTestAdapter.Xunit.Runner.VisualStudio.Constants;
using TestPlatformContext = VSTestAdapter.Xunit.Runner.VisualStudio.TestPlatformContext;
using VsDiscoverySink = VSTestAdapter.Xunit.Runner.VisualStudio.VsDiscoverySink;
using VsTestRunner = VSTestAdapter.Xunit.Runner.VisualStudio.VsTestRunner;

public class VsDiscoverySinkTests
{
	public class CreateVsTestCase
	{
		readonly SpyLoggerHelper logger = SpyLoggerHelper.Create();
		readonly TestPlatformContext testPlatformContext = new TestPlatformContext { DesignMode = false };

		[Fact]
		public void MustSetTestClassName()
		{
			var testCase = TestData.TestCaseDiscovered(testClassName: null);

			var vsTestCase = VsDiscoverySink.CreateVsTestCase("source", testCase, logger, testPlatformContext);

			Assert.Null(vsTestCase);
			var message = Assert.Single(logger.Messages);
			Assert.Equal("[Error] [xUnit.net 00:00:00.00] source: Error creating Visual Studio test case for test-case-display-name: TestClassName is null", message);
		}

		[Theory]
		[InlineData(false, null)]
		[InlineData(true, "serialization")]
		public void StandardData(
			bool designMode,
			string? expectedSerialization)
		{
			var testCase = TestData.TestCaseDiscovered(
				sourceFilePath: "/source/file.cs",
				sourceLineNumber: 42,
				traits: new Dictionary<string, IReadOnlyCollection<string>>
				{
					{ "foo", ["baz", "bar"] },
					{ "biff", ["42"] },
				}
			);
			var testPlatformContext = new TestPlatformContext { DesignMode = designMode };

			var vsTestCase = VsDiscoverySink.CreateVsTestCase("source", testCase, logger, testPlatformContext);

			Assert.NotNull(vsTestCase);

			// Standard VSTest properties
			Assert.Equal("/source/file.cs", vsTestCase.CodeFilePath);
			Assert.Equal("test-case-display-name", vsTestCase.DisplayName);
			Assert.Equal(Constants.ExecutorUri, vsTestCase.ExecutorUri.OriginalString);
			Assert.Equal("test-class-name.test-method", vsTestCase.FullyQualifiedName);
			Assert.NotEqual(Guid.Empty, vsTestCase.Id);  // Computed at runtime, just need to ensure it's set
			Assert.Equal(42, vsTestCase.LineNumber);
			Assert.Equal("source", vsTestCase.Source);
			Assert.Collection(
				vsTestCase.Traits.Select(t => $"'{t.Name}' = '{t.Value}'").OrderBy(x => x),
				trait => Assert.Equal("'biff' = '42'", trait),
				trait => Assert.Equal("'foo' = 'bar'", trait),
				trait => Assert.Equal("'foo' = 'baz'", trait)
			);

			// xUnit.net extension properties
			Assert.Equal(expectedSerialization, vsTestCase.GetPropertyValue(VsTestRunner.TestCaseSerializationProperty));
			Assert.Equal("test-case-id", vsTestCase.GetPropertyValue(VsTestRunner.TestCaseUniqueIDProperty));
			Assert.Equal(false, vsTestCase.GetPropertyValue(VsTestRunner.TestCaseExplicitProperty));
		}

		[Theory]
		[InlineData(null, "test-method")]
		[InlineData(new[] { "Type1", "Type2" }, "test-method(Type1,Type2)")]
		public void SetsManagedTypeAndMethodProperties(
			string[]? parameterTypes,
			string expectedManagedMethodName)
		{
			var testCase = TestData.TestCaseDiscovered(testMethodParameterTypes: parameterTypes);

			var vsTestCase = VsDiscoverySink.CreateVsTestCase("source", testCase, logger, testPlatformContext);

			Assert.NotNull(vsTestCase);
			Assert.Equal("test-class-name", vsTestCase.GetPropertyValue(VsTestRunner.ManagedTypeProperty));
			Assert.Equal(expectedManagedMethodName, vsTestCase.GetPropertyValue(VsTestRunner.ManagedMethodProperty));
		}

		[Fact]
		public void SetsSkipReason()
		{
			var testCase = TestData.TestCaseDiscovered(skipReason: "the-skip-reason");

			var vsTestCase = VsDiscoverySink.CreateVsTestCase("source", testCase, logger, testPlatformContext);

			Assert.NotNull(vsTestCase);
			Assert.Equal("the-skip-reason", vsTestCase.GetPropertyValue(VsTestRunner.SkipReasonProperty));
		}
	}
}
