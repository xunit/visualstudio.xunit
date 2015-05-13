namespace Xunit
{
    internal static class ExecutionHelper
    {
        /// <summary>
        /// Gets the name of the execution DLL used to run xUnit.net v2 tests.
        /// </summary>

#if WINDOWS_PHONE_APP || WINDOWS_APP
        public static readonly string AssemblyName = "xunit.execution.universal.dll";
#else
        public static readonly string AssemblyName = "xunit.execution.desktop.dll";
#endif
    }
}