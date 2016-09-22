using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#if !PLATFORM_DOTNET
    using System.Xml;
#endif

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    internal static class RunSettingsHelper
    {
        public static bool DisableAppDomain { get; set; }

        public static bool DisableParallelization { get; set; }

        /// <summary>
        /// Reads settings for the current run from run context
        /// </summary>
        /// <param name="runContext">RunContext of the run</param>
        public static void ReadRunSettings(IRunContext runContext, LoggerHelper logger)
        {
            var settingsXml = runContext?.RunSettings?.SettingsXml;

#if !PLATFORM_DOTNET
            if (!string.IsNullOrEmpty(settingsXml))
            {
                try
                {
                    using (var stringReader = new StringReader(settingsXml))
                    {
                        using (var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true }))
                        {
                            if (ReadToRunConfigurationElement(xmlReader))
                            {
                                // Process the fields in Xml elements
                                xmlReader.Read();
                                var numSettingsRead = 0;
                                while (xmlReader.NodeType == XmlNodeType.Element && numSettingsRead < 2)
                                {
                                    string elementName = xmlReader.Name;
                                    switch (elementName)
                                    {
                                        case "DisableAppDomain":
                                            DisableAppDomain = ReadBooleanValue(xmlReader);
                                            numSettingsRead++;
                                            break;

                                        case "DisableParallelization":
                                            DisableParallelization = ReadBooleanValue(xmlReader);
                                            numSettingsRead++;
                                            break;

                                        default: xmlReader.Read(); break;
                                    }
                                }

                                xmlReader.ReadEndElement();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError("Error reading RunSettingsXml: {0}, Exception: {1}", settingsXml, ex);
                }
            }
#endif
        }

#if !PLATFORM_DOTNET
        static bool ReadToRunConfigurationElement(XmlReader xmlReader)
        {
            var success = false;
            // Read to the root node.
            ReadToNextElement(xmlReader);

            // Verify that it is a "RunSettings" node.
            if (string.Equals(xmlReader.Name, "RunSettings"))
            {
                // Read to the next element.
                ReadToNextElement(xmlReader);

                // Read till we reach RunConfiguration element or reach EOF
                while (!string.Equals(xmlReader.Name, "RunConfiguration", StringComparison.OrdinalIgnoreCase)
                        &&
                        !xmlReader.EOF)
                {
                    xmlReader.Skip();
                    if (xmlReader.NodeType != XmlNodeType.Element)
                    {
                        // Read to the next element.
                        ReadToNextElement(xmlReader);
                    }
                }

                success = !xmlReader.EOF && !xmlReader.IsEmptyElement;
            }
            return success;
        }

        static void ReadToNextElement(XmlReader xmlReader)
        {
            // read to next element
            while (!xmlReader.EOF && xmlReader.Read() && xmlReader.NodeType != XmlNodeType.Element)
            {
            }
        }

        static bool ReadBooleanValue(XmlReader xmlReader)
        {
            bool value;
            if (bool.TryParse(xmlReader.ReadElementContentAsString(), out value))
            {
                return value;
            }
            return false;
        }
#endif
    }
}
