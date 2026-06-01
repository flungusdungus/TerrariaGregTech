#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Items.Machines;
using GregTechCEuTerraria.TerrariaCompat.Items.Machines.Transformers;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Transformers;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Registers Tile + Item per (MachineDefinition x tier).
// Behavioral entity classes autoload separately (~8, well under tML's 256
// byte-truncated network ceiling). Tile/item ids are ushort/int = no 256 cap.
// MachineDefinitions.RegisterAll MUST run first.
public static class TieredMachineFactory
{
	public static void RegisterAll(Mod mod)
	{
		foreach (var def in MachineRegistry.All)
		{
			foreach (var tier in def.Tiers)
			{
				ModTile tile = def.Family switch
				{
					MachineFamily.Transformer => new TransformerTile(tier, def),
					MachineFamily.SolarPanel  => new SolarPanelTile(tier, def),
					MachineFamily.SuperTank   => new SuperTankTile(tier, def),
					MachineFamily.SuperChest  => new SuperChestTile(tier, def),
					MachineFamily.Drum        => new DrumTile(tier, def),
					MachineFamily.Crate       => new CrateTile(tier, def),
					_                         => new TieredMachineTile(tier, def),
				};
				mod.AddContent(tile);

				ModItem item = def.Family switch
				{
					MachineFamily.Transformer => new TransformerItem(tier, def),
					MachineFamily.SuperTank   => new SuperTankItem(tier, def),
					MachineFamily.SuperChest  => new SuperChestItem(tier, def),
					MachineFamily.Drum        => new DrumItem(tier, def),
					MachineFamily.Crate       => new CrateItem(tier, def),
					_                         => new TieredMachineItem(tier, def),
				};
				mod.AddContent(item);

				// Multi patterns resolve via Predicates.Abilities(PartAbility.X);
				// no per-multi hand-collection of tile lists needed.
				if (def.PartAbilities.Length > 0)
				{
					ushort tileType = (ushort)tile.Type;
					foreach (var ab in def.PartAbilities)
						ab.Register((int)tier, tileType);
				}
			}
		}
	}
}
