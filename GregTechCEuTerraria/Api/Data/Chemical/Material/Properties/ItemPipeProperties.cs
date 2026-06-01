#nullable enable
namespace GregTechCEuTerraria.Api.Data.Chemical.Material.Properties;

// Per-material item-pipe properties - verbatim port of upstream
// com.gregtechceu.gtceu.api.data.chemical.material.properties.ItemPipeProperties.
//
//   Priority     - items take the route with the LOWEST priority sum (lower
//                  = preferred). Per-pipe-size multiplier from ItemPipeType
//                  blows up restrictive pipes' priority so they sort dead
//                  last (x150 / x100 / x75 / x50 vs x1.5 / x1 / x0.75 / x0.5
//                  for the normal variants).
//   TransferRate - items per second; multiplied by 64 / 20 ticks for the
//                  per-tick cap in ItemNetHandler.checkTransferable.
//
// Per-material base values aren't in materials.json yet - DataGenerators.java
// needs to be extended to dump them. Until then the loader will pass a null
// Material.ItemPipe and PipeItem.BuildItemCell falls back to upstream's
// parameterless default `(1, 0.25f)`.
public sealed class ItemPipeProperties
{
	public int Priority { get; init; } = 1;
	public float TransferRate { get; init; } = 0.25f;

	public ItemPipeProperties() { }

	public ItemPipeProperties(int priority, float transferRate)
	{
		Priority = priority;
		TransferRate = transferRate;
	}

	public override bool Equals(object? obj) =>
		obj is ItemPipeProperties o && Priority == o.Priority && TransferRate.Equals(o.TransferRate);

	public override int GetHashCode() => System.HashCode.Combine(Priority, TransferRate);

	public override string ToString() => $"ItemPipeProperties{{priority={Priority}, transferRate={TransferRate}}}";
}
