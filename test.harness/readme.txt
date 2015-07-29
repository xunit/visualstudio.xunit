To use this, set your startup to be a program with the following path:
C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe

Set your working directory to your output dir:
c:\dev\xunit.vsrunner\test.harness\bin\Debug\

Set the command line args to
test.harness.dll /testadapterpath:C:\dev\xunit.vsrunner\xunit.runner.visualstudio.testadapter\bin\Debug

You'll need to enable native debugging and enable the child process debugger. To enable the child process debugger,
install the following extension:
https://visualstudiogallery.msdn.microsoft.com/a1141bff-463f-465f-9b6d-d29b7b503d7a
