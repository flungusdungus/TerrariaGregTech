#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of OpticalDataHatchMachine. Transmitter side queries co-located data
// hatches while its multi runs; receiver side asks an adjacent optical pipe
// / data hatch. Upstream's PartAbility tag filter maps to type classification
// (non-optical IDataAccessHatch + receiver optical -> dataAccesses; transmitter
// optical -> transmitters).
public class OpticalDataHatchMachine : MultiblockPartMachine, IOpticalDataAccessHatch
{
	protected override string Label => "Optical Data Hatch";

	public bool TransmitterFlag { get; protected set; }

	public OpticalDataHatchMachine() : base() { }

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		Configure(Definition?.OpticalTransmitter ?? false);
	}

	public void Configure(bool isTransmitter) => TransmitterFlag = isTransmitter;

	public bool IsTransmitter() => TransmitterFlag;
	public bool IsCreative()    => false;
	public bool CanShared() => false;

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		lines.Add(TransmitterFlag
			? "[c/55AAFF:Data Transmitter] - serves this multi's research data to the optical network (e.g. a Data Bank -> Assembly Line)"
			: "[c/FFAA55:Data Receiver] - pulls research data from a remote Data Bank over the optical network");

		// Diagnostic chain trace (TerrariaCompat aid; remote multi out of sync
		// range on a true MP client reads as 0).
		if (!IsFormed())
		{
			lines.Add("[c/AAAAAA:Research link:] [c/FF5555:not part of a formed multi]");
			return;
		}

		if (TransmitterFlag)
		{
			bool working = IsSourceMultiWorking();
			lines.Add(working
				? "[c/55FF55:Source multi: providing]"
				: "[c/FF5555:Source multi: NOT providing] (idle / unpowered -> serves nothing)");
			lines.Add($"[c/AAAAAA:Research entries served:] {FmtResearchCount(CountVisibleResearch(new HashSet<IDataAccessHatch>()))}");
		}
		else
		{
			DescribeReceiverLink(lines);
			lines.Add($"[c/AAAAAA:Research entries visible:] {FmtResearchCount(CountVisibleResearch(new HashSet<IDataAccessHatch>()))}");
		}
	}

	// Same gate as transmitter's IsRecipeAvailable. Data Bank only reports
	// working when powered (verbatim upstream tick()).
	public bool IsSourceMultiWorking()
	{
		foreach (var c in GetControllers())
			return c is IWorkableMultiController w && w.GetRecipeLogic().IsWorking();
		return false;
	}

	// Surfaces the first break in the wiring -> source chain (per perimeter side).
	private void DescribeReceiverLink(System.Collections.Generic.List<string> lines)
	{
		bool anySource = false;
		foreach (var (side, x, y) in WorldCapability.Perimeter(this))
		{
			if (TerrariaCompat.Pipelike.Optical.OpticalPipeLayerSystem.Pipes.Has(x, y))
			{
				anySource = true;
				var net = TerrariaCompat.Pipelike.Optical.OpticalPipeNetSystem.Level.GetNetFromPos((x, y));
				if (net == null) { lines.Add($"  [c/AAAAAA:{side}:] optical pipe, [c/FF5555:no net]"); continue; }
				var handler = new TerrariaCompat.Pipelike.Optical.OpticalNetHandler(net, (x, y), side.Opposite());
				var remote = handler.ResolveRemoteDataHatch();
				if (remote == null)
					lines.Add($"  [c/AAAAAA:{side}:] pipe net, [c/FF5555:no source hatch on the other end]");
				else if (!remote.IsTransmitter())
					lines.Add($"  [c/AAAAAA:{side}:] remote is a [c/FF5555:receiver, not a transmitter]");
				else
				{
					bool srcWorking = remote is OpticalDataHatchMachine odm && odm.IsSourceMultiWorking();
					lines.Add(srcWorking
						? $"  [c/AAAAAA:{side}:] [c/55FF55:transmitter, source providing]"
						: $"  [c/AAAAAA:{side}:] transmitter found, [c/FF5555:source not providing]");
				}
				continue;
			}
			var neighbour = WorldCapability.Get<IDataAccessHatch>(x, y);
			if (neighbour != null)
			{
				anySource = true;
				lines.Add($"  [c/AAAAAA:{side}:] [c/55FF55:adjacent data hatch]");
			}
		}
		if (!anySource)
			lines.Add("  [c/FF5555:no optical pipe or data hatch adjacent]");
	}

	private static int SatAdd(int a, int b) =>
		(a == int.MaxValue || b == int.MaxValue) ? int.MaxValue : a + b;

	private static string FmtResearchCount(int n) =>
		n == int.MaxValue ? "all (creative)" : n == 0 ? "[c/FF5555:none]" : n.ToString();

	// Mirrors IsRecipeAvailable's traversal but sums entries.
	public int CountVisibleResearch(ICollection<IDataAccessHatch> seen)
	{
		if (seen.Contains(this)) return 0;
		seen.Add(this);
		if (!IsFormed()) return 0;

		if (IsTransmitter())
		{
			MultiblockControllerMachine? controller = null;
			foreach (var c in GetControllers()) { controller = c; break; }
			if (controller is not IWorkableMultiController workable || !workable.GetRecipeLogic().IsWorking())
				return 0;
			int total = 0;
			foreach (var part in controller.GetParts())
				if (part is IDataAccessHatch h && !seen.Contains(h))
					total = SatAdd(total, h.CountVisibleResearch(seen));
			return total;
		}

		int sum = 0;
		foreach (var (side, x, y) in WorldCapability.Perimeter(this))
		{
			if (TerrariaCompat.Pipelike.Optical.OpticalPipeLayerSystem.Pipes.Has(x, y))
			{
				var net = TerrariaCompat.Pipelike.Optical.OpticalPipeNetSystem.Level.GetNetFromPos((x, y));
				if (net != null)
					sum = SatAdd(sum, new TerrariaCompat.Pipelike.Optical.OpticalNetHandler(net, (x, y), side.Opposite()).CountVisibleResearch(seen));
				continue;
			}
			var neighbour = WorldCapability.Get<IDataAccessHatch>(x, y);
			if (neighbour != null && !seen.Contains(neighbour))
				sum = SatAdd(sum, neighbour.CountVisibleResearch(seen));
		}
		return sum;
	}

	// MUST inline the DIM body - NEVER `((IDataAccessHatch)this).ModifyRecipe`.
	// This override shadows the DIM (same signature on inherited virtual), so an
	// interface-cast call resolves back to this method = infinite tail-call
	// self-recursion -> silent server hang. Same trap as DataAccessHatchMachine.
	public override GTRecipe? ModifyRecipe(GTRecipe recipe)
	{
		if (IsCreative()) return recipe;
		if (IsRecipeAvailable(recipe, new HashSet<IDataAccessHatch> { this })) return recipe;
		return null;
	}

	public bool IsRecipeAvailable(GTRecipe recipe, ICollection<IDataAccessHatch> seen)
	{
		seen.Add(this);
		if (!IsFormed()) return false;

		if (IsTransmitter())
		{
			MultiblockControllerMachine? controller = null;
			foreach (var c in GetControllers()) { controller = c; break; }
			if (controller is not IWorkableMultiController workable
				|| !workable.GetRecipeLogic().IsWorking())
				return false;

			// Type classification stands in for upstream's PartAbility tag filter.
			var dataAccesses = new List<IDataAccessHatch>();
			var transmitters = new List<IDataAccessHatch>();
			foreach (var part in controller.GetParts())
			{
				if (part is IOpticalDataAccessHatch optical)
				{
					if (optical.IsTransmitter()) transmitters.Add(optical);
					else                          dataAccesses.Add(optical);
				}
				else if (part is IDataAccessHatch hatch)
				{
					dataAccesses.Add(hatch);
				}
			}

			return IsRecipeAvailableIn(dataAccesses, seen, recipe)
				|| IsRecipeAvailableIn(transmitters, seen, recipe);
		}

		// Receiver: (a) optical pipe -> walk the net; (b) direct adjacent
		// IDataAccessHatch (upstream looks for OpticalPipeBlockEntity only -
		// we generalise so other adjacent multi parts satisfy the same role).
		foreach (var (side, x, y) in WorldCapability.Perimeter(this))
		{
			if (TerrariaCompat.Pipelike.Optical.OpticalPipeLayerSystem.Pipes.Has(x, y))
			{
				var net = TerrariaCompat.Pipelike.Optical.OpticalPipeNetSystem.Level.GetNetFromPos((x, y));
				if (net != null)
				{
					var handler = new TerrariaCompat.Pipelike.Optical.OpticalNetHandler(net, (x, y), side.Opposite());
					if (!seen.Contains(handler) && handler.IsRecipeAvailable(recipe, seen))
						return true;
				}
				continue;
			}
			var neighbour = WorldCapability.Get<IDataAccessHatch>(x, y);
			if (neighbour != null && !seen.Contains(neighbour)
				&& neighbour.IsRecipeAvailable(recipe, seen))
				return true;
		}
		return false;
	}

	private static bool IsRecipeAvailableIn(IEnumerable<IDataAccessHatch> hatches,
		ICollection<IDataAccessHatch> seen, GTRecipe recipe)
	{
		foreach (var hatch in hatches)
		{
			if (seen.Contains(hatch)) continue;
			if (hatch.IsRecipeAvailable(recipe, seen)) return true;
		}
		return false;
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["transmitter"] = TransmitterFlag;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		TransmitterFlag = tag.GetBool("transmitter");
	}
}
