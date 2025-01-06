#if NETFRAMEWORK

using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Security.Permissions;
using Xunit.Internal;

namespace Xunit.Runner.VisualStudio;

class AppDomainManager
{
	readonly AppDomain appDomain;

	public AppDomainManager(string assemblyFileName)
	{
		Guard.ArgumentNotNullOrEmpty(assemblyFileName);

		assemblyFileName = Path.GetFullPath(assemblyFileName);
		Guard.FileExists(assemblyFileName);

		var applicationBase = Path.GetDirectoryName(assemblyFileName);
		var applicationName = Guid.NewGuid().ToString();
		var setup = new AppDomainSetup
		{
			ApplicationBase = applicationBase,
			ApplicationName = applicationName,
			ShadowCopyFiles = "true",
			ShadowCopyDirectories = applicationBase,
			CachePath = Path.Combine(Path.GetTempPath(), applicationName)
		};

		appDomain = AppDomain.CreateDomain(Path.GetFileNameWithoutExtension(assemblyFileName), AppDomain.CurrentDomain.Evidence, setup, new PermissionSet(PermissionState.Unrestricted));
	}

	public TObject? CreateObject<TObject>(
		AssemblyName assemblyName,
		string typeName,
		params object[] args)
			where TObject : class
	{
		try
		{
			return appDomain.CreateInstanceAndUnwrap(assemblyName.FullName, typeName, false, BindingFlags.Default, null, args, null, null) as TObject;
		}
		catch (TargetInvocationException ex)
		{
			ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
			return default;  // Will never reach here, but the compiler doesn't know that
		}
	}

	public virtual void Dispose()
	{
		if (appDomain is not null)
		{
			var cachePath = appDomain.SetupInformation.CachePath;

			try
			{
				AppDomain.Unload(appDomain);

				if (cachePath is not null)
					Directory.Delete(cachePath, true);
			}
			catch { }
		}
	}
}

#endif
