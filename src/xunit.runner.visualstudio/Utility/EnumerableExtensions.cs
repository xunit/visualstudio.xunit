using System.Linq;

namespace System.Collections.Generic;

static class EnumerableExtensions
{
	static readonly Func<object, bool> notNullTest = x => x is not null;

	public static void ForEach<T>(
		this IEnumerable<T> This,
		Action<T> action)
	{
		foreach (var item in This)
			action(item);
	}

	public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
		where T : class =>
			source.Where((Func<T?, bool>)notNullTest)!;
}
