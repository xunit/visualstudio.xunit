using System;

internal static class Guard
{
    public static void ArgumentNotNull(string argName, object argValue)
    {
        if (argValue == null)
            throw new ArgumentNullException(argName);
    }
}
