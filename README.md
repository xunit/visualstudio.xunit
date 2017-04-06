## About This Project

This project contains the Visual Studio runner for xUnit.net. It supports the built-in Test Explorer feature in Visual Studio 2012 and later (all editions except Express). It supports Desktop .NET 2.0, Windows 8.1, Windows Phone 8.1 Applications, and Universal Windows Apps 10.0 (and later). It can run tests from xUnit.net 1.9.2 and later.

To open an issue for this project, please visit the [core xUnit.net project issue tracker](https://github.com/xunit/xunit/issues).

## Debugging 

Debugging the VS Adapter is tricky. There are two ways to do it depending on whether you want to do it under `net46` or `netcoreapp1.0`. In all cases, you'll currently need to build your own test adapter NuGet package using `build.ps1`, `build.ps1 Packages` first to ensure you have local symbols. The symbols are not in the public package. It's helpful to add it to a local `\packages` directory and then use an entry like `<add key="Local Packages" value=".\packages" />` in your `NuGet.config` file to point to it. Don't forget to eventually delete it from your global profile `.nuget\packages\xunit...` when you're done.

### `net46`
Easiest thing to do is add a `launchSettings.json` file that adds the `vstest.console.exe` as a startup project and point it to an xunit dll. Something like the following (use `/listtests` if you just want to debug the discovery portion):

```json
{
  "profiles": {
    "vstest console": {
      "executablePath": "C:\\Program Files (x86)\\Microsoft Visual Studio\\2017\\Enterprise\\Common7\\IDE\\CommonExtensions\\Microsoft\\TestWindow\\vstest.console.exe",
      "commandLineArgs": ".\\bin\\Debug\\net46\\Tests.System.Reactive.dll /TestAdapterPath:.\\bin\\Debug\\net46 /listtests",
      "workingDirectory": "C:\\dev\\RxNET\\Rx.NET\\Source\\Tests.System.Reactive\\"
    }
  }
}
```

With that as the startup project, you can set breakpoints and then hit them. You may need to manually load symbols the first time if it's not detected automatically.

### `netcoreapp1.0`

Debugging the .NET Core version of the runner is currently much more difficult. You'll need [Process Explorer](https://technet.microsoft.com/en-us/sysinternals/processexplorer.aspx) to help locate the correct process to debug. This limitation should be improved in subsequent .NET Test Platform releases.

1. Start a PowerShell console and navigate to the directory with your test project
2. Set an environment variable in that console session: `$env:VSTEST_HOST_DEBUG = 1`
3. Build your test project: `dotnet build`
4. Have VS open with the xUnit solution loaded
5. Have Process Explorer open and in Tree View mode. You may want to update the "highlight delay" settings to 2-3 settings. Defaults to 1. Make the list scroll roughly to the "d's" 
6. Execute the test: `dotnet vstest .\bin\debug\netcoreapp1.0\MyTest.dll` (you can use the  `-lt` switch to do discovery only)
7. The test adapter will wait for about 30 seconds for you to attach a debugger. You need to look for the "lowest" `dotnet.exe` process in the tree like this: ![img](https://cloud.githubusercontent.com/assets/1427284/21454655/2ca31676-c8e8-11e6-937b-06b16d8b9254.png). In this case, the PID you're looking for is `79404`.
8. In VS, go to Debug -> Attach to Process and look for the PID (easiest to sort the column by PID). Ensure the debugger type is "Automatic" and it'll choose the CoreCLR debugger. 
9. Attach and then quickly hit Continue. It should load up the adapter and related code with symbols.

## About xUnit.net

[<img align="right" src="https://xunit.github.io/images/dotnet-fdn-logo.png" width="100" />](https://www.dotnetfoundation.org/)

xUnit.net is a free, open source, community-focused unit testing tool for the .NET Framework. Written by the original inventor of NUnit v2, xUnit.net is the latest technology for unit testing C#, F#, VB.NET and other .NET languages. xUnit.net works with ReSharper, CodeRush, TestDriven.NET and Xamarin. It is part of the [.NET Foundation](https://www.dotnetfoundation.org/), and operates under their [code of conduct](http://www.dotnetfoundation.org/code-of-conduct). It is licensed under [Apache 2](https://opensource.org/licenses/Apache-2.0) (an OSI approved license).

For project documentation, please visit the [xUnit.net project home](https://xunit.github.io/).
