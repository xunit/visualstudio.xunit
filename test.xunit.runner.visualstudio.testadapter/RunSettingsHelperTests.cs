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
