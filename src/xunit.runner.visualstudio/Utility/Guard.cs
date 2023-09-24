using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Xunit.Internal;

/// <summary>
/// Helper class for guarding value arguments and valid state.
/// </summary>
public static class Guard
{
	/// <summary>
	/// Ensures that a nullable reference type argument is not null.
	/// </summary>
	/// <typeparam name="T">The argument type</typeparam>
	/// <param name="argValue">The value of the argument</param>
	/// <param name="argName">The name of the argument</param>
	/// <returns>The argument value as a non-null value</returns>
	/// <exception cref="ArgumentNullException">Thrown when the argument is null</exception>
	public static T ArgumentNotNull<T>(
		[NotNull] T? argValue,
		[CallerArgumentExpression(nameof(argValue))] string? argName = null)
			where T : class
	{
		if (argValue is null)
			throw new ArgumentNullException(argName?.TrimStart('@'));

		return argValue;
	}

	/// <summary>
	/// Ensures that a nullable enumerable type argument is not null or empty.
	/// </summary>
	/// <typeparam name="T">The argument type</typeparam>
	/// <param name="argValue">The value of the argument</param>
	/// <param name="argName">The name of the argument</param>
	/// <returns>The argument value as a non-null, non-empty value</returns>
	/// <exception cref="ArgumentException">Thrown when the argument is null or empty</exception>
	public static T ArgumentNotNullOrEmpty<T>(
		[NotNull] T? argValue,
		[CallerArgumentExpression(nameof(argValue))] string? argName = null)
			where T : class, IEnumerable
	{
		ArgumentNotNull(argValue, argName);

		if (!argValue.GetEnumerator().MoveNext())
			throw new ArgumentException("Argument was empty", argName?.TrimStart('@'));

		return argValue;
	}
}
