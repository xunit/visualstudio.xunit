#pragma warning disable CA1513 // ObjectDisposedException.ThrowIf is not available in net472

using System;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Navigation;
using Xunit.Internal;
using Xunit.Runner.Common;

namespace Xunit.Runner.VisualStudio;

// This class wraps DiaSession, and uses DiaSessionWrapperHelper to discover when a test is an async test
// (since that requires special handling by DIA). The wrapper helper needs to exist in a separate AppDomain
// so that we can do discovery without locking the assembly under test (for .NET Framework).
class DiaSessionWrapper : IDisposable
{
#if NETFRAMEWORK
	readonly AppDomainManager? appDomainManager;
#endif
	readonly DiagnosticMessageSink diagnosticMessageSink;
	readonly object disposalLock = new();
	bool disposed;
	readonly DiaSessionWrapperHelper? helper;
	readonly DiaSession? session;

	public DiaSessionWrapper(
		string assemblyFileName,
		DiagnosticMessageSink diagnosticMessageSink)
	{
		this.diagnosticMessageSink = Guard.ArgumentNotNull(diagnosticMessageSink);

		try
		{
			session = new DiaSession(assemblyFileName);
		}
		catch (Exception ex)
		{
			diagnosticMessageSink.OnMessage(new InternalDiagnosticMessage($"Exception creating DiaSession: {ex}"));
		}

		try
		{
#if NETFRAMEWORK
			var adapterFileName = typeof(DiaSessionWrapperHelper).Assembly.GetLocalCodeBase();
			if (adapterFileName is not null)
			{
				appDomainManager = new AppDomainManager(assemblyFileName);
				helper = appDomainManager.CreateObject<DiaSessionWrapperHelper>(typeof(DiaSessionWrapperHelper).Assembly.GetName(), typeof(DiaSessionWrapperHelper).FullName!, adapterFileName);
			}
#else
			helper = new DiaSessionWrapperHelper(assemblyFileName);
#endif
		}
		catch (Exception ex)
		{
			diagnosticMessageSink.OnMessage(new DiagnosticMessage($"Exception creating DiaSessionWrapperHelper: {ex}"));
		}
	}

	public INavigationData? GetNavigationData(
		string typeName,
		string methodName)
	{
		if (session is null || helper is null)
			return null;

		try
		{
			helper.Normalize(ref typeName, ref methodName);
			return session.GetNavigationDataForMethod(typeName, methodName);
		}
		catch (Exception ex)
		{
			diagnosticMessageSink.OnMessage(new DiagnosticMessage($"Exception getting source mapping for {typeName}.{methodName}: {ex}"));
			return null;
		}
	}

	public void Dispose()
	{
		lock (disposalLock)
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(DiaSessionWrapper));

			disposed = true;
		}

		session?.Dispose();
#if NETFRAMEWORK
		appDomainManager?.Dispose();
#endif
	}
}
