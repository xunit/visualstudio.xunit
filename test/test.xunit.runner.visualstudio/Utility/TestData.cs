using System.Collections.Generic;
using Xunit.Runner.Common;
using Xunit.Sdk;

internal static class TestData
{
	static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyTraits = new Dictionary<string, IReadOnlyCollection<string>>();

	public static ITestCaseDiscovered TestCaseDiscovered(
		string assemblyUniqueID = "assembly-id",
		bool @explicit = false,
		string serialization = "serialization",
		string? skipReason = null,
		string? sourceFilePath = null,
		int? sourceLineNumber = null,
		string testCaseDisplayName = "test-case-display-name",
		string testCaseUniqueID = "test-case-id",
		int? testClassMetadataToken = null,
		string? testClassName = "test-class-name",
		string? testClassNamespace = null,
		string? testClassSimpleName = "test-class-simple-name",
		string? testClassUniqueID = "test-class-id",
		string testCollectionUniqueID = "test-collection-id",
		int? testMethodMetadataToken = null,
		string? testMethodName = "test-method",
		string[]? testMethodParameterTypes = null,
		string? testMethodReturnType = null,
		string? testMethodUniqueID = "test-method-id",
		IReadOnlyDictionary<string, IReadOnlyCollection<string>>? traits = null) =>
			new TestCaseDiscovered
			{
				AssemblyUniqueID = assemblyUniqueID,
				Explicit = @explicit,
				Serialization = serialization,
				SkipReason = skipReason,
				SourceFilePath = sourceFilePath,
				SourceLineNumber = sourceLineNumber,
				TestCaseDisplayName = testCaseDisplayName,
				TestCaseUniqueID = testCaseUniqueID,
				TestClassMetadataToken = testClassMetadataToken,
				TestClassName = testClassName,
				TestClassNamespace = testClassNamespace,
				TestClassSimpleName = testClassSimpleName,
				TestClassUniqueID = testClassUniqueID,
				TestCollectionUniqueID = testCollectionUniqueID,
				TestMethodMetadataToken = testMethodMetadataToken,
				TestMethodName = testMethodName,
				TestMethodParameterTypesVSTest = testMethodParameterTypes,
				TestMethodReturnTypeVSTest = testMethodReturnType,
				TestMethodUniqueID = testMethodUniqueID,
				Traits = traits ?? EmptyTraits,
			};
}
