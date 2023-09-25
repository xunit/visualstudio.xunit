using System;
using System.Collections.Generic;

static class DictionaryExtensions
{
	public static Dictionary<TKey, TValue> ToDictionaryIgnoringDuplicateKeys<TKey, TValue>(
		this IEnumerable<TValue> inputValues,
		Func<TValue, TKey> keySelector,
		IEqualityComparer<TKey>? comparer = null)
			where TKey : notnull =>
				ToDictionaryIgnoringDuplicateKeys(inputValues, keySelector, x => x, comparer);

	public static Dictionary<TKey, TValue> ToDictionaryIgnoringDuplicateKeys<TInput, TKey, TValue>(
		this IEnumerable<TInput> inputValues,
		Func<TInput, TKey> keySelector,
		Func<TInput, TValue> valueSelector,
		IEqualityComparer<TKey>? comparer = null)
			where TKey : notnull
	{
		var result = new Dictionary<TKey, TValue>(comparer);

		foreach (var inputValue in inputValues)
		{
			var key = keySelector(inputValue);
			if (!result.ContainsKey(key))
				result.Add(key, valueSelector(inputValue));
		}

		return result;
	}
}
