namespace Xunit.Runner.VisualStudio.TestAdapter
{
    public class DiagnosticMessageSink : TestMessageSink
    {
        public DiagnosticMessageSink(LoggerHelper logger, string assemblyDisplayName, bool showDiagnostics)
        {
            if (showDiagnostics)
                DiagnosticMessageEvent += args => logger.LogWarning("{0}: {1}", assemblyDisplayName, args.Message.Message);
        }
    }
}
