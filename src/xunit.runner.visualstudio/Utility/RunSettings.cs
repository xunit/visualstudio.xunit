using System;
using System.Text.RegularExpressions;

namespace Xunit.Runner.VisualStudio;

public class RunSettings
{
	public AppDomainSupport? AppDomain { get; set; }
	public bool CollectSourceInformation { get; set; } = true;
	public bool? DiagnosticMessages { get; set; }
	public bool DisableSerialization { get; set; } = false;
	public bool? FailSkips { get; set; }
	public bool? InternalDiagnosticMessages { get; set; }
	public int? LongRunningTestSeconds { get; set; }
	public int? MaxParallelThreads { get; set; }
	public TestMethodDisplay? MethodDisplay { get; set; }
	public TestMethodDisplayOptions? MethodDisplayOptions { get; set; }
	public bool? NoAutoReporters { get; set; }
	public bool? ParallelizeAssembly { get; set; }
	public bool? ParallelizeTestCollections { get; set; }
	public bool? PreEnumerateTheories { get; set; }
	public string? ReporterSwitch { get; set; }
	public bool? ShadowCopy { get; set; }
	public bool? StopOnFail { get; set; }
	public string? TargetFrameworkVersion { get; set; }

	public void CopyTo(TestAssemblyConfiguration configuration)
	{
		if (AppDomain.HasValue)
			configuration.AppDomain = AppDomain;
		if (DiagnosticMessages.HasValue)
			configuration.DiagnosticMessages = DiagnosticMessages;
		if (FailSkips.HasValue)
			configuration.FailSkips = FailSkips;
		if (InternalDiagnosticMessages.HasValue)
			configuration.InternalDiagnosticMessages = InternalDiagnosticMessages;
		if (LongRunningTestSeconds.HasValue)
			configuration.LongRunningTestSeconds = LongRunningTestSeconds;
		if (MaxParallelThreads.HasValue)
			configuration.MaxParallelThreads = MaxParallelThreads;
		if (MethodDisplay.HasValue)
			configuration.MethodDisplay = MethodDisplay;
		if (MethodDisplayOptions.HasValue)
			configuration.MethodDisplayOptions = MethodDisplayOptions;
		if (ParallelizeAssembly.HasValue)
			configuration.ParallelizeAssembly = ParallelizeAssembly;
		if (ParallelizeTestCollections.HasValue)
			configuration.ParallelizeTestCollections = ParallelizeTestCollections;
		if (PreEnumerateTheories.HasValue)
			configuration.PreEnumerateTheories = PreEnumerateTheories;
		if (ShadowCopy.HasValue)
			configuration.ShadowCopy = ShadowCopy;
		if (StopOnFail.HasValue)
			configuration.StopOnFail = StopOnFail;
	}

	public static RunSettings Parse(string? settingsXml)
	{
		var result = new RunSettings();

		if (settingsXml is not null)
		{
			try
			{
				var runSettingsElement = System.Xml.Linq.XDocument.Parse(settingsXml)?.Element("RunSettings");

				if (runSettingsElement is not null)
				{
					// Custom settings for xUnit.net
					var xunitElement = runSettingsElement.Element("xUnit");
					if (xunitElement is not null)
					{
						var appDomainString = xunitElement.Element(Constants.Xunit.AppDomain)?.Value;
						if (Enum.TryParse<AppDomainSupport>(appDomainString, ignoreCase: true, out var appDomain))
							result.AppDomain = appDomain;

						var diagnosticMessagesString = xunitElement.Element(Constants.Xunit.DiagnosticMessages)?.Value;
						if (bool.TryParse(diagnosticMessagesString, out var diagnosticMessages))
							result.DiagnosticMessages = diagnosticMessages;

						var failSkipsString = xunitElement.Element(Constants.Xunit.FailSkips)?.Value;
						if (bool.TryParse(failSkipsString, out var failSkips))
							result.FailSkips = failSkips;

						var internalDiagnosticMessagesString = xunitElement.Element(Constants.Xunit.InternalDiagnosticMessages)?.Value;
						if (bool.TryParse(internalDiagnosticMessagesString, out var internalDiagnosticMessages))
							result.InternalDiagnosticMessages = internalDiagnosticMessages;

						var longRunningTestSecondsString = xunitElement.Element(Constants.Xunit.LongRunningTestSeconds)?.Value;
						if (int.TryParse(longRunningTestSecondsString, out var longRunningTestSeconds) && longRunningTestSeconds > 0)
							result.LongRunningTestSeconds = longRunningTestSeconds;

						var maxParallelThreadsString = xunitElement.Element(Constants.Xunit.MaxParallelThreads)?.Value;
						if (int.TryParse(maxParallelThreadsString, out var maxParallelThreads) && maxParallelThreads >= -1)
							result.MaxParallelThreads = maxParallelThreads;

						var methodDisplayString = xunitElement.Element(Constants.Xunit.MethodDisplay)?.Value;
						if (Enum.TryParse<TestMethodDisplay>(methodDisplayString, ignoreCase: true, out var methodDisplay))
							result.MethodDisplay = methodDisplay;

						var methodDisplayOptionsString = xunitElement.Element(Constants.Xunit.MethodDisplayOptions)?.Value;
						if (Enum.TryParse<TestMethodDisplayOptions>(methodDisplayOptionsString, ignoreCase: true, out var methodDisplayOptions))
							result.MethodDisplayOptions = methodDisplayOptions;

						var noAutoReportersString = xunitElement.Element(Constants.Xunit.NoAutoReporters)?.Value;
						if (bool.TryParse(noAutoReportersString, out var noAutoReporters))
							result.NoAutoReporters = noAutoReporters;

						var parallelizeAssemblyString = xunitElement.Element(Constants.Xunit.ParallelizeAssembly)?.Value;
						if (bool.TryParse(parallelizeAssemblyString, out var parallelizeAssembly))
							result.ParallelizeAssembly = parallelizeAssembly;

						var parallelizeTestCollectionsString = xunitElement.Element(Constants.Xunit.ParallelizeTestCollections)?.Value;
						if (bool.TryParse(parallelizeTestCollectionsString, out var parallelizeTestCollections))
							result.ParallelizeTestCollections = parallelizeTestCollections;

						var preEnumerateTheoriesString = xunitElement.Element(Constants.Xunit.PreEnumerateTheories)?.Value;
						if (bool.TryParse(preEnumerateTheoriesString, out var preEnumerateTheories))
							result.PreEnumerateTheories = preEnumerateTheories;

						var reporterSwitchString = xunitElement.Element(Constants.Xunit.ReporterSwitch)?.Value;
						if (reporterSwitchString is not null)
							result.ReporterSwitch = reporterSwitchString;

						var shadowCopyString = xunitElement.Element(Constants.Xunit.ShadowCopy)?.Value;
						if (bool.TryParse(shadowCopyString, out var shadowCopy))
							result.ShadowCopy = shadowCopy;

						var stopOnFailString = xunitElement.Element(Constants.Xunit.StopOnFail)?.Value;
						if (bool.TryParse(stopOnFailString, out var stopOnFail))
							result.StopOnFail = stopOnFail;
					}

					// Standard settings from VSTest, which can override the user's configured values
					var runConfigurationElement = runSettingsElement.Element("RunConfiguration");
					if (runConfigurationElement is not null)
					{
						var collectSourceInformationString = runConfigurationElement.Element(Constants.RunConfiguration.CollectSourceInformation)?.Value;
						if (bool.TryParse(collectSourceInformationString, out var collectSourceInformation))
							result.CollectSourceInformation = collectSourceInformation;

						var designModeString = runConfigurationElement.Element(Constants.RunConfiguration.DesignMode)?.Value;
						if (bool.TryParse(designModeString, out var designMode))
							// Design mode == running inside the IDE (where we need serialization)
							result.DisableSerialization = !designMode;

						var disableAppDomainString = runConfigurationElement.Element(Constants.RunConfiguration.DisableAppDomain)?.Value;
						if (bool.TryParse(disableAppDomainString, out var disableAppDomain))
							if (disableAppDomain)
								result.AppDomain = AppDomainSupport.Denied;

						var disableParallelizationString = runConfigurationElement.Element(Constants.RunConfiguration.DisableParallelization)?.Value;
						if (bool.TryParse(disableParallelizationString, out var disableParallelization))
							if (disableParallelization)
							{
								result.ParallelizeAssembly = false;
								result.ParallelizeTestCollections = false;
							}

						var targetFrameworkVersionString = runConfigurationElement.Element(Constants.RunConfiguration.TargetFrameworkVersion)?.Value;
						if (targetFrameworkVersionString is not null)
							result.TargetFrameworkVersion = targetFrameworkVersionString;

						// These values are holdovers that we inappropriately shoved into RunConfiguration. The documentation will
						// only reflect that these are "legal" values in the xUnit section.
						var internalDiagnosticsString = runConfigurationElement.Element(Constants.RunConfiguration.InternalDiagnostics)?.Value;
						if (bool.TryParse(internalDiagnosticsString, out var internalDiagnostics))
							result.InternalDiagnosticMessages = internalDiagnostics;

						var noAutoReportersString = runConfigurationElement.Element(Constants.RunConfiguration.NoAutoReporters)?.Value;
						if (bool.TryParse(noAutoReportersString, out var noAutoReporters))
							result.NoAutoReporters = noAutoReporters;

						var reporterSwitchString = runConfigurationElement.Element(Constants.RunConfiguration.ReporterSwitch)?.Value;
						if (reporterSwitchString is not null)
							result.ReporterSwitch = reporterSwitchString;
					}
				}
			}
			catch { }
		}

		return result;
	}

	static class Constants
	{
		// https://learn.microsoft.com/en-us/visualstudio/test/configure-unit-tests-by-using-a-dot-runsettings-file?view=vs-2022#runconfiguration-element
		public static class RunConfiguration
		{
			public const string CollectSourceInformation = "CollectSourceInformation";
			public const string DesignMode = "DesignMode";
			public const string DisableAppDomain = "DisableAppDomain";
			public const string DisableParallelization = "DisableParallelization";
			public const string InternalDiagnostics = "InternalDiagnostics";
			public const string NoAutoReporters = "NoAutoReporters";
			public const string ReporterSwitch = "ReporterSwitch";
			public const string TargetFrameworkVersion = "TargetFrameworkVersion";
		}

		public static class Xunit
		{
			public const string AppDomain = "AppDomain";
			public const string DiagnosticMessages = "DiagnosticMessages";
			public const string FailSkips = "FailSkips";
			public const string InternalDiagnosticMessages = "InternalDiagnosticMessages";
			public const string LongRunningTestSeconds = "LongRunningTestSeconds";
			public const string MaxParallelThreads = "MaxParallelThreads";
			public const string MethodDisplay = "MethodDisplay";
			public const string MethodDisplayOptions = "MethodDisplayOptions";
			public const string NoAutoReporters = "NoAutoReporters";
			public const string ParallelizeAssembly = "ParallelizeAssembly";
			public const string ParallelizeTestCollections = "ParallelizeTestCollections";
			public const string PreEnumerateTheories = "PreEnumerateTheories";
			public const string ReporterSwitch = "ReporterSwitch";
			public const string ShadowCopy = "ShadowCopy";
			public const string StopOnFail = "StopOnFail";
		}
	}

	public bool IsMatchingTargetFramework()
	{
		// FrameworkVersion parameter
		// https://github.com/Microsoft/vstest/blob/00f170990b8687d95a13719faec6417e4b1daef5/src/Microsoft.TestPlatform.ObjectModel/FrameworkVersion.cs
		// https://github.com/Microsoft/vstest/blob/b0fc6c9212813abdbfb31e2fe4162a7751c33ca2/src/Microsoft.TestPlatform.ObjectModel/RunSettings/RunConfiguration.cs#L315

		// Short circuit on null since we don't have anything to detect, return true
#if NETCOREAPP
		return string.IsNullOrWhiteSpace(TargetFrameworkVersion) || IsNetCore(TargetFrameworkVersion);
#else
		return string.IsNullOrWhiteSpace(TargetFrameworkVersion) || !IsNetCore(TargetFrameworkVersion);
#endif
	}

	// This should match .NET versions like 'net6.0' but not .NET Framework version like 'net462'.
	static readonly Regex regexNet5Plus = new(@"^net\d+\.\d+$");

	static bool IsNetCore(string targetFrameworkVersion) =>
		targetFrameworkVersion.StartsWith(".NETCoreApp,", StringComparison.OrdinalIgnoreCase) ||
		targetFrameworkVersion.StartsWith("FrameworkCore10", StringComparison.OrdinalIgnoreCase) ||
		regexNet5Plus.Match(targetFrameworkVersion).Success;
}
