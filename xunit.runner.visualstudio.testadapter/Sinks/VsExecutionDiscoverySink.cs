using System;
using Xunit.Abstractions;

namespace Xunit.Runner.VisualStudio
{
    /// <summary>
    /// Used to discover tests before running when VS says "run everything in the assembly".
    /// </summary>
    internal class VsExecutionDiscoverySink : TestDiscoverySink, IVsDiscoverySink
    {
        readonly Func<bool> cancelThunk;

        public VsExecutionDiscoverySink(Func<bool> cancelThunk)
        {
            this.cancelThunk = cancelThunk;
        }

        public int Finish()
        {
            Finished.WaitOne();
            return TestCases.Count;
        }

        void HandleCancellation(MessageHandlerArgs args)
        {
            if (cancelThunk())
                args.Stop();
        }

        protected override void HandleDiscoveryCompleteMessage(MessageHandlerArgs<IDiscoveryCompleteMessage> args)
        {
            base.HandleDiscoveryCompleteMessage(args);

            HandleCancellation(args);
        }

        protected override void HandleTestCaseDiscoveryMessage(MessageHandlerArgs<ITestCaseDiscoveryMessage> args)
        {
            base.HandleTestCaseDiscoveryMessage(args);

            HandleCancellation(args);
        }
    }
}
