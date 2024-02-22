using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Testing.Extensions.VSTestBridge;
using Microsoft.Testing.Extensions.VSTestBridge.Requests;
using Microsoft.Testing.Platform.Capabilities.TestFramework;
using Microsoft.Testing.Platform.Messages;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;

namespace Xunit.Runner.VisualStudio;

internal sealed class XunitBridgedTestFramework : SynchronizedSingleSessionVSTestBridgedTestFramework
{
	public XunitBridgedTestFramework(XunitExtension extension, Func<IEnumerable<Assembly>> getTestAssemblies,
		IServiceProvider serviceProvider, ITestFrameworkCapabilities capabilities)
		: base(extension, getTestAssemblies, serviceProvider, capabilities)
	{
	}

	/// <inheritdoc />
	protected override Task SynchronizedDiscoverTestsAsync(VSTestDiscoverTestExecutionRequest request, IMessageBus messageBus,
		CancellationToken cancellationToken)
	{
		var discoverer = new VsTestRunner();
		using (cancellationToken.Register(discoverer.Cancel))
		{
			((ITestDiscoverer)discoverer).DiscoverTests(request.AssemblyPaths, request.DiscoveryContext, request.MessageLogger, request.DiscoverySink);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	protected override Task SynchronizedRunTestsAsync(VSTestRunTestExecutionRequest request, IMessageBus messageBus,
		CancellationToken cancellationToken)
	{
		var runner = new VsTestRunner();
		using (cancellationToken.Register(runner.Cancel))
		{
			var executor = (ITestExecutor)runner;
			if (request.VSTestFilter.TestCases is { } testCases)
			{
				executor.RunTests(testCases, request.RunContext, request.FrameworkHandle);
			}
			else
			{
				executor.RunTests(request.AssemblyPaths, request.RunContext, request.FrameworkHandle);
			}
		}

		return Task.CompletedTask;
	}
}
