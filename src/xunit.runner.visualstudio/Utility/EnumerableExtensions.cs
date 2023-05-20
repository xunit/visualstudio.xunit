namespace System.Collections.Generic;

static class EnumerableExtensions
{
	public static void ForEach<T>(
		this IEnumerable<T> This,
		Action<T> action)
	{
		foreach (var item in This)
			action(item);
	}
}
