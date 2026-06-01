#nullable enable
using GregTechCEuTerraria.Api.Machine.Trait;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Common.Machine.Trait.Hpca;

// Port of com.gregtechceu.gtceu.common.machine.trait.hpca.HPCAComponentTrait.
//
// Base trait carried by every HPCA grid component part (empty / computation /
// heat-sink / active-cooler / bridge). Holds the per-component EU upkeep + max
// draw, whether it can take heat damage, whether it enables HPCA bridging, and
// the damaged flag.
//
// Documented adaptations:
//   - `@SaveField @SyncToClient @RerenderOnChanged isDamaged` -> plain field
//     persisted in Save/Load; the part's MachineStateSyncPacket round-trip
//     carries it to clients (same path every other part field uses).
//   - `setActive`/`MachineRenderState` (block-state render property) ->
//     `IsActive` bool the part renderer reads for the active overlay.
//   - `getRenderState` / `GTMachineModelProperties` DROPPED (no 3D model
//     property system; the 2D renderer reads IsDamaged / IsActive directly).
//   - `getTraitType()` returns the BASE TYPE for all subclasses (verbatim -
//     upstream's subclasses inherit getTraitType), so the controller can
//     gather every component under one type. (We gather via the part class,
//     but the shared TYPE is kept for parity.)
public class HPCAComponentTrait : MachineTrait
{
	public static readonly MachineTraitType<HPCAComponentTrait> TYPE = new(allowMultipleInstances: false);
	public override MachineTraitType TraitType => TYPE;

	public int  UpkeepEUt     { get; }
	public int  MaxEUt        { get; }
	public bool CanBeDamaged  { get; }
	public bool AllowBridging { get; }

	public bool IsDamaged { get; private set; }

	// Render-only active flag (upstream's IS_ACTIVE render-state property).
	public bool IsActive { get; private set; }

	public HPCAComponentTrait(int upkeepEUt, int maxEUt, bool canBeDamaged, bool allowBridging)
	{
		UpkeepEUt     = upkeepEUt;
		MaxEUt        = maxEUt;
		CanBeDamaged  = canBeDamaged;
		IsDamaged     = false;
		AllowBridging = allowBridging;
	}

	public void SetDamaged(bool damaged)
	{
		if (!CanBeDamaged) return;
		if (IsDamaged != damaged)
			IsDamaged = damaged;
	}

	public void SetActive(bool active) => IsActive = active;

	public override void Save(TagCompound tag)
	{
		tag["damaged"] = IsDamaged;
	}

	public override void Load(TagCompound tag)
	{
		IsDamaged = tag.GetBool("damaged");
	}
}
