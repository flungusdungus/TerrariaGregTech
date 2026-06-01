#nullable enable
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Persists FavoritesRegistry per character. tML invokes SaveData on player
// save (world save / exit) and LoadData when the player file is read. The
// registry is static, so LoadData wholesale REPLACES it - guarantees a
// character switch picks up its own set instead of leaking the previous
// player's pins.
public sealed class FavoritesPlayer : ModPlayer
{
	private const string Key = "gtFavorites";

	public override void SaveData(TagCompound tag)
	{
		var list = new List<TagCompound>();
		foreach (var e in FavoritesRegistry.Entries)
		{
			var sub = new TagCompound();
			if (e.ItemType > 0)
			{
				// ItemIO.Save captures mod identity (ModItem.FullName) so a
				// future load survives type-id reshuffles and degrades to a
				// silent drop if the source mod is removed.
				if (e.ItemType < ContentSamples.ItemsByType.Count)
					sub["item"] = ItemIO.Save(ContentSamples.ItemsByType[e.ItemType]);
				else continue;
			}
			else if (!string.IsNullOrEmpty(e.FluidId))
			{
				sub["fluidId"] = e.FluidId;
				if (!string.IsNullOrEmpty(e.FluidLabel)) sub["fluidLabel"] = e.FluidLabel;
			}
			else continue;
			list.Add(sub);
		}
		tag[Key] = list;
	}

	public override void LoadData(TagCompound tag)
	{
		FavoritesRegistry.Clear();
		if (!tag.TryGet<List<TagCompound>>(Key, out var list)) return;
		foreach (var sub in list)
		{
			if (sub.ContainsKey("item"))
			{
				var item = ItemIO.Load(sub.GetCompound("item"));
				if (item is not null && !item.IsAir && item.type > ItemID.None)
					FavoritesRegistry.AddItemSilent(item.type);
				// type == 0 (mod uninstalled / unresolvable) drops silently.
			}
			else if (sub.ContainsKey("fluidId"))
			{
				string id = sub.GetString("fluidId");
				string? label = sub.ContainsKey("fluidLabel") ? sub.GetString("fluidLabel") : null;
				FavoritesRegistry.AddFluidSilent(id, label);
			}
		}
	}
}
