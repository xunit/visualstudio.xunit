using System;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Xunit.Abstractions;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class DiagnosticMessageVisitor : TestMessageVisitor
    {
        readonly string assemblyDisplayName;
        readonly IMessageLogger logger;
        readonly bool showDiagnostics;

        public DiagnosticMessageVisitor(IMessageLogger logger, string assemblyDisplayName, bool showDiagnostics)
        {
            this.logger = logger;
            this.assemblyDisplayName = assemblyDisplayName;
            this.showDiagnostics = showDiagnostics;
        }

        protected override bool Visit(IDiagnosticMessage diagnosticMessage)
        {
            if (showDiagnostics)
                logger.SendMessage(TestMessageLevel.Warning, String.Format("{0}: {1}", assemblyDisplayName, diagnosticMessage.Message));

            return base.Visit(diagnosticMessage);
        }
    }
}
