using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class DiagnosticMessageVisitor : TestMessageVisitor
    {
        readonly string assemblyDisplayName;
        readonly IMessageLogger logger;
        readonly bool showDiagnostics;
        readonly Stopwatch stopwatch;

        public DiagnosticMessageVisitor(IMessageLogger logger, string assemblyDisplayName, bool showDiagnostics, Stopwatch stopwatch)
        {
            this.logger = logger;
            this.assemblyDisplayName = assemblyDisplayName;
            this.showDiagnostics = showDiagnostics;
            this.stopwatch = stopwatch;
        }

        protected override bool Visit(IDiagnosticMessage diagnosticMessage)
        {
            if (showDiagnostics)
                logger.SendMessage(TestMessageLevel.Warning, string.Format("[xUnit.net {0}] {1}: {2}", stopwatch.Elapsed, assemblyDisplayName, diagnosticMessage.Message));

            return base.Visit(diagnosticMessage);
        }
    }
}
