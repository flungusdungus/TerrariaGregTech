#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.Api.Capability;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Sentinel-pattern base for tier-templated ModItems (TieredMachineItem,
// BatteryItem). Subclasses just declare SnakeName.
public abstract class TieredItem : ModItem
{
	protected readonly VoltageTier _tier;
	private readonly bool _isTemplate;

	protected TieredItem() { _isTemplate = true; _tier = VoltageTier.LV; }
	protected TieredItem(VoltageTier tier) { _tier = tier; _isTemplate = false; }

	public override bool IsLoadingEnabled(Mod mod) => !_isTemplate;
	public override string Name => $"{VoltageTiers.Id(_tier)}_{SnakeName}";
	protected override bool CloneNewInstances => true;

	protected abstract string SnakeName { get; }

	public VoltageTier Tier => _tier;
}
