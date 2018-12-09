using System.Reflection;

[assembly: AssemblyCompany(".NET Foundation")]
[assembly: AssemblyProduct("xUnit.net Testing Framework")]
[assembly: AssemblyCopyright("Copyright (C) .NET Foundation")]
[assembly: AssemblyVersion("99.99.99.0")]
[assembly: AssemblyFileVersion("99.99.99.0")]
[assembly: AssemblyInformationalVersion("99.99.99-dev")]

#if NET452
[assembly: AssemblyTitle("xUnit.net Runner for Visual Studio (.NET 4.5.2)")]
#elif NETCOREAPP1_0
[assembly: AssemblyTitle("xUnit.net Runner for Visual Studio (.NET Core 1.0)")]
#elif WINDOWS_UAP
[assembly: AssemblyTitle("xUnit.net Runner for Visual Studio (Universal Windows 10.0)")]
#else
#error Unknown target platform
#endif
