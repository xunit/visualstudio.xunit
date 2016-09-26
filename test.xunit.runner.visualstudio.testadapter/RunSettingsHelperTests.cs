using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Runner.VisualStudio.TestAdapter;

namespace test.xunit.runner.visualstudio.testadapter
{
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
            Assert.Equal(false, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(false, RunSettingsHelper.DisableParallelization);
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
                     </RunConfiguration>
                </RunSettings>";

            RunSettingsHelper.ReadRunSettings(settingsXml);

            // Default values must be used
            Assert.Equal(false, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(false, RunSettingsHelper.DisableParallelization);
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
            Assert.Equal(true, RunSettingsHelper.DisableAppDomain);
            // Default value must be used for disableparallelization
            Assert.Equal(false, RunSettingsHelper.DisableParallelization);
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

            // Default value must be used for DisableAppDomain
            Assert.Equal(false, RunSettingsHelper.DisableAppDomain);
            // DisableParallelization can be set 
            Assert.Equal(true, RunSettingsHelper.DisableParallelization);
        }

        [Fact]
        public void RunSettingsHelperShouldReadValuesCorrectly()
        {
            string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                     <RunConfiguration>
                       <DisableAppDomain>true</DisableAppDomain>
                       <DisableParallelization>true</DisableParallelization>
                     </RunConfiguration>
                </RunSettings>";

            RunSettingsHelper.ReadRunSettings(settingsXml);

            // Correct values must be sets
            Assert.Equal(true, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(true, RunSettingsHelper.DisableParallelization);

            settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                     <RunConfiguration>
                       <DisableAppDomain>false</DisableAppDomain>
                       <DisableParallelization>true</DisableParallelization>
                     </RunConfiguration>
                </RunSettings>";

            RunSettingsHelper.ReadRunSettings(settingsXml);

            // Values must be the latest ones 
            Assert.Equal(false, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(true, RunSettingsHelper.DisableParallelization);

            settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                     <RunConfiguration>
                       <DisableAppDomain>true</DisableAppDomain>
                       <DisableParallelization>false</DisableParallelization>
                     </RunConfiguration>
                </RunSettings>";

            RunSettingsHelper.ReadRunSettings(settingsXml);

            // Values must be the latest ones 
            Assert.Equal(true, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(false, RunSettingsHelper.DisableParallelization);


            settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                     <RunConfiguration>
                       <DisableAppDomain>false</DisableAppDomain>
                       <DisableParallelization>false</DisableParallelization>
                     </RunConfiguration>
                </RunSettings>";

            RunSettingsHelper.ReadRunSettings(settingsXml);

            // Values must be the latest ones 
            Assert.Equal(false, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(false, RunSettingsHelper.DisableParallelization);
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
            Assert.Equal(true, RunSettingsHelper.DisableAppDomain);
            Assert.Equal(true, RunSettingsHelper.DisableParallelization);
        }
    }
}
