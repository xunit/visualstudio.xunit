using Xunit;
using Xunit.Runner.VisualStudio;

public class RunSettingsTests
{
	void AssertDefaultValues(RunSettings runSettings)
	{
		Assert.Null(runSettings.AppDomain);
		Assert.True(runSettings.CollectSourceInformation);
		Assert.Null(runSettings.DiagnosticMessages);
		Assert.False(runSettings.DisableSerialization);
		Assert.Null(runSettings.FailSkips);
		Assert.Null(runSettings.InternalDiagnosticMessages);
		Assert.Null(runSettings.LongRunningTestSeconds);
		Assert.Null(runSettings.MaxParallelThreads);
		Assert.Null(runSettings.MethodDisplay);
		Assert.Null(runSettings.MethodDisplayOptions);
		Assert.Null(runSettings.NoAutoReporters);
		Assert.Null(runSettings.ParallelizeAssembly);
		Assert.Null(runSettings.ParallelizeTestCollections);
		Assert.Null(runSettings.PreEnumerateTheories);
		Assert.Null(runSettings.ReporterSwitch);
		Assert.Null(runSettings.ShadowCopy);
		Assert.Null(runSettings.StopOnFail);
		Assert.Null(runSettings.TargetFrameworkVersion);
	}

	[Fact]
	public void RunSettingsHelperShouldNotThrowExceptionOnBadXml()
	{
		string settingsXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings";

		var runSettings = RunSettings.Parse(settingsXml);

		AssertDefaultValues(runSettings);
	}

	[Fact]
	public void RunSettingsHelperShouldNotThrowExceptionOnInvalidValuesForElements()
	{
		string settingsXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		<DisableAppDomain>1234</DisableAppDomain>
		<DisableParallelization>smfhekhgekr</DisableParallelization>
		<DesignMode>3245sax</DesignMode>
		<CollectSourceInformation>1234blah</CollectSourceInformation>
		<NoAutoReporters>1x3_5f8g0</NoAutoReporters>
	</RunConfiguration>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		AssertDefaultValues(runSettings);
	}

	[Fact]
	public void RunSettingsHelperShouldUseDefaultValuesInCaseOfIncorrectSchemaAndIgnoreAttributes()
	{
		string settingsXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		<OuterElement>
			<DisableParallelization>true</DisableParallelization>
		</OuterElement>
		<DisableAppDomain value=""false"">true</DisableAppDomain>
	</RunConfiguration>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		// Use element value, not attribute value
		Assert.Equal(AppDomainSupport.Denied, runSettings.AppDomain);
		// Ignore value that isn't at the right level
		Assert.Null(runSettings.ParallelizeAssembly);
		Assert.Null(runSettings.ParallelizeTestCollections);
	}

	[Fact]
	public void RunSettingsHelperShouldUseDefaultValuesInCaseOfBadXml()
	{
		string settingsXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		Random Text
		<DisableParallelization>true</DisableParallelization>
	</RunConfiguration>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		// Allow value to be read even after unexpected element body
		Assert.False(runSettings.ParallelizeAssembly);
		Assert.False(runSettings.ParallelizeTestCollections);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RunSettingsHelperShouldReadBooleanValuesCorrectly(bool testValue)
	{
		var testValueString = testValue.ToString().ToLowerInvariant();
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		<CollectSourceInformation>{testValueString}</CollectSourceInformation>
		<DesignMode>{testValueString}</DesignMode>
	</RunConfiguration>
	<xUnit>
		<DiagnosticMessages>{testValueString}</DiagnosticMessages>
		<FailSkips>{testValueString}</FailSkips>
		<InternalDiagnosticMessages>{testValueString}</InternalDiagnosticMessages>
		<NoAutoReporters>{testValueString}</NoAutoReporters>
		<ParallelizeAssembly>{testValueString}</ParallelizeAssembly>
		<ParallelizeTestCollections>{testValueString}</ParallelizeTestCollections>
		<PreEnumerateTheories>{testValueString}</PreEnumerateTheories>
		<ShadowCopy>{testValueString}</ShadowCopy>
		<StopOnFail>{testValueString}</StopOnFail>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(testValue, runSettings.CollectSourceInformation);
		Assert.Equal(testValue, runSettings.DiagnosticMessages);
		Assert.Equal(!testValue, runSettings.DisableSerialization);  // DisableSerialization is the inversion of DesignMode
		Assert.Equal(testValue, runSettings.DiagnosticMessages);
		Assert.Equal(testValue, runSettings.FailSkips);
		Assert.Equal(testValue, runSettings.InternalDiagnosticMessages);
		Assert.Equal(testValue, runSettings.NoAutoReporters);
		Assert.Equal(testValue, runSettings.ParallelizeAssembly);
		Assert.Equal(testValue, runSettings.ParallelizeTestCollections);
		Assert.Equal(testValue, runSettings.PreEnumerateTheories);
		Assert.Equal(testValue, runSettings.ShadowCopy);
		Assert.Equal(testValue, runSettings.StopOnFail);
	}

	[Theory]
	[InlineData("nonsense", null)]
	[InlineData("denied", AppDomainSupport.Denied)]
	[InlineData("ifAvailable", AppDomainSupport.IfAvailable)]
#if NETFRAMEWORK
	[InlineData("required", AppDomainSupport.Required)]
#endif
	public void AppDomain(string value, AppDomainSupport? expected)
	{
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<xUnit>
		<AppDomain>{value}</AppDomain>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(expected, runSettings.AppDomain);
	}

	[Theory]
	[InlineData(0, null)]
	[InlineData(1, 1)]
	[InlineData(42, 42)]
	public void LongRunningTestSeconds(int value, int? expected)
	{
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<xUnit>
		<LongRunningTestSeconds>{value}</LongRunningTestSeconds>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(expected, runSettings.LongRunningTestSeconds);
	}

	[Theory]
	[InlineData(-2, null)]
	[InlineData(-1, -1)]
	[InlineData(42, 42)]
	public void MaxParallelThreads(int value, int? expected)
	{
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<xUnit>
		<MaxParallelThreads>{value}</MaxParallelThreads>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(expected, runSettings.MaxParallelThreads);
	}

	[Theory]
	[InlineData("nonsense", null)]
	[InlineData("method", TestMethodDisplay.Method)]
	[InlineData("classAndMethod", TestMethodDisplay.ClassAndMethod)]
	public void MethodDisplay(string value, TestMethodDisplay? expected)
	{
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<xUnit>
		<MethodDisplay>{value}</MethodDisplay>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(expected, runSettings.MethodDisplay);
	}

	[Theory]
	[InlineData("nonsense", null)]
	[InlineData("all", TestMethodDisplayOptions.All)]
	[InlineData("replacePeriodWithComma", TestMethodDisplayOptions.ReplacePeriodWithComma)]
	[InlineData("replaceUnderscoreWithSpace", TestMethodDisplayOptions.ReplaceUnderscoreWithSpace)]
	[InlineData("useOperatorMonikers", TestMethodDisplayOptions.UseOperatorMonikers)]
	[InlineData("useEscapeSequences", TestMethodDisplayOptions.UseEscapeSequences)]
	[InlineData("replaceUnderscoreWithSpace, useOperatorMonikers", TestMethodDisplayOptions.ReplaceUnderscoreWithSpace | TestMethodDisplayOptions.UseOperatorMonikers)]
	[InlineData("none", TestMethodDisplayOptions.None)]
	public void MethodDisplayOptions(string value, TestMethodDisplayOptions? expected)
	{
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<xUnit>
		<MethodDisplayOptions>{value}</MethodDisplayOptions>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(expected, runSettings.MethodDisplayOptions);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SpecialBooleanValuesFromVSTest(bool testValue)
	{
		var testValueString = testValue.ToString().ToLowerInvariant();
		string settingsXml =
$@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		<DisableAppDomain>{testValueString}</DisableAppDomain>
		<DisableParallelization>{testValueString}</DisableParallelization>
	</RunConfiguration>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		// When true, these set values...
		if (testValue)
		{
			Assert.Equal(AppDomainSupport.Denied, runSettings.AppDomain);
			Assert.False(runSettings.ParallelizeAssembly);
			Assert.False(runSettings.ParallelizeTestCollections);
		}
		// ...otherwise, they remain unchanged
		else
		{
			Assert.Null(runSettings.AppDomain);
			Assert.Null(runSettings.ParallelizeAssembly);
			Assert.Null(runSettings.ParallelizeAssembly);
		}
	}

	[Fact]
	public void RunSettingsHelperShouldIgnoreEvenIfAdditionalElementsExist()
	{
		string settingsXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		<TargetPlatform>x64</TargetPlatform>
		<TargetFrameworkVersion>FrameworkCore10</TargetFrameworkVersion>
		<SolutionDirectory>%temp%</SolutionDirectory>
		<DisableAppDomain>true</DisableAppDomain>
		<DisableParallelization>true</DisableParallelization>
		<MaxCpuCount>4</MaxCpuCount>
	</RunConfiguration>
	<xUnit>
		<UnknownElement>sassy</UnknownElement>
		<NoAutoReporters>true</NoAutoReporters>
		<ReporterSwitch>foo</ReporterSwitch>
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

		Assert.Equal(AppDomainSupport.Denied, runSettings.AppDomain);
		Assert.False(runSettings.ParallelizeAssembly);
		Assert.False(runSettings.ParallelizeTestCollections);
		Assert.True(runSettings.NoAutoReporters);
		Assert.Equal("FrameworkCore10", runSettings.TargetFrameworkVersion);
		Assert.Equal("foo", runSettings.ReporterSwitch);
	}

	[Fact]
	public void RunConfigurationOptionsOverrideXunitOptions()
	{
		string settingsXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<RunSettings>
	<RunConfiguration>
		<DisableAppDomain>true</DisableAppDomain>
		<DisableParallelization>true</DisableParallelization>
	</RunConfiguration>
	<xUnit>
		<
	</xUnit>
</RunSettings>";

		var runSettings = RunSettings.Parse(settingsXml);

	}
}
