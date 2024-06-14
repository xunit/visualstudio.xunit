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
}
