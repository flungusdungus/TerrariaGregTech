#nullable enable
using GregTechCEuTerraria.Api.Block;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

// Verbatim port of CoilWorkableElectricMultiblockMachine.
// WorkableElectricMultiblockMachine + coil-tier readout. On form, reads
// "CoilType" from the match context (populated by Predicates.HeatingCoils)
// for GTRecipeModifiers.ebfOverclock to consume. Falls through to
// CUPRONICKEL when HeatingCoils stub doesn't match.
//
// Concrete (was abstract) - standard coil multis (multi_smelter,
// pyrolyse_oven, cracker, alloy_blast_smelter, mega_blast_furnace) share
// this entity via MachineFamily.MultiblockCoilStandard.
public class CoilWorkableElectricMultiblockMachine : WorkableElectricMultiblockMachine
{
	public ICoilType CoilType { get; private set; } = DefaultCoilType.CUPRONICKEL;

	public CoilWorkableElectricMultiblockMachine() : base() { }

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		var type = GetMultiblockState().MatchContext.Get<object>("CoilType");
		if (type is ICoilType coil)
			CoilType = coil;
	}

	public int GetCoilTier() => CoilType.Tier;

	// SaveData required so CoilType rides MachineStateSyncPacket to MP clients
	// (display-driving field). Stored as lowercase name; CoilType.GetByName
	// re-binds on load.
	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["coilName"] = CoilType.Name;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		if (!tag.ContainsKey("coilName")) return;
		string name = tag.GetString("coilName");
		var resolved = Api.Block.CoilType.GetByName(name);
		if (resolved is not null) CoilType = resolved;
	}
}
