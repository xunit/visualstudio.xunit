using System;
using Xunit.v3;

namespace Xunit.Runner.VisualStudio;

internal interface IVsDiscoverySink : _IMessageSink, IDisposable
{
	int Finish();
}
