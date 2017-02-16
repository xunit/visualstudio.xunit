using Xunit;
using Xunit.Runner.VisualStudio.TestAdapter;

public class RunSettingsHelperTests
{
    [Fact]
    public void RunSettingsHelperShouldNotThrowExceptionOnBadXml()
    {
        string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
            <RunSettings";

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Default values must be used
        Assert.False(RunSettingsHelper.DisableAppDomain);
        Assert.False(RunSettingsHelper.DisableParallelization);
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
                    </RunConfiguration>
                </RunSettings>";

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Default values must be used
        Assert.False(RunSettingsHelper.DisableAppDomain);
        Assert.False(RunSettingsHelper.DisableParallelization);
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

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Attribute must be ignored
        Assert.True(RunSettingsHelper.DisableAppDomain);
        // Default value must be used for disableparallelization, designmode
        Assert.False(RunSettingsHelper.DisableParallelization);
        Assert.True(RunSettingsHelper.DesignMode);
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

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Default value must be used for DisableAppDomain, DesignMode
        Assert.False(RunSettingsHelper.DisableAppDomain);
        Assert.True(RunSettingsHelper.DesignMode);
        // DisableParallelization can be set
        Assert.True(RunSettingsHelper.DisableParallelization);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RunSettingsHelperShouldReadValuesCorrectly(bool disableAppDomain, bool disableParallelization)
    {
        string settingsXml =
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                    <RunConfiguration>
                        <DisableAppDomain>{disableAppDomain.ToString().ToLowerInvariant()}</DisableAppDomain>
                        <DisableParallelization>{disableParallelization.ToString().ToLowerInvariant()}</DisableParallelization>
                    </RunConfiguration>
                </RunSettings>";

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Correct values must be set
        Assert.Equal(disableAppDomain, RunSettingsHelper.DisableAppDomain);
        Assert.Equal(disableParallelization, RunSettingsHelper.DisableParallelization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunSettingsHelperShouldReadDesignModeSettingCorrectly(bool designMode)
    {
        string settingsXml =
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                    <RunConfiguration>
                        <DisableAppDomain>true</DisableAppDomain>
                        <DisableParallelization>invalid</DisableParallelization>
                        <DesignMode>{designMode.ToString().ToLowerInvariant()}</DesignMode>
                    </RunConfiguration>
                </RunSettings>";

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Correct values must be set
        Assert.Equal(designMode, RunSettingsHelper.DesignMode);
        Assert.True(RunSettingsHelper.DisableAppDomain);
        // Default value should be set for DisableParallelization
        Assert.False(RunSettingsHelper.DisableParallelization);
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
                </RunSettings>";

        RunSettingsHelper.ReadRunSettings(settingsXml);

        // Correct values must be used
        Assert.True(RunSettingsHelper.DisableAppDomain);
        Assert.True(RunSettingsHelper.DisableParallelization);
    }
}
