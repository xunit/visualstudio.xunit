using System;
using Xunit.Sdk;

namespace Xunit.Runner.VisualStudio;

internal interface IVsDiscoverySink : IMessageSink, IDisposable
{
	int Finish();
}
