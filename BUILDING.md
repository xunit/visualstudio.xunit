# Building xUnit.net VSTest Adapter

The primary build system for xUnit.net is done via command line. You can also build from within Visual Studio 2022 (or later)
as the only supported IDE environment (others like Resharper should work, though).

## Pre-Requisites

You will need the following software installed:

* .NET Framework 4.7.2 or later (part of the Windows OS)
* [.NET SDK 9.0](https://dotnet.microsoft.com/download/dotnet/9.0)
* [.NET 6.0 Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)
* [git](https://git-scm.com/downloads)
* PowerShell (or [PowerShell Core](https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell-core-on-windows?view=powershell-6))

Ensure that you have configured PowerShell to be able to run local unsigned scripts (either by running
`Set-ExecutionPolicy RemoteSigned` from within PowerShell, or by launching PowerShell with the
`-ExecutionPolicy RemoteSigned` command line switch).

## Command-Line Build

1. Open PowerShell (or PowerShell Core).

1. From the root folder of the source repo, this command will build the code & run all tests:

    `./build`

    To build a specific target (or multiple targets):

    `./build [target [target...]]`

    The common targets (case-insensitive) include:

    * `Restore`: Perform package restore
    * `Build`: Build the source
    * `Test`: Run unit tests
    * `TestCore`: Run unit tests (.NET Core)
    * `TestFx`: Run unit tests (.NET Framework)

    You can get a list of options:

    `./build --help`
