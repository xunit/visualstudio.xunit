using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Xunit.Internal;

static class DictionaryExtensions
{
	public static void Add<TKey, TValue>(
		this ConcurrentDictionary<TKey, List<TValue>> dictionary,
		TKey key,
		TValue value)
			where TKey : notnull
	{
		Guard.ArgumentNotNull(dictionary);

		dictionary.GetOrAdd(key, _ => []).Add(value);
	}

	public static Dictionary<TKey, TValue> ToDictionaryIgnoringDuplicateKeys<TKey, TValue>(
		this IEnumerable<TValue> values,
		Func<TValue, TKey> keySelector,
		IEqualityComparer<TKey>? comparer = null)
			where TKey : notnull
				=> ToDictionaryIgnoringDuplicateKeys(values, keySelector, x => x, comparer);

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
