using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#if !PLATFORM_DOTNET
using System.Xml.Linq;
#endif

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public static class RunSettingsHelper
    {
        public static bool DisableAppDomain { get; set; }

        public static bool DisableParallelization { get; set; }

        /// <summary>
        /// Reads settings for the current run from run settings xml
        /// </summary>
        /// <param name="runSettingsXml">RunSettingsXml of the run</param>
        public static void ReadRunSettings(string runSettingsXml)
        {
#if !PLATFORM_DOTNET
            if (!string.IsNullOrEmpty(runSettingsXml))
            {
                try
                {
                    var element = XDocument.Parse(runSettingsXml)?.Element("RunSettings")?.Element("RunConfiguration");
                    if (element != null)
                    {
                        var disableAppDomainString = element.Element("DisableAppDomain")?.Value;
                        bool disableAppDomain;
                        if (!string.IsNullOrEmpty(disableAppDomainString) && bool.TryParse(disableAppDomainString, out disableAppDomain))
                        {
                            DisableAppDomain = disableAppDomain;
                        }

                        var disableParallelizationString = element.Element("DisableParallelization")?.Value;
                        bool disableParallelization;
                        if(!string.IsNullOrEmpty(disableParallelizationString) && bool.TryParse(disableParallelizationString, out disableParallelization))
                        {
                            DisableParallelization = disableParallelization;
                        }
                    }
                }
                catch (Exception)
                {
                    // ignore
                }
            }
#endif
        }
    }
}
