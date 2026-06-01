#nullable enable
using GregTechCEuTerraria.Common.Energy;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;

// Per-world CableLayer + save/load. Co-indexed lists; voltage/amps/loss
// denormalised into the cell so load doesn't need MaterialRegistry first.
public sealed class CableLayerSystem : ModSystem
{
	public static CableLayer Cables { get; } = new();

	public override void Load()
	{
		On_Main.DoDraw_WallsAndBlacks += DrawCablesAfterWalls;
	}

	public override void Unload()
	{
		On_Main.DoDraw_WallsAndBlacks -= DrawCablesAfterWalls;
		Cables.Clear();
	}

	private static void DrawCablesAfterWalls(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self)
	{
		orig(self);
		CableRenderer.DrawVisible();
	}

	public override void ClearWorld() => Cables.Clear();

	public override void OnWorldLoad()
	{
		TerrariaCompat.Net.CablePackets.SendLayerRequest();
	}

	// Re-render layer above tiles while holding a wire or wire cutter.
	public override void PostDrawTiles()
	{
		var held = Main.LocalPlayer?.HeldItem;
		bool wireRelated = held?.ModItem is Items.Cables.WireItem
			|| (held?.ModItem is Items.Tools.ToolItem tool && tool.IsWireCutter);
		if (!wireRelated) return;
		CableRenderer.DrawForegroundOverlay();
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Cables.Count == 0) return;
		var xs = new List<int>(Cables.Count);
		var ys = new List<int>(Cables.Count);
		var mats = new List<string>(Cables.Count);
		var sizes = new List<byte>(Cables.Count);
		var flags = new List<byte>(Cables.Count);
		var volts = new List<byte>(Cables.Count);
		var amps  = new List<int>(Cables.Count);
		var losses= new List<int>(Cables.Count);
		foreach (var kv in Cables.All)
		{
			var cell = kv.Value;
			xs.Add(kv.Key.x); ys.Add(kv.Key.y);
			mats.Add(cell.MaterialId);
			sizes.Add(cell.WireSize);
			flags.Add((byte)(cell.Insulated ? 1 : 0));
			volts.Add((byte)cell.Voltage);
			amps.Add(cell.BaseAmperage);
			losses.Add(cell.LossPerAmp);
		}
		tag["cables.xs"] = xs;
		tag["cables.ys"] = ys;
		tag["cables.mats"]  = mats;
		tag["cables.sizes"] = sizes;
		tag["cables.flags"] = flags;
		tag["cables.volts"] = volts;
		tag["cables.amps"]  = amps;
		tag["cables.losses"]= losses;
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Cables.Clear();
		// v1 (cables.tiers byte format) no longer supported.
		if (!tag.ContainsKey("cables.xs") || !tag.ContainsKey("cables.mats")) return;
		var xs    = tag.GetList<int>("cables.xs");
		var ys    = tag.GetList<int>("cables.ys");
		var mats  = tag.GetList<string>("cables.mats");
		var sizes = tag.GetList<byte>("cables.sizes");
		var flags = tag.GetList<byte>("cables.flags");
		var volts = tag.GetList<byte>("cables.volts");
		var amps  = tag.GetList<int>("cables.amps");
		var losses= tag.GetList<int>("cables.losses");
		int n = System.Math.Min(xs.Count, System.Math.Min(ys.Count, mats.Count));
		for (int i = 0; i < n; i++)
		{
			byte voltIdx = volts.Count > i ? volts[i] : (byte)0;
			if (voltIdx > 14) voltIdx = 0;
			Cables.Set(xs[i], ys[i], new CableCell(
				MaterialId: mats[i],
				WireSize:   sizes.Count > i ? sizes[i] : (byte)1,
				Insulated:  flags.Count > i && (flags[i] & 1) != 0,
				Voltage:    (VoltageTier)voltIdx,
				BaseAmperage: amps.Count > i ? amps[i] : 0,
				LossPerAmp:   losses.Count > i ? losses[i] : 0));
		}
	}
}
