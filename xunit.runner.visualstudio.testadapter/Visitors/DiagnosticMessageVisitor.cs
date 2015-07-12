using Xunit.Abstractions;

namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class DiagnosticMessageVisitor : TestMessageVisitor
    {
        readonly string assemblyDisplayName;
        readonly LoggerHelper logger;
        readonly bool showDiagnostics;

        public DiagnosticMessageVisitor(LoggerHelper logger, string assemblyDisplayName, bool showDiagnostics)
        {
            this.logger = logger;
            this.assemblyDisplayName = assemblyDisplayName;
            this.showDiagnostics = showDiagnostics;
        }

        protected override bool Visit(IDiagnosticMessage diagnosticMessage)
        {
            if (showDiagnostics)
                logger.LogWarning("{0}: {1}", assemblyDisplayName, diagnosticMessage.Message);

            return base.Visit(diagnosticMessage);
        }
    }
}
