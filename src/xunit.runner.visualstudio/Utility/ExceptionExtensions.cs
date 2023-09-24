using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

static class ExceptionExtensions
{
	/// <summary>
	/// Re-throws an exception without modifying it.
	/// </summary>
	/// <param name="ex">The exception to be re-thrown</param>
	[DoesNotReturn]
	public static void Rethrow(this Exception ex) =>
		ExceptionDispatchInfo.Capture(ex).Throw();

	/// <summary>
	/// Unwraps an exception to remove any wrappers, like <see cref="TargetInvocationException"/>.
	/// </summary>
	/// <param name="ex">The exception to unwrap.</param>
	/// <returns>The unwrapped exception.</returns>
	public static Exception Unwrap(this Exception ex)
	{
		while (true)
		{
			if (ex is not TargetInvocationException tiex || tiex.InnerException is null)
				return ex;

			ex = tiex.InnerException;
		}
	}
}
