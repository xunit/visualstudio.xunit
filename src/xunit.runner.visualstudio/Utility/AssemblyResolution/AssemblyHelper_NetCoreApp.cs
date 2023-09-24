#if NETCOREAPP

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Internal.Microsoft.Extensions.DependencyModel;
using Xunit.Abstractions;

namespace Xunit;

/// <summary>
/// This class provides assistance with assembly resolution for missing assemblies.
/// </summary>
class AssemblyHelper : AssemblyLoadContext, IDisposable
{
	readonly DependencyContextAssemblyCache? assemblyCache;

	/// <summary>
	/// Initializes a new instance of the <see cref="NetCoreAssemblyDependencyResolver"/> class.
	/// </summary>
	/// <param name="assemblyFileName">The path to the assembly</param>
	/// <param name="internalDiagnosticsMessageSink">An optional message sink for use with internal diagnostics messages;
	/// may pass <c>null</c> for no internal diagnostics messages</param>
	public AssemblyHelper(
		string assemblyFileName,
		IMessageSink? internalDiagnosticsMessageSink)
	{
		if (!File.Exists(assemblyFileName))
		{
			internalDiagnosticsMessageSink?.OnMessage(new _DiagnosticMessage($"[AssemblyHelper_NetCoreApp..ctor] Assembly file not found: '{assemblyFileName}'"));
			return;
		}

		var assembly = LoadFromAssemblyPath(assemblyFileName);
		if (assembly is null)
		{
			internalDiagnosticsMessageSink?.OnMessage(new _DiagnosticMessage($"[AssemblyHelper_NetCoreApp..ctor] Assembly file could not be loaded: '{assemblyFileName}'"));
			return;
		}

		var dependencyContext = DependencyContext.Load(assembly);
		if (dependencyContext is null)
		{
			internalDiagnosticsMessageSink?.OnMessage(new _DiagnosticMessage($"[AssemblyHelper_NetCoreApp..ctor] Assembly file does not contain dependency manifest: '{assemblyFileName}'"));
			return;
		}

		var assemblyFolder = Path.GetDirectoryName(assemblyFileName);
		if (assemblyFolder is null)
		{
			internalDiagnosticsMessageSink?.OnMessage(new _DiagnosticMessage($"[AssemblyHelper_NetCoreApp..ctor] Assembly file does not have an associated folder to watch: '{assemblyFileName}'"));
			return;
		}

		assemblyCache = new DependencyContextAssemblyCache(assemblyFolder, dependencyContext, internalDiagnosticsMessageSink);

		Default.Resolving += OnResolving;
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		if (assemblyCache is not null)
			Default.Resolving -= OnResolving;
	}

	/// <inheritdoc/>
	protected override Assembly Load(AssemblyName assemblyName) =>
		Default.LoadFromAssemblyName(assemblyName);

	/// <inheritdoc/>
	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		var result = default(IntPtr);

		if (assemblyCache is not null)
			result = assemblyCache.LoadUnmanagedLibrary(unmanagedDllName, LoadUnmanagedDllFromPath);

		if (result == default)
			result = base.LoadUnmanagedDll(unmanagedDllName);

		return result;
	}

	Assembly? OnResolving(
		AssemblyLoadContext context,
		AssemblyName name)
	{
		if (assemblyCache is null || name.Name is null)
			return null;

		return assemblyCache.LoadManagedDll(name.Name, LoadFromAssemblyPath);
	}

	/// <summary>
	/// Subscribes to the appropriate assembly resolution event, to provide automatic assembly resolution for
	/// an assembly and any of its dependencies. Depending on the target platform, this may include the use
	/// of the .deps.json file generated during the build process.
	/// </summary>
	/// <returns>An object which, when disposed, un-subscribes.</returns>
	public static IDisposable SubscribeResolveForAssembly(
		string assemblyFileName,
		IMessageSink? internalDiagnosticsMessageSink = null) =>
			new AssemblyHelper(assemblyFileName, internalDiagnosticsMessageSink);

	/// <summary>
	/// Subscribes to the appropriate assembly resolution event, to provide automatic assembly resolution for
	/// an assembly and any of its dependencies. Depending on the target platform, this may include the use
	/// of the .deps.json file generated during the build process.
	/// </summary>
	/// <returns>An object which, when disposed, un-subscribes.</returns>
	public static IDisposable SubscribeResolveForAssembly(
		Type typeInAssembly,
		IMessageSink? internalDiagnosticsMessageSink = null) =>
			new AssemblyHelper(typeInAssembly.GetTypeInfo().Assembly.Location, internalDiagnosticsMessageSink);
}

#endif
