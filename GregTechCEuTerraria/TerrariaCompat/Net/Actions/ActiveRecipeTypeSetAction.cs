#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Server-authoritative recipe-type cycle for a multi-mode multiblock
// controller (large_extractor's EXTRACTOR / CANNER toggle, large_cutter's
// CUTTER / LATHE, multi_smelter's FURNACE / ALLOY_SMELTER, ...).
//
// Adapted from upstream `MachineModeFancyConfigurator.
// setActiveRecipeTypeAndUpdateTickSubs` (lines 66-71). Absolute target (not
// delta) so duplicated packets converge on intent; clamping runs server-side
// inside `SetActiveRecipeType`.
public sealed class ActiveRecipeTypeSetAction : IMachineAction
{
	public PacketType Type => PacketType.ActiveRecipeTypeSet;

	private int _index;

	public ActiveRecipeTypeSetAction() { }
	public ActiveRecipeTypeSetAction(int index) { _index = index; }

	public void Write(BinaryWriter w) => w.Write(_index);
	public void Read (BinaryReader r) => _index = r.ReadInt32();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is WorkableMultiblockMachine multi)
			multi.SetActiveRecipeType(_index);
	}
}
