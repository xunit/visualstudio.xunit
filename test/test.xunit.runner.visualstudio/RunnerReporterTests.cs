using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NSubstitute;
using Xunit;
using Xunit.Runner.Reporters;
using Xunit.Runner.VisualStudio;

public class RunnerReporterTests
{
	[Fact]
	public void WhenNotUsingAutoReporters_ChoosesDefault()
	{
		using var _ = EnvironmentHelper.NullifyEnvironmentalReporters();
		var settings = new RunSettings { NoAutoReporters = true };

		var runnerReporter = VsTestRunner.GetRunnerReporter(null, settings, Array.Empty<string>());

		Assert.Equal(typeof(DefaultRunnerReporterWithTypes).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
	}

	[Fact]
	public void WhenUsingAutoReporters_DoesNotChooseDefault()
	{
		using var _ = EnvironmentHelper.NullifyEnvironmentalReporters();
		Environment.SetEnvironmentVariable("TEAMCITY_PROJECT_NAME", "foo");  // Force TeamCityReporter to surface environmentally
		var settings = new RunSettings { NoAutoReporters = false };

		var runnerReporter = VsTestRunner.GetRunnerReporter(null, settings, Array.Empty<string>());

		Assert.Equal(typeof(TeamCityReporter).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
	}

	[Fact]
	public void WhenUsingReporterSwitch_PicksThatReporter()
	{
		using var _ = EnvironmentHelper.NullifyEnvironmentalReporters();
		var settings = new RunSettings { NoAutoReporters = true, ReporterSwitch = "json" };

		var runnerReporter = VsTestRunner.GetRunnerReporter(null, settings, Array.Empty<string>());

		Assert.Equal(typeof(JsonReporter).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
	}

	[Fact]
	public void WhenRequestedReporterDoesntExist_LogsAndFallsBack()
	{
		using var _ = EnvironmentHelper.NullifyEnvironmentalReporters();
		var settings = new RunSettings { NoAutoReporters = true, ReporterSwitch = "thisnotavalidreporter" };
		var logger = Substitute.For<IMessageLogger>();
		var loggerHelper = new LoggerHelper(logger, new Stopwatch());

		var runnerReporter = VsTestRunner.GetRunnerReporter(loggerHelper, settings, Array.Empty<string>());

		Assert.Equal(typeof(DefaultRunnerReporterWithTypes).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
		logger.Received(1).SendMessage(TestMessageLevel.Warning, "[xUnit.net 00:00:00.00] Could not find requested reporter 'thisnotavalidreporter'");
	}
}
