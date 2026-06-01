#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Microsoft.Xna.Framework;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;

// Solar panel tile - definition-driven like every machine, but with a custom
// per-tier overlay basename. Upstream ships `block/generators/solar/
// solarpanel_<tier>.png` only through UV; UHV+ walk down to the highest tier
// that actually has a texture. A single static MachineDefinition.OverlayBasename
// can't express that, so the solar panel keeps a thin rendering subclass -
// same pattern as TransformerTile.
public class SolarPanelTile : TieredMachineTile
{
	public SolarPanelTile() { }
	public SolarPanelTile(VoltageTier tier, MachineDefinition def) : base(tier, def) { }

	protected override Color MapColor     => new(80, 110, 200);
	protected override int   MineDustType => Terraria.ID.DustID.Glass;

	// Solar panels are SINGLE-LAYER upstream - `solarpanel_<tier>` is the whole
	// face (the definition's Casing is None, no hull under it).
	private string? _overlayBasename;
	public override string OverlayBasename => _overlayBasename ??= ResolveOverlayBasename();

	private string ResolveOverlayBasename()
	{
		const string dir = "GregTechCEuTerraria/Content/Textures/block/generators/solar";
		for (int ti = (int)_tier; ti >= 0; ti--)
		{
			string name = $"solarpanel_{VoltageTiers.Id((VoltageTier)ti)}";
			if (ModContent.HasAsset($"{dir}/{name}")) return name;
		}
		return "solarpanel_lv";
	}
}
