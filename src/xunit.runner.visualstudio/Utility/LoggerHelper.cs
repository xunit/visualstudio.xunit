using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

public class LoggerHelper
{
	public LoggerHelper(
		IMessageLogger? logger,
		Stopwatch stopwatch)
	{
		InnerLogger = logger;
		Stopwatch = stopwatch;
	}

	public IMessageLogger? InnerLogger { get; private set; }

	public Stopwatch Stopwatch { get; private set; }

	public void Log(
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Informational, null, string.Format(format, args));
	}

	public void Log(
		ITestCase testCase,
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Informational, testCase.TestMethod.TestClass.TestCollection.TestAssembly.Assembly.AssemblyPath, string.Format(format, args));
	}

	public void LogWithSource(
		string source,
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Informational, source, string.Format(format, args));
	}

	public void LogError(
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Error, null, string.Format(format, args));
	}

	public void LogError(
		ITestCase testCase,
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Error, testCase.TestMethod.TestClass.TestCollection.TestAssembly.Assembly.AssemblyPath, string.Format(format, args));
	}

	public void LogErrorWithSource(
		string source,
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Error, source, string.Format(format, args));
	}

	public void LogWarning(
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Warning, null, string.Format(format, args));
	}

	public void LogWarning(
		ITestCase testCase,
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Warning, testCase.TestMethod.TestClass.TestCollection.TestAssembly.Assembly.AssemblyPath, string.Format(format, args));
	}

	public void LogWarningWithSource(
		string source,
		string format,
		params object?[] args)
	{
		if (InnerLogger is not null)
			SendMessage(InnerLogger, TestMessageLevel.Warning, source, string.Format(format, args));
	}

	void SendMessage(
		IMessageLogger logger,
		TestMessageLevel level,
		string? assemblyName,
		string message)
	{
		var assemblyText = assemblyName is null ? "" : $"{Path.GetFileNameWithoutExtension(assemblyName)}: ";
		logger.SendMessage(level, $"[xUnit.net {Stopwatch.Elapsed:hh\\:mm\\:ss\\.ff}] {assemblyText}{message}");
	}
}
