# Building xUnit.net VSTest Adapter

The primary build system for xUnit.net is done via command line. You can also build from within Visual Studio 2022 (or later)
as the only supported IDE environment (others like Resharper should work, though).

## Pre-Requisites

You will need the following software installed:

* .NET Framework 4.6.2 or later (part of the Windows OS)
* [.NET SDK 7.0](https://dotnet.microsoft.com/download/dotnet/7.0)
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

## Debugging

Debugging the VS Adapter is tricky. There are two ways to do it depending on whether you want to do it under `net462` or `net6.0`. In all cases, you'll currently need to build your own test adapter NuGet package using `./build.ps1 Build` first to ensure you have local symbols. The symbols are not in the public package. It's helpful to add it to a local `\packages` directory and then use an entry like `<add key="Local Packages" value=".\packages" />` in your `NuGet.config` file to point to it. Don't forget to eventually delete it from your global profile `.nuget\packages\xunit...` when you're done.

### `net462`
Easiest thing to do is add a `launchSettings.json` file that adds the `vstest.console.exe` as a startup project and point it to an xunit dll. Something like the following (use `/listtests` if you just want to debug the discovery portion):

```json
{
  "profiles": {
    "vstest console": {
      "executablePath": "C:\\Program Files\\Microsoft Visual Studio\\2022\\Enterprise\\Common7\\IDE\\CommonExtensions\\Microsoft\\TestWindow\\vstest.console.exe",
      "commandLineArgs": ".\\bin\\Debug\\net462\\Tests.System.Reactive.dll /TestAdapterPath:.\\bin\\Debug\\net462 /listtests",
      "workingDirectory": "C:\\dev\\RxNET\\Rx.NET\\Source\\Tests.System.Reactive\\"
    }
  }
}
```

With that as the startup project, you can set breakpoints and then hit them. You may need to manually load symbols the first time if it's not detected automatically.

### `net6.0`

Debugging the .NET Core version of the runner is currently much more difficult. You'll need [Process Explorer](https://technet.microsoft.com/en-us/sysinternals/processexplorer.aspx) to help locate the correct process to debug. This limitation should be improved in subsequent .NET Test Platform releases.

1. Start a PowerShell console and navigate to the directory with your test project
2. Set an environment variable in that console session: `$env:VSTEST_HOST_DEBUG = 1`
3. Build your test project: `dotnet build`
4. Have VS open with the xUnit solution loaded
5. Have Process Explorer open and in Tree View mode. You may want to update the "highlight delay" settings to 2-3 settings. Defaults to 1. Make the list scroll roughly to the "d's"
6. Execute the test: `dotnet vstest .\bin\debug\net6.0\MyTest.dll` (you can use the  `-lt` switch to do discovery only)
7. The test adapter will wait for about 30 seconds for you to attach a debugger. You need to look for the "lowest" `dotnet.exe` process in the tree like this: ![img](https://cloud.githubusercontent.com/assets/1427284/21454655/2ca31676-c8e8-11e6-937b-06b16d8b9254.png). In this case, the PID you're looking for is `79404`.
8. In VS, go to Debug -> Attach to Process and look for the PID (easiest to sort the column by PID). Ensure the debugger type is "Automatic" and it'll choose the CoreCLR debugger.
9. Attach and then quickly hit Continue. It should load up the adapter and related code with symbols.

