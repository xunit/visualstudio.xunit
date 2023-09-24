using System;
using System.IO;
using System.Reflection;

internal static class AssemblyExtensions
{
	public static string? GetLocalCodeBase(this Assembly assembly)
	{
#if NETFRAMEWORK
		string? codeBase = assembly.CodeBase;
		if (codeBase is null)
			return null;

		if (!codeBase.StartsWith("file:///"))
			throw new ArgumentException($"Code base {codeBase} in wrong format; must start with file:///", "assembly");

		codeBase = codeBase.Substring(8);
		if (Path.DirectorySeparatorChar == '/')
			return "/" + codeBase;

		return codeBase.Replace('/', Path.DirectorySeparatorChar);
#else
		return assembly.Location;
#endif
	}
}

