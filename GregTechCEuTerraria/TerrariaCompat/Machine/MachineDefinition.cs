#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Pattern;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Common.Energy;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// One machine kind as data. Tier is orthogonal (per-entity / per-tile).
// Mirrors upstream MachineDefinition + registerSimpleMachines. Pure data.
public sealed class MachineDefinition
{
	public string        Id     { get; init; } = "";
	public string        Label  { get; init; } = "";
	public MachineFamily Family { get; init; }
	public (int Width, int Height) Size { get; init; } = (2, 2);

	// 1-element for non-tiered (keeps registration uniform).
	public VoltageTier[] Tiers { get; init; } = Array.Empty<VoltageTier>();

	// true -> per-tier id ("lv_macerator"); false -> bare ("wooden_drum").
	public bool Tiered { get; init; } = true;

	// Drum: mB; Crate: slot count. MaterialId -> drum FLUID_PIPE filter + tint.
	public int     Capacity   { get; init; }
	public string? MaterialId { get; init; }

	public GTRecipeType? RecipeType           { get; init; }

	// Multi-mode multis (multi_smelter etc.); supersedes RecipeType when set.
	public GTRecipeType[]? RecipeTypes        { get; init; }

	public int           InputSlotCount       { get; init; }
	public int           OutputSlotCount      { get; init; }
	public int           InputFluidTankCount  { get; init; }
	public int           OutputFluidTankCount { get; init; }
	public bool          UsesCircuit          { get; init; }
	public int?          FluidTankCapacity    { get; init; }   // null = 16_000

	public bool IsHighPressure { get; init; }

	// BatteryBuffer (charger = OutputAmps==0).
	public int  BatterySlotCount { get; init; }
	public long InputAmpsPerItem { get; init; }
	public long OutputAmps       { get; init; }

	// Transformer baseAmp (1/2/4/16).
	public int BaseAmp { get; init; }

	// Part config (read in OnDefinitionBound). PartFluidSlots: 1/4/9.
	public IO?  PartIo         { get; init; }
	public int  PartAmperage   { get; init; }
	public int  PartFluidSlots { get; init; }
	// Factory wires Predicates.Abilities(...) at load.
	public Api.Machine.Multiblock.PartAbility[] PartAbilities { get; init; } =
		System.Array.Empty<Api.Machine.Multiblock.PartAbility>();

	public string? FusedCasingTileName    { get; init; }
	public string? FusedCasingTexturePath { get; init; }
	// MaintenanceHatch: true = HV configurable, false = plain LV.
	public bool PartConfigurable { get; init; }

	// LargeBoiler per-tier (GTMachineUtils.registerLargeBoiler).
	public int BoilerMaxTemperature { get; init; }
	public int BoilerHeatSpeed      { get; init; }
	// CleaningMaintenanceHatch: CLEANROOM->UV, STERILE->UHV.
	public Api.Machine.Multiblock.CleanroomType? PartCleanroomType { get; init; }

	// HPCAComponentPartMachine kind (Empty/Computation/Cooler/Bridge/...).
	public TerrariaCompat.Machine.Multiblock.Part.Hpca.HpcaComponentKind? HpcaKind { get; init; }
	public int  DataAccessSlots   { get; init; }
	public bool DataAccessCreative { get; init; }
	// Optical computation/data hatch: true = transmitter.
	public bool? OpticalTransmitter { get; init; }

	// Layer order back->front: casing -> pipe -> tinted -> directional -> emissive.
	public MachineCasing Casing          { get; init; } = MachineCasing.Voltage;
	// Empty = derive "block/machines/{Id}".
	public string        OverlayDir      { get; init; } = "";
	// Per-tier override (e.g. parallel_hatch_mk{1..4}).
	public System.Func<VoltageTier, string>? OverlayDirByTier { get; init; }
	public string        OverlayBasename { get; init; } = "overlay_front";
	public string        PipeOverlayBasename     { get; init; } = "";
	public string        TintedOverlayBasename   { get; init; } = "";
	public string        EmissiveOverlayBasename { get; init; } = "";

	public string LayoutKey { get; init; } = "generic";

	// Closure so dynamic per-controller patterns + tiered multis work.
	public Func<IBlockPattern>? PatternFactory { get; init; }

	// Standard processing multis - subclasses with hardcoded modifiers (EBF)
	// override GetRecipeModifier directly.
	public Api.Recipe.Modifier.RecipeModifier? MultiRecipeModifier { get; init; }

	// Verbatim MachineDefinition.getAdditionalDisplay - extra status lines
	// (EBF heat, coil discount, ...) after the standard builder.
	public System.Action<MetaMachine, System.Collections.Generic.List<string>>? AdditionalDisplay { get; init; }

	// Forward-compat (matcher flip-pass unimplemented).
	public bool AllowFlip { get; init; } = false;

	// Generator multis flip GetMaxVoltage to read output-side hatches.
	public bool IsGenerator { get; init; } = false;

	// Verbatim MachineDefinition.tooltipBuilder - runtime-formatted lines
	// (slot count, EU/t, capacitor cap, ...).
	public Action<System.Collections.Generic.List<string>, MachineDefinition>? TooltipBuilder { get; init; }

	// Muffler recovery-item factory (slag/ash).
	public Func<Terraria.Item[]>? RecoveryItemsFactory { get; init; }

	public string ResolvedOverlayDir =>
		OverlayDir.Length > 0 ? OverlayDir : $"block/machines/{Id}";

	public string ResolveOverlayDirForTier(VoltageTier tier) =>
		OverlayDirByTier?.Invoke(tier) ?? ResolvedOverlayDir;
}
