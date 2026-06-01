#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover;

// Adaptation of com.gregtechceu.gtceu.api.capability.ICoverable - the holder
// surface a machine exposes so covers can attach to it.
//
// Upstream ICoverable is ~376 lines, most of it voxel-shape / raytrace /
// rendering / wrench copy-paste. This port keeps only the cover-container
// surface: per-side storage, place / remove, lifecycle, save / load.
// MetaMachine implements it (4 sides - see CoverSide). The place / remove /
// persistence methods are default-interface methods, verbatim-adapted from
// upstream's ICoverable defaults.
public interface ICoverable
{
	// ===== Holder identity (covers read these) ===============================

	Point16 GetBlockPos();
	bool IsRemote { get; }
	void NotifyBlockUpdate();
	TickableSubscription? SubscribeServerTick(Action runnable);
	void Unsubscribe(TickableSubscription? subscription);

	// Adaptation of upstream MetaMachine.getOffsetTimer() - the world game-time
	// plus a per-machine offset. Covers use `% N` against it to stagger their
	// periodic work (conveyor / pump / voiding tick every 5).
	long GetOffsetTimer();

	// ===== Per-side storage (implementor provides the backing array) =========

	CoverBehavior? GetCoverAtSide(CoverSide side);
	void SetCoverAtSide(CoverBehavior? cover, CoverSide side);
	bool CanPlaceCoverOnSide(CoverDefinition definition, CoverSide side);

	// ===== Place / remove (verbatim-adapted from upstream defaults) ==========

	bool PlaceCoverOnSide(CoverSide side, Item itemStack, CoverDefinition definition)
	{
		var cover = definition.CreateCoverBehavior(this, side);
		if (!CanPlaceCoverOnSide(definition, side) || !cover.CanAttach())
			return false;
		if (GetCoverAtSide(side) != null)
			RemoveCover(side);
		cover.OnAttached(itemStack);
		cover.OnLoad();
		SetCoverAtSide(cover, side);
		NotifyBlockUpdate();
		return true;
	}

	// Removes the cover and returns its drops. drops[0] is the cover item
	// itself (the pick item); any further entries are additional drops. The
	// caller decides where they go - UI removal puts the cover item on the
	// cursor, machine break drops everything in-world via MetaMachine.OnKill.
	List<Item> RemoveCover(CoverSide side)
	{
		var drops = new List<Item>();
		var cover = GetCoverAtSide(side);
		if (cover == null) return drops;
		if (!cover.GetPickItem().IsAir) drops.Add(cover.GetPickItem());
		drops.AddRange(cover.GetAdditionalDrops());
		cover.OnRemoved();
		SetCoverAtSide(null, side);
		NotifyBlockUpdate();
		return drops;
	}

	IEnumerable<CoverBehavior> GetCovers()
	{
		foreach (var side in CoverSides.All)
		{
			var cover = GetCoverAtSide(side);
			if (cover != null) yield return cover;
		}
	}

	bool HasCover(CoverSide side) => GetCoverAtSide(side) != null;

	bool HasAnyCover()
	{
		foreach (var side in CoverSides.All)
			if (GetCoverAtSide(side) != null) return true;
		return false;
	}

	void OnCoversUnload()
	{
		foreach (var cover in GetCovers()) cover.OnUnload();
	}

	void OnCoversNeighborChanged()
	{
		foreach (var cover in GetCovers()) cover.OnNeighborChanged();
	}

	// ===== Persistence ========================================================

	void SaveCovers(TagCompound tag)
	{
		foreach (var side in CoverSides.All)
		{
			var cover = GetCoverAtSide(side);
			if (cover == null) continue;
			var coverTag = new TagCompound { ["id"] = cover.CoverDefinition.Id };
			cover.Save(coverTag);
			tag[$"cover_{(int)side}"] = coverTag;
		}
	}

	void LoadCovers(TagCompound tag)
	{
		foreach (var side in CoverSides.All)
		{
			string key = $"cover_{(int)side}";
			var existing = GetCoverAtSide(side);
			if (!tag.ContainsKey(key))
			{
				// Side has no saved cover. SaveCovers omits keys for empty
				// sides entirely - a server-side removal arrives at the
				// client as a sync blob with the key MISSING, not as an
				// explicit null. Without this clear, the client keeps
				// rendering the stale cover (the "cover dupes / slot doesn't
				// update on remove" bug).
				if (existing != null)
				{
					existing.OnUnload();
					SetCoverAtSide(null, side);
				}
				continue;
			}
			var coverTag = tag.GetCompound(key);
			var definition = CoverRegistry.Get(coverTag.GetString("id"));
			if (definition == null) continue;
			// Same definition still on this side - reload its state in place
			// instead of swapping the instance, so OnUnload / OnLoad don't
			// churn every periodic sync.
			if (existing != null && existing.CoverDefinition == definition)
			{
				existing.Load(coverTag);
				continue;
			}
			existing?.OnUnload();
			var cover = definition.CreateCoverBehavior(this, side);
			cover.Load(coverTag);
			SetCoverAtSide(cover, side);
			cover.OnLoad();
		}
	}
}
