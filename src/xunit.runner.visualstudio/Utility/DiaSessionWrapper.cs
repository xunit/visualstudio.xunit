using System;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Navigation;

namespace Xunit.Runner.VisualStudio.Utility;

// This class wraps DiaSession, and uses DiaSessionWrapperHelper to discover when a test is an async test
// (since that requires special handling by DIA). The wrapper helper needs to exist in a separate AppDomain
// so that we can do discovery without locking the assembly under test (for .NET Framework).
class DiaSessionWrapper : IDisposable
{
#if NETFRAMEWORK
	readonly AppDomainManager? appDomainManager;
#endif
	readonly DiaSessionWrapperHelper? helper;
	readonly DiaSession session;

	public DiaSessionWrapper(string assemblyFileName)
	{
		session = new DiaSession(assemblyFileName);

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

	public INavigationData? GetNavigationData(
		string typeName,
		string methodName)
	{
		if (helper is null)
			return null;

		helper.Normalize(ref typeName, ref methodName);
		return session.GetNavigationDataForMethod(typeName, methodName);
	}

	public void Dispose()
	{
		session.Dispose();
#if NETFRAMEWORK
		appDomainManager?.Dispose();
#endif
	}
}
