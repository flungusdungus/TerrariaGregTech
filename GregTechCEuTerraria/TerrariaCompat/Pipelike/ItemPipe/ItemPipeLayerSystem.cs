#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Per-world ItemPipeLayer. Mirrors CableLayerSystem lifecycle + render hook.
// Save format = co-indexed lists; one PipeCoverable per cell with any
// configured side (default-state cells skip allocation).
public sealed class ItemPipeLayerSystem : ModSystem
{
	public static ItemPipeLayer Pipes { get; } = new();

	private static readonly Dictionary<(int x, int y), PipeCoverable> _sides = new();

	public static IReadOnlyDictionary<(int x, int y), PipeCoverable> AllSides => _sides;

	public static PipeCoverable? GetSides(int x, int y) =>
		_sides.TryGetValue((x, y), out var c) ? c : null;

	public static PipeCoverable EnsureSides(int x, int y)
	{
		if (_sides.TryGetValue((x, y), out var c)) return c;
		c = new PipeCoverable(PipeKind.Item, x, y);
		_sides[(x, y)] = c;
		return c;
	}

	// Called from CutAt so a cut+replaced pipe doesn't inherit stale state.
	public static void DropSides(int x, int y)
	{
		if (_sides.TryGetValue((x, y), out var c)) ((ICoverable)c).OnCoversUnload();
		_sides.Remove((x, y));
	}

	public override void Load()
	{
		On_Main.DoDraw_WallsAndBlacks += DrawAfterWalls;
	}

	public override void Unload()
	{
		On_Main.DoDraw_WallsAndBlacks -= DrawAfterWalls;
		Pipes.Clear();
	}

	private static void DrawAfterWalls(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self)
	{
		orig(self);
		PipeRenderer.DrawItemPipes();
	}

	public override void ClearWorld()
	{
		Pipes.Clear();
		foreach (var c in _sides.Values) ((ICoverable)c).OnCoversUnload();
		_sides.Clear();
	}

	// Re-render layer above tiles while holding any item-pipe item (regular
	// PipeItem or SimpleItemPipeItem - both place into this layer).
	public override void PostDrawTiles()
	{
		var held = Main.LocalPlayer?.HeldItem;
		if (held is null) return;
		bool itemLayer = held.ModItem is Items.Pipes.SimpleItemPipeItem
		              || (held.ModItem is Items.Pipes.PipeItem pipe && pipe.Kind == PipeKind.Item);
		if (!itemLayer) return;
		PipeRenderer.DrawItemForegroundOverlay();
	}

	public override void OnWorldLoad()
	{
		PipePackets.SendLayerRequest(PipeKind.Item);
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Pipes.Count == 0) return;
		var xs    = new List<int>   (Pipes.Count);
		var ys    = new List<int>   (Pipes.Count);
		var mats  = new List<string>(Pipes.Count);
		var sizes = new List<byte>  (Pipes.Count);
		var flags = new List<byte>  (Pipes.Count);
		var prios = new List<int>   (Pipes.Count);
		var rates = new List<float> (Pipes.Count);
		foreach (var kv in Pipes.All)
		{
			var c = kv.Value;
			xs.Add(kv.Key.x); ys.Add(kv.Key.y);
			mats.Add(c.MaterialId);
			sizes.Add((byte)c.Size);
			// flags bit 0 = Restrictive, bit 1 = IsSimple. Bit 1 is
			// backward-compatible: pre-IsSimple saves have bit 1 = 0,
			// loading produces IsSimple=false which is the original behaviour.
			// flags bit 0 = Restrictive, bit 1 = IsSimple (back-compat).
			byte f = 0;
			if (c.Restrictive) f |= 1;
			if (c.IsSimple)    f |= 2;
			flags.Add(f);
			prios.Add(c.Priority);
			rates.Add(c.TransferRate);
		}
		tag["item_pipes.xs"]    = xs;
		tag["item_pipes.ys"]    = ys;
		tag["item_pipes.mats"]  = mats;
		tag["item_pipes.sizes"] = sizes;
		tag["item_pipes.flags"] = flags;
		tag["item_pipes.prio"]  = prios;
		tag["item_pipes.rate"]  = rates;

		// Sparse: one row per cell with at least one cover.
		var cxs   = new List<int>();
		var cys   = new List<int>();
		var cdata = new List<TagCompound>();
		foreach (var kv in _sides)
		{
			ICoverable c = kv.Value;
			if (!c.HasAnyCover()) continue;
			var t = new TagCompound();
			c.SaveCovers(t);
			cxs.Add(kv.Key.x);
			cys.Add(kv.Key.y);
			cdata.Add(t);
		}
		if (cxs.Count > 0)
		{
			tag["item_pipes.covers.xs"]   = cxs;
			tag["item_pipes.covers.ys"]   = cys;
			tag["item_pipes.covers.data"] = cdata;
		}
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Pipes.Clear();
		_sides.Clear();
		if (!tag.ContainsKey("item_pipes.xs") || !tag.ContainsKey("item_pipes.mats")) return;
		var xs    = tag.GetList<int>   ("item_pipes.xs");
		var ys    = tag.GetList<int>   ("item_pipes.ys");
		var mats  = tag.GetList<string>("item_pipes.mats");
		var sizes = tag.GetList<byte>  ("item_pipes.sizes");
		var flags = tag.GetList<byte>  ("item_pipes.flags");
		var prios = tag.ContainsKey("item_pipes.prio") ? tag.GetList<int>   ("item_pipes.prio") : null;
		var rates = tag.ContainsKey("item_pipes.rate") ? tag.GetList<float> ("item_pipes.rate") : null;
		int n = System.Math.Min(xs.Count, System.Math.Min(ys.Count, mats.Count));
		for (int i = 0; i < n; i++)
		{
			byte fb = flags.Count > i ? flags[i] : (byte)0;
			Pipes.Set(xs[i], ys[i], new ItemPipeCell(
				MaterialId:   mats[i],
				Size:         sizes.Count > i ? (PipeSize)sizes[i] : PipeSize.Normal,
				Restrictive:  (fb & 1) != 0,
				Priority:     prios is not null && prios.Count > i ? prios[i] : 1,
				TransferRate: rates is not null && rates.Count > i ? rates[i] : 0.25f,
				IsSimple:     (fb & 2) != 0));
		}

		if (tag.ContainsKey("item_pipes.covers.xs"))
		{
			var cxs   = tag.GetList<int>         ("item_pipes.covers.xs");
			var cys   = tag.GetList<int>         ("item_pipes.covers.ys");
			var cdata = tag.GetList<TagCompound> ("item_pipes.covers.data");
			int m = System.Math.Min(cxs.Count, System.Math.Min(cys.Count, cdata.Count));
			for (int i = 0; i < m; i++)
			{
				var pcv = new PipeCoverable(PipeKind.Item, cxs[i], cys[i]);
				((ICoverable)pcv).LoadCovers(cdata[i]);
				_sides[(cxs[i], cys[i])] = pcv;
			}
		}

		// Pipes.IsDirty was set; net rebuild fires on next PostUpdateEverything
		// via ItemPipeNetSystem.MaybeRebuild.
	}
}
