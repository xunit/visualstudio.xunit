using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.Sdk;

namespace Xunit.Runner.VisualStudio.Utility;

class DiaSessionWrapperHelper : LongLivedMarshalByRefObject
{
	readonly Assembly? assembly;
	readonly Dictionary<string, Type> typeNameMap;

	public DiaSessionWrapperHelper(string assemblyFileName)
	{
		try
		{
#if NETFRAMEWORK
			assembly = Assembly.ReflectionOnlyLoadFrom(assemblyFileName);
			var assemblyDirectory = Path.GetDirectoryName(assemblyFileName);

			if (assemblyDirectory is not null)
				AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += (sender, args) =>
				{
					try
					{
						// Try to load it normally
						var name = AppDomain.CurrentDomain.ApplyPolicy(args.Name);
						return Assembly.ReflectionOnlyLoad(name);
					}
					catch
					{
						try
						{
							// If a normal implicit load fails, try to load it from the directory that
							// the test assembly lives in
							return Assembly.ReflectionOnlyLoadFrom(
								Path.Combine(
									assemblyDirectory,
									new AssemblyName(args.Name).Name + ".dll"
								)
							);
						}
						catch
						{
							// If all else fails, say we couldn't find it
							return null;
						}
					}
				};
#else
			assembly = Assembly.Load(new AssemblyName { Name = Path.GetFileNameWithoutExtension(assemblyFileName) });
#endif
		}
		catch { }

		if (assembly is not null)
		{
			Type?[]? types = null;

			try
			{
				types = assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				types = ex.Types;
			}
			catch { }  // Ignore anything other than ReflectionTypeLoadException

			if (types is not null)
				typeNameMap =
					types
						.WhereNotNull()
						.Where(t => !string.IsNullOrEmpty(t.FullName))
						.ToDictionaryIgnoringDuplicateKeys(k => k.FullName!);
		}

		typeNameMap ??= new();
	}

	public void Normalize(
		ref string typeName,
		ref string methodName)
	{
		try
		{
			if (assembly is null)
				return;

			if (typeNameMap.TryGetValue(typeName, out var type) && type is not null)
			{
				var method = type.GetMethod(methodName);
				if (method is not null && method.DeclaringType is not null && method.DeclaringType.FullName is not null)
				{
					// DiaSession only ever wants you to ask for the declaring type
					typeName = method.DeclaringType.FullName;

					// See if this is an async method by looking for [AsyncStateMachine] on the method,
					// which means we need to pass the state machine's "MoveNext" method.
					var stateMachineType = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
					if (stateMachineType is not null && stateMachineType.FullName is not null)
					{
						typeName = stateMachineType.FullName;
						methodName = "MoveNext";
					}
				}
			}
		}
		catch { }
	}
}
