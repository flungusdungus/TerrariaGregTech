#nullable enable
using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of api.misc.virtualregistry.VirtualEnderRegistry. Upstream's
// SavedData -> tML ModSystem (same world-scoped server-side persistence).
// Owner UUID dropped - PRIVATE channels need unported MachineOwner, so every
// channel is public (single type -> name -> entry map).
public class VirtualEnderRegistry : ModSystem
{
	private readonly Dictionary<EnderEntryType, Dictionary<string, VirtualEntry>> _entries = new();

	public static VirtualEnderRegistry Instance => ModContent.GetInstance<VirtualEnderRegistry>();

	private Dictionary<string, VirtualEntry> MapFor(EnderEntryType type)
	{
		if (!_entries.TryGetValue(type, out var map))
			_entries[type] = map = new Dictionary<string, VirtualEntry>();
		return map;
	}

	private static VirtualEntry CreateInstance(EnderEntryType type) => type switch
	{
		EnderEntryType.Item     => new VirtualItemStorage(),
		EnderEntryType.Fluid    => new VirtualTank(),
		EnderEntryType.Redstone => new VirtualRedstone(),
		_                       => new VirtualItemStorage(),
	};

	public VirtualEntry? GetEntry(EnderEntryType type, string name) =>
		MapFor(type).TryGetValue(name, out var e) ? e : null;

	public bool HasEntry(EnderEntryType type, string name) => MapFor(type).ContainsKey(name);

	public VirtualEntry GetOrCreateEntry(EnderEntryType type, string name)
	{
		var map = MapFor(type);
		if (!map.TryGetValue(name, out var e))
			map[name] = e = CreateInstance(type);
		return e;
	}

	// Verbatim deleteEntryIf - channel survives while any cover or stored
	// payload still references it (predicate is always CanRemove).
	public void DeleteEntryIf(EnderEntryType type, string name, Predicate<VirtualEntry> shouldDelete)
	{
		var entry = GetEntry(type, name);
		if (entry != null && shouldDelete(entry))
			MapFor(type).Remove(name);
	}

	public override void SaveWorldData(TagCompound tag)
	{
		foreach (var (type, map) in _entries)
		{
			if (map.Count == 0) continue;
			var typeTag = new TagCompound();
			foreach (var (name, entry) in map)
			{
				var entryTag = new TagCompound();
				entry.Save(entryTag);
				typeTag[name] = entryTag;
			}
			tag[type.ToString()] = typeTag;
		}
	}

	public override void LoadWorldData(TagCompound tag)
	{
		_entries.Clear();
		foreach (EnderEntryType type in Enum.GetValues<EnderEntryType>())
		{
			string key = type.ToString();
			if (!tag.ContainsKey(key)) continue;
			var typeTag = tag.GetCompound(key);
			var map = MapFor(type);
			foreach (var kvp in typeTag)
			{
				var entry = CreateInstance(type);
				entry.Load(typeTag.GetCompound(kvp.Key));
				map[kvp.Key] = entry;
			}
		}
	}

	// Verbatim release() - fresh world doesn't inherit previous ender state.
	public override void OnWorldUnload() => _entries.Clear();
}
