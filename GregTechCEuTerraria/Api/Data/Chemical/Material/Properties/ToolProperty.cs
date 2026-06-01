#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;

// Port of upstream com.gregtechceu.gtceu.api.data.chemical.material
// .properties.ToolProperty - the per-material stats every tool made from this
// material inherits. Deserialized straight off the `tool` block in
// Data/Materials/materials.json (emitted by the material registry dump).
//
// Upstream-only members dropped in the port:
//   - MaterialToolTier (implements MC's Tier interface) - our ToolItem maps
//     these stats onto Terraria's tool model directly.
//   - the enchantments Object2IntMap + addEnchantmentForTools - Terraria has
//     no enchantment system.
//   - the Builder - materials are dump-driven, never hand-built here.
public sealed class ToolProperty
{
	// Harvest speed of tools made from this material. Upstream default 1.0F.
	public float HarvestSpeed { get; init; } = 1.0f;

	// Attack damage of tools made from this material. Upstream default 1.0F.
	public float AttackDamage { get; init; } = 1.0f;

	// Attack speed (animation time modifier) of tools made from this material.
	// Upstream default 0.0F.
	public float AttackSpeed { get; init; }

	// Durability of tools made from this material. Upstream default 100.
	public int Durability { get; init; } = 100;

	// Harvest level tools of this material can mine. Upstream default 2 (Iron).
	public int HarvestLevel { get; init; } = 2;

	// Vein depth for the prospecting hammer. Upstream default harvestLevel*2+1.
	public int ProspectingDepth { get; init; }

	// Enchantability of tools made from this material. Upstream default 10.
	public int Enchantability { get; init; } = 10;

	// Multiplier applied to base durability. Upstream default 1.
	public int DurabilityMultiplier { get; init; } = 1;

	// Tools made of this material ignore durability entirely.
	public bool Unbreakable { get; init; }

	// Mined blocks go straight into the inventory instead of dropping.
	public bool Magnetic { get; init; }

	// No crafting tools (saw/file/hammer/...) are generated for this material.
	public bool IgnoreCraftingTools { get; init; }

	// GTToolType.name values this material generates a tool for. This is the
	// generation source: ToolItemLoader enumerates (material x type) over it.
	public List<string> Types { get; init; } = new();

	// Verbatim port of ToolProperty.hasType.
	public bool HasType(string toolTypeName) => Types.Contains(toolTypeName);
}
