#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;

// Per-world FluidPipeLayer. Save format = co-indexed lists; numeric pipe
// properties are denormalised so load doesn't need MaterialRegistry first.
public sealed class FluidPipeLayerSystem : ModSystem
{
	public static FluidPipeLayer Pipes { get; } = new();

	private static readonly Dictionary<(int x, int y), PipeCoverable> _sides = new();

	// Per-cell FluidPipeState; lifetime mirrors the pipe cell. Separate from
	// PipeCoverable to mirror upstream's cover/pipe-state split.
	private static readonly Dictionary<(int x, int y), FluidPipeState> _states = new();

	// Populated by FluidPipeStatsPacket; read by the pipe panel on MP clients.
	public static readonly Dictionary<(int x, int y), global::GregTechCEuTerraria.Api.Fluids.FluidStack[]>
		ClientTankSnapshots = new();

	public static IReadOnlyDictionary<(int x, int y), PipeCoverable> AllSides => _sides;

	public static IReadOnlyDictionary<(int x, int y), FluidPipeState> AllStates => _states;

	public static PipeCoverable? GetSides(int x, int y) =>
		_sides.TryGetValue((x, y), out var c) ? c : null;

	public static PipeCoverable EnsureSides(int x, int y)
	{
		if (_sides.TryGetValue((x, y), out var c)) return c;
		c = new PipeCoverable(PipeKind.Fluid, x, y);
		_sides[(x, y)] = c;
		return c;
	}

	public static void DropSides(int x, int y)
	{
		if (_sides.TryGetValue((x, y), out var c)) ((ICoverable)c).OnCoversUnload();
		_sides.Remove((x, y));
		_states.Remove((x, y));
	}

	public static FluidPipeState? GetState(int x, int y) =>
		_states.TryGetValue((x, y), out var s) ? s : null;

	public static FluidPipeState EnsureState(int x, int y)
	{
		if (_states.TryGetValue((x, y), out var s)) return s;
		var cell = Pipes.CellAt(x, y);
		if (cell is null) throw new System.InvalidOperationException(
			$"EnsureState called at ({x}, {y}) with no pipe cell.");
		var c = cell.Value;
		var props = new global::GregTechCEuTerraria.Api.Data.Chemical.Material.Properties.FluidPipeProperties
		{
			MaxFluidTemperature = c.MaxFluidTemperature,
			Throughput          = c.Throughput,
			Channels            = c.Channels,
			GasProof            = c.GasProof,
			CryoProof           = c.CryoProof,
			PlasmaProof         = c.PlasmaProof,
			AcidProof           = c.AcidProof,
		};
		s = new FluidPipeState(x, y, props);
		_states[(x, y)] = s;
		return s;
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
		PipeRenderer.DrawFluidPipes();
	}

	public override void ClearWorld()
	{
		Pipes.Clear();
		foreach (var c in _sides.Values) ((ICoverable)c).OnCoversUnload();
		_sides.Clear();
		_states.Clear();
	}

	// Re-render layer above tiles while holding any fluid-pipe item.
	public override void PostDrawTiles()
	{
		var held = Main.LocalPlayer?.HeldItem;
		if (held is null) return;
		bool fluidLayer = held.ModItem is Items.Pipes.SimpleFluidPipeItem
		               || (held.ModItem is Items.Pipes.PipeItem pipe && pipe.Kind == PipeKind.Fluid);
		if (!fluidLayer) return;
		PipeRenderer.DrawFluidForegroundOverlay();
	}

	public override void OnWorldLoad()
	{
		PipePackets.SendLayerRequest(PipeKind.Fluid);
	}

	public override void SaveWorldData(TagCompound tag)
	{
		if (Pipes.Count == 0) return;
		var xs     = new List<int>   (Pipes.Count);
		var ys     = new List<int>   (Pipes.Count);
		var mats   = new List<string>(Pipes.Count);
		var sizes  = new List<byte>  (Pipes.Count);
		var tputs  = new List<int>   (Pipes.Count);
		var chans  = new List<byte>  (Pipes.Count);
		var mtemps = new List<int>   (Pipes.Count);
		var proofs = new List<byte>  (Pipes.Count);
		foreach (var kv in Pipes.All)
		{
			var c = kv.Value;
			xs.Add(kv.Key.x); ys.Add(kv.Key.y);
			mats.Add(c.MaterialId);
			sizes.Add((byte)c.Size);
			tputs.Add(c.Throughput);
			chans.Add((byte)c.Channels);
			mtemps.Add(c.MaxFluidTemperature);
			// Bits 0-3 = proof flags, bit 4 = IsSimple (back-compat: 0 = false).
			byte proof = 0;
			if (c.GasProof)    proof |= 1;
			if (c.CryoProof)   proof |= 2;
			if (c.PlasmaProof) proof |= 4;
			if (c.AcidProof)   proof |= 8;
			if (c.IsSimple)    proof |= 16;
			proofs.Add(proof);
		}
		tag["fluid_pipes.xs"]      = xs;
		tag["fluid_pipes.ys"]      = ys;
		tag["fluid_pipes.mats"]    = mats;
		tag["fluid_pipes.sizes"]   = sizes;
		tag["fluid_pipes.tput"]    = tputs;
		tag["fluid_pipes.chan"]    = chans;
		tag["fluid_pipes.maxtemp"] = mtemps;
		tag["fluid_pipes.proof"]   = proofs;

		// Per-cell covers (sparse).
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
			tag["fluid_pipes.covers.xs"]   = cxs;
			tag["fluid_pipes.covers.ys"]   = cys;
			tag["fluid_pipes.covers.data"] = cdata;
		}

		// Sparse: only non-empty states saved; empty re-derive on reload.
		var sxs   = new List<int>();
		var sys   = new List<int>();
		var sdata = new List<TagCompound>();
		foreach (var kv in _states)
		{
			if (!kv.Value.HasAnyFluid()) continue;
			sxs.Add(kv.Key.x);
			sys.Add(kv.Key.y);
			sdata.Add(kv.Value.SaveTo());
		}
		if (sxs.Count > 0)
		{
			tag["fluid_pipes.states.xs"]   = sxs;
			tag["fluid_pipes.states.ys"]   = sys;
			tag["fluid_pipes.states.data"] = sdata;
		}
	}

	public override void LoadWorldData(TagCompound tag)
	{
		Pipes.Clear();
		_sides.Clear();
		if (!tag.ContainsKey("fluid_pipes.xs") || !tag.ContainsKey("fluid_pipes.mats")) return;
		var xs     = tag.GetList<int>   ("fluid_pipes.xs");
		var ys     = tag.GetList<int>   ("fluid_pipes.ys");
		var mats   = tag.GetList<string>("fluid_pipes.mats");
		var sizes  = tag.GetList<byte>  ("fluid_pipes.sizes");
		var tputs  = tag.GetList<int>   ("fluid_pipes.tput");
		var chans  = tag.GetList<byte>  ("fluid_pipes.chan");
		var mtemps = tag.GetList<int>   ("fluid_pipes.maxtemp");
		var proofs = tag.GetList<byte>  ("fluid_pipes.proof");
		int n = System.Math.Min(xs.Count, System.Math.Min(ys.Count, mats.Count));
		for (int i = 0; i < n; i++)
		{
			byte proof = proofs.Count > i ? proofs[i] : (byte)0;
			Pipes.Set(xs[i], ys[i], new FluidPipeCell(
				MaterialId:          mats[i],
				Size:                sizes.Count  > i ? (PipeSize)sizes[i] : PipeSize.Normal,
				Throughput:          tputs.Count  > i ? tputs[i]  : 0,
				Channels:            chans.Count  > i ? chans[i]  : 1,
				MaxFluidTemperature: mtemps.Count > i ? mtemps[i] : 0,
				GasProof:    (proof & 1)  != 0,
				CryoProof:   (proof & 2)  != 0,
				PlasmaProof: (proof & 4)  != 0,
				AcidProof:   (proof & 8)  != 0,
				IsSimple:    (proof & 16) != 0));
		}

		// Per-cell covers (sparse - only cells with HasAnyCover were saved).
		if (tag.ContainsKey("fluid_pipes.covers.xs"))
		{
			var cxs   = tag.GetList<int>         ("fluid_pipes.covers.xs");
			var cys   = tag.GetList<int>         ("fluid_pipes.covers.ys");
			var cdata = tag.GetList<TagCompound> ("fluid_pipes.covers.data");
			int m = System.Math.Min(cxs.Count, System.Math.Min(cys.Count, cdata.Count));
			for (int i = 0; i < m; i++)
			{
				var pcv = new PipeCoverable(PipeKind.Fluid, cxs[i], cys[i]);
				((ICoverable)pcv).LoadCovers(cdata[i]);
				_sides[(cxs[i], cys[i])] = pcv;
			}
		}

		// Every cell needs a PipeCoverable so per-cell tick state is available.
		foreach (var kv in Pipes.All)
		{
			var pos = kv.Key;
			if (!_sides.ContainsKey(pos))
				_sides[pos] = new PipeCoverable(PipeKind.Fluid, pos.x, pos.y);
		}

		if (tag.ContainsKey("fluid_pipes.states.xs"))
		{
			var sxs   = tag.GetList<int>         ("fluid_pipes.states.xs");
			var sys   = tag.GetList<int>         ("fluid_pipes.states.ys");
			var sdata = tag.GetList<TagCompound> ("fluid_pipes.states.data");
			int k = System.Math.Min(sxs.Count, System.Math.Min(sys.Count, sdata.Count));
			for (int i = 0; i < k; i++)
			{
				if (!Pipes.Has(sxs[i], sys[i])) continue;   // orphan blob - skip
				var state = EnsureState(sxs[i], sys[i]);
				state.LoadFrom(sdata[i]);
			}
		}
	}
}
