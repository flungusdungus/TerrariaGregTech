#nullable enable
using System;
using System.Collections.Generic;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of virtualregistry.entries.VirtualRedstone. Set of members (one per
// EnderRedstoneLinkCover); channel signal = max. Upstream's player UUID ->
// per-cover Guid.
public sealed class VirtualRedstone : VirtualEntry
{
	private readonly Dictionary<Guid, int> _members = new();

	public override EnderEntryType Type => EnderEntryType.Redstone;

	public int Signal
	{
		get
		{
			int max = 0;
			foreach (var v in _members.Values)
				if (v > max) max = v;
			return max;
		}
	}

	public void AddMember(Guid id) => _members[id] = 0;

	public void SetSignal(Guid id, int signal)
	{
		if (_members.ContainsKey(id)) _members[id] = signal;
	}

	public void RemoveMember(Guid id) => _members.Remove(id);

	public override bool CanRemove() => base.CanRemove() && _members.Count == 0;

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		var members = new TagCompound();
		foreach (var (id, signal) in _members)
			members[id.ToString()] = signal;
		tag["members"] = members;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		_members.Clear();
		if (!tag.ContainsKey("members")) return;
		var members = tag.GetCompound("members");
		foreach (var kvp in members)
			if (Guid.TryParse(kvp.Key, out var id))
				_members[id] = members.GetInt(kvp.Key);
	}
}
