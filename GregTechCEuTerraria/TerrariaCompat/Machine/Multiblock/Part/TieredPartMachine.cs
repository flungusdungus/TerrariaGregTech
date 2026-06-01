#nullable enable
using GregTechCEuTerraria.Api.Machine.Feature;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of TieredPartMachine. Tier-carrying part base; ITieredMachine for
// recipe overclock / tier filters. Tier is late-bound (no ctor arg).
public abstract class TieredPartMachine : MultiblockPartMachine, ITieredMachine
{
	// `new` shadow - MetaMachine.Tier is the VoltageTier enum; this is the int.
	public new int Tier { get; protected set; }

	protected TieredPartMachine() : base() { }

	public int GetTier() => Tier;

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["tier"] = Tier;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("tier")) Tier = tag.GetInt("tier");
	}
}
