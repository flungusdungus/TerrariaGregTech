#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Serialization;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.QuestBooksInterop;

// JSON SerializationBinder used to deserialize qb-questlog.json into a
// List<QuestBook> on OUR side, then handed to QuestBooksMod.AddQuestLog's
// IList overload (which skips QB's own JsonTypeResolverFix).
//
// Why we need our own binder: QB's JsonTypeResolverFix calls `Assembly.Load
// (assemblyName)` for every $type discriminator. tML loads each mod's
// assembly into its own AssemblyLoadContext - calls to Assembly.Load made
// from QB's ALC can resolve QuestBooks, but not GregTechCEuTerraria
// (cross-mod ALC resolution isn't wired through that path). The
// FileNotFoundException there auto-disables our mod on load.
//
// This binder resolves OUR types directly from `typeof(GTQuestDisplay)
// .Assembly`, QB types directly from `typeof(QuestBook).Assembly`, and
// .NET runtime types via `Type.GetType` (which the default ALC handles
// fine). Recursive resolution for generic `List<T>` mirrors QB's own
// JsonTypeResolverFix.GetGenericTypeFromTypeName - same parser, just
// scoped to the assemblies WE know how to load.
[ExtendsFromMod("QuestBooks")]
internal sealed class GTQBJsonBinder : ISerializationBinder
{
	private static readonly Assembly _ourAsm = typeof(GTQBJsonBinder).Assembly;
	private static readonly Assembly _qbAsm  = typeof(QuestBooks.QuestLog.QuestBook).Assembly;

	private readonly Dictionary<string, Type> _cache = new(StringComparer.Ordinal);

	public Type BindToType(string? assemblyName, string typeName)
	{
		string key = (assemblyName ?? "") + "|" + typeName;
		if (_cache.TryGetValue(key, out var hit)) return hit;
		var t = ResolveType(assemblyName, typeName);
		_cache[key] = t;
		return t;
	}

	public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
	{
		assemblyName = serializedType.Assembly.GetName().Name;
		typeName = serializedType.FullName;
	}

	private static Type ResolveType(string? assemblyName, string typeName)
	{
		Assembly? asm = ResolveAssembly(assemblyName);
		if (asm is null)
			throw new InvalidOperationException(
				$"GTQBJsonBinder: cannot resolve assembly '{assemblyName}' for type '{typeName}'");

		// Generic types arrive as `Outer`1[[Inner, Asm], ...]`; recurse for each
		// inner type argument. Same shape JsonTypeResolverFix.GetGenericTypeFromTypeName
		// parses - we just route the recursion through this same ResolveType so
		// our assembly resolution stays consistent end-to-end.
		int bracket = typeName.IndexOf('[');
		if (bracket < 0)
		{
			var direct = asm.GetType(typeName);
			if (direct is null)
				throw new InvalidOperationException(
					$"GTQBJsonBinder: type '{typeName}' not found in assembly '{asm.GetName().Name}'");
			return direct;
		}

		string defName = typeName.Substring(0, bracket);
		Type? def = asm.GetType(defName)
			?? throw new InvalidOperationException(
				$"GTQBJsonBinder: generic def '{defName}' not found in assembly '{asm.GetName().Name}'");

		var args = new List<Type>();
		int scope = 0;
		int argStart = 0;
		int end = typeName.Length - 1;
		for (int i = bracket + 1; i < end; i++)
		{
			char c = typeName[i];
			if (c == '[')
			{
				if (scope == 0) argStart = i + 1;
				scope++;
			}
			else if (c == ']')
			{
				scope--;
				if (scope == 0)
				{
					string argQualified = typeName.Substring(argStart, i - argStart);
					int sep = FindAssemblyDelimiter(argQualified);
					string argTypeName = sep >= 0 ? argQualified.Substring(0, sep) : argQualified;
					string? argAsmName = sep >= 0 ? argQualified.Substring(sep + 1).TrimStart() : null;
					args.Add(ResolveType(argAsmName, argTypeName));
				}
			}
		}

		return def.MakeGenericType(args.ToArray());
	}

	private static Assembly? ResolveAssembly(string? assemblyName)
	{
		if (string.IsNullOrEmpty(assemblyName)) return null;
		// Short-name match wins: tML mods register under their bare assembly
		// name, no version/culture/key suffix.
		string shortName = ShortName(assemblyName);
		if (shortName == _ourAsm.GetName().Name) return _ourAsm;
		if (shortName == _qbAsm.GetName().Name)  return _qbAsm;
		// .NET runtime types route through Type.GetType / Assembly.Load - the
		// default ALC always sees System.Private.CoreLib + friends.
		try { return Assembly.Load(assemblyName); }
		catch { return null; }
	}

	private static string ShortName(string asmFullName)
	{
		int comma = asmFullName.IndexOf(',');
		return comma < 0 ? asmFullName.Trim() : asmFullName.Substring(0, comma).Trim();
	}

	// Same algorithm as JsonTypeResolverFix.GetAssemblyDelimiterIndex: find the
	// first top-level comma. Inner generic args are bracket-nested.
	private static int FindAssemblyDelimiter(string fullyQualified)
	{
		int scope = 0;
		for (int i = 0; i < fullyQualified.Length; i++)
		{
			char c = fullyQualified[i];
			if (c == '[') scope++;
			else if (c == ']') scope--;
			else if (c == ',' && scope == 0) return i;
		}
		return -1;
	}
}
