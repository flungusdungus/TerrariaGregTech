#nullable enable
using GregTechCEuTerraria.Api.Transfer;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of virtualregistry.entries.VirtualTank. Default 160 B = one second of
// 8000 mB/t.
public sealed class VirtualTank : VirtualEntry
{
	public const int DefaultCapacity = 160_000;

	public CustomFluidTank FluidTank { get; } = new(DefaultCapacity);

	public override EnderEntryType Type => EnderEntryType.Fluid;

	public override bool CanRemove() => base.CanRemove() && FluidTank.IsEmpty;

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["tank"] = FluidTank.SerializeNBT();
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("tank")) FluidTank.DeserializeNBT(tag.GetCompound("tank"));
	}
}
