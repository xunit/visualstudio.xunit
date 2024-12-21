using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace Xunit.Runner.VisualStudio;

internal class LoggerHelper(IMessageLogger? logger, Stopwatch stopwatch)
{
	public IMessageLogger? InnerLogger { get; private set; } = logger;

	public Stopwatch Stopwatch { get; private set; } = stopwatch;

	public void Log(
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, TestMessageLevel.Informational, null, format, [first, .. args]);

	public void LogWithSource(
		string? source,
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, TestMessageLevel.Informational, source, format, [first, .. args]);

	public void LogError(
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, TestMessageLevel.Error, null, format, [first, .. args]);

	public void LogErrorWithSource(
		string? source,
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, TestMessageLevel.Error, source, format, [first, .. args]);

	public void LogWarning(
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, TestMessageLevel.Warning, null, format, [first, .. args]);

	public void LogWarningWithSource(
		string? source,
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, TestMessageLevel.Warning, source, format, [first, .. args]);

	public void SendMessage(
		TestMessageLevel level,
		string? assemblyName,
		string format,
		object? first,
		params object?[] args) =>
			SendMessage(InnerLogger, level, assemblyName, format, [first, .. args]);

	void SendMessage(
		IMessageLogger? logger,
		TestMessageLevel level,
		string? assemblyName,
		string format,
		object?[] args)
	{
		if (logger is null)
			return;

		try
		{
			var assemblyText = assemblyName is null ? "" : $"{Path.GetFileNameWithoutExtension(assemblyName)}: ";
			logger.SendMessage(level, $"[xUnit.net {Stopwatch.Elapsed:hh\\:mm\\:ss\\.ff}] {assemblyText}{string.Format(CultureInfo.CurrentCulture, format, args)}");
		}
		catch (Exception ex)
		{
			logger.SendMessage(TestMessageLevel.Warning, $"Exception formatting {level} message '{format}': {ex}");
		}
	}
}
