#nullable enable
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Pattern.Util;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.pattern.util.PatternMatchContext.
//
// Typed key-value store shared between every predicate participating in one
// pattern-match attempt. Predicates use it to thread cross-cell state - e.g.
// "remember the tier of the first coil I saw so subsequent coils have to
// match", "accumulate which tiles should suppress formed-state rendering",
// etc. Cleared between match attempts via Reset().
//
// Adaptations:
//   - Java generic `<T>` accessors map cleanly to C# generics. Casts are
//     unchecked in both - same risk surface.
//   - `Supplier<T>` -> `Func<T>` for the `GetOrCreate(key, factory)` overload.
public class PatternMatchContext
{
	private readonly Dictionary<string, object> _data = new();

	public void Reset() => _data.Clear();

	public void Set(string key, object value) => _data[key] = value;

	public int GetInt(string key) =>
		_data.TryGetValue(key, out var v) ? (int)v : 0;

	public void Increment(string key, int value) =>
		Set(key, GetOrDefault(key, 0) + value);

	public T GetOrDefault<T>(string key, T defaultValue) =>
		_data.TryGetValue(key, out var v) ? (T)v : defaultValue;

	public T? Get<T>(string key) where T : class =>
		_data.TryGetValue(key, out var v) ? (T)v : null;

	public T GetOrCreate<T>(string key, Func<T> creator) where T : class
	{
		var result = Get<T>(key);
		if (result is null)
		{
			result = creator();
			Set(key, result);
		}
		return result;
	}

	public T GetOrPut<T>(string key, T initialValue) where T : class
	{
		var result = Get<T>(key);
		if (result is null)
		{
			result = initialValue;
			Set(key, result);
		}
		return result;
	}

	public bool ContainsKey(string key) => _data.ContainsKey(key);

	public IEnumerable<KeyValuePair<string, object>> EntrySet => _data;
}
