#nullable enable
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.UI;
using GregTechCEuTerraria.TerrariaCompat.UI.Layouts;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Universal definition-driven machine tile. One class, registered per
// (MachineDefinition x VoltageTier) by TieredMachineFactory. Parameterless
// ctor is the autoload probe (gated off via IsLoadingEnabled).
public class TieredMachineTile : MetaMachineTile
{
	protected readonly VoltageTier _tier;
	protected readonly MachineDefinition? _def;
	private readonly bool _isTemplate;

	public TieredMachineTile() { _isTemplate = true; _tier = VoltageTier.LV; }
	public TieredMachineTile(VoltageTier tier, MachineDefinition def)
	{
		_tier = tier;
		_def = def;
		_isTemplate = false;
	}

	public override MachineDefinition Definition => _def!;

	public override bool IsLoadingEnabled(Mod mod) => !_isTemplate;
	// Null-guarded for the autoload-probe instance.
	public override string Name => _def == null
		? GetType().Name
		: _def.Tiered ? $"{VoltageTiers.Id(_tier)}_{_def.Id}" : _def.Id;
	// PreDraw composites the real sheet; this PNG is never actually drawn.
	public override string Texture => "GregTechCEuTerraria/Content/TerrariaCompat/TooManyItemsTile";

	protected virtual Color MapColor => new Color(140, 130, 110);

	public override void SetStaticDefaults()
	{
		ApplyDefaults();
		TileObjectData.addTile(Type);

		// BindDefinition's tile-type backstop.
		MachineRegistry.MapTile(Type, _def!.Id, _tier);

		AddMapEntry(MapColor,
			Language.GetOrRegister($"Mods.GregTechCEuTerraria.Tiles.{Name}.MapEntry",
				() => _def!.Tiered ? $"{VoltageTiers.ShortName(_tier)} {_def.Label}" : _def.Label));

		DustType   = MineDustType;
		HitSound   = Terraria.ID.SoundID.Tink;
		MineResist = 1.5f;
	}

	protected virtual int MineDustType => Terraria.ID.DustID.Stone;

	public override MachineRenderer.Casing CasingKind => MapCasing(_def!.Casing);
	public override string OverlayDir              => _def!.ResolveOverlayDirForTier(_tier);
	public override string OverlayBasename         => _def!.OverlayBasename;
	public override string PipeOverlayBasename     => _def!.PipeOverlayBasename;
	public override string TintedOverlayBasename   => _def!.TintedOverlayBasename;
	public override string EmissiveOverlayBasename => _def!.EmissiveOverlayBasename;
	protected override VoltageTier TileTier => _tier;

	// Multi appearance-casing - matches upstream workableCasingModel(appearance, overlay).
	public override string? CustomCasingTexturePath
	{
		get
		{
			var name = _def?.FusedCasingTileName;
			if (string.IsNullOrEmpty(name)) return null;
			// Mod.TryFind<CasingTile> returns false here; use base ModTile +
			// TileLoader.GetTile cast (same as MultiblockControllerMachine.FusedCasingTexture).
			var mod = Terraria.ModLoader.ModLoader.GetMod("GregTechCEuTerraria");
			if (!mod.TryFind<Terraria.ModLoader.ModTile>(name, out var tile)) return null;
			return Terraria.ModLoader.TileLoader.GetTile(tile.Type) is Tiles.Casings.CasingTile casing
				? casing.BlockTexture : null;
		}
	}

	internal static MachineRenderer.Casing MapCasing(MachineCasing c) => c switch
	{
		MachineCasing.Voltage       => MachineRenderer.Casing.Voltage,
		MachineCasing.BrickedBronze => MachineRenderer.Casing.BrickedBronze,
		MachineCasing.BrickedSteel  => MachineRenderer.Casing.BrickedSteel,
		MachineCasing.CokeBricks    => MachineRenderer.Casing.CokeBricks,
		MachineCasing.Firebricks    => MachineRenderer.Casing.Firebricks,
		MachineCasing.PumpDeck      => MachineRenderer.Casing.PumpDeck,
		_                           => MachineRenderer.Casing.None,
	};

	// LayoutKey "none" = no GUI (transformer / solar / lamp).
	public override bool RightClick(int i, int j)
	{
		// Terminal auto-build takes priority over the GUI.
		if (MachineCellResolver.TryFindMachineAt(i, j, out var clicked)
		    && Items.TerminalItem.TryAutoBuild(clicked, Main.LocalPlayer))
			return true;

		// LD endpoint screwdriver IO flip (GUI-less, LayoutKey "none").
		if (clicked is Pipelike.LongDistance.LongDistanceEndpointMachine ep
		    && IsScrewdriver(Main.LocalPlayer.HeldItem))
		{
			if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
				Net.LdEndpointTogglePacket.SendRequest(ep.Position.X, ep.Position.Y);
			else
				Net.LdEndpointTogglePacket.Apply(ep);
			return true;
		}

		if (_def!.LayoutKey == "none") return false;
		if (!MachineCellResolver.TryFindMachineAt(i, j, out var machine)) return false;
		var layout = MachineLayoutRegistry.Build(machine);
		if (layout is null) return false;
		MachineUISystem.OpenFor(machine, layout);
		return true;
	}

	private static bool IsScrewdriver(Item item)
	{
		if (item?.ModItem is not Items.Tools.ToolItem tool) return false;
		string n = tool.ToolType.Name;
		return n == "screwdriver" || n.EndsWith("_screwdriver");
	}
}
