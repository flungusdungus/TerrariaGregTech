#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.Common.Materials;
using GregTechCEuTerraria.Api.Data.Chemical.Material;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Worldgen;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Items.Cables;
using GregTechCEuTerraria.TerrariaCompat.Items.Machines;
using GregTechCEuTerraria.TerrariaCompat.Tiles;
using GregTechCEuTerraria.TerrariaCompat.Tiles.Machines;
using System;
using System.Reflection;
using Terraria.ModLoader;

namespace GregTechCEuTerraria;

public sealed class GregTechCEuTerraria : Mod
{
	// tML's UILoadMods.SubProgressText setter is internal - reach it once via
	// reflection so the loading screen shows "GregTechCEuTerraria: <stage>"
	// like MagicStorage does (it routes through SerousCommonLib, we route
	// direct). Cached as a delegate so per-stage calls are a single Invoke.
	private static readonly Action<string>? _setSubProgress = ResolveSubProgressSetter();

	private static Action<string>? ResolveSubProgressSetter()
	{
		try
		{
			var asm = typeof(Mod).Assembly;
			var interfaceType = asm.GetType("Terraria.ModLoader.UI.Interface");
			var field = interfaceType?.GetField("loadMods", BindingFlags.NonPublic | BindingFlags.Static);
			var loadMods = field?.GetValue(null);
			if (loadMods == null) return null;
			var prop = loadMods.GetType().GetProperty("SubProgressText", BindingFlags.Public | BindingFlags.Instance);
			if (prop == null) return null;
			return text => prop.SetValue(loadMods, text);
		}
		catch { return null; }
	}

	private void Stage(string text)
	{
		_setSubProgress?.Invoke("GregTechCEuTerraria: " + text);
		Logger.Info(text);
	}

	public override void Load()
	{
		// Wire the simulation-speed provider before any system that reads
		// it - `TickScale.FromMcTicks` is the upstream-tick-to-Terraria-tick
		// translator used by pipe transfer cadences and (eventually) other
		// upstream-MC-tick-based systems. The provider is a delegate so the
		// Api namespace doesn't take a hard dependency on the Config namespace.
		Api.TickScale.SimulationSpeedProvider = () => Config.GTConfig.Instance?.SimulationSpeed ?? 1.0f;

		Stage("Loading materials");
		TerrariaCompat.Materials.MaterialJsonLoader.Load(this);
		// Parse the upstream item-registry dump once - the single source of
		// item identity. WireItemRegistry / MaterialItemRegistry / Registry
		// ItemLoader all enumerate its entries instead of synthesising ids.
		Stage("Parsing registry dump");
		TerrariaCompat.Items.Registry.RegistryDump.Load(this);
		// Fluids register straight after materials so material.FluidProperty
		// is populated before any item / recipe loader needs it. Mirrors
		// upstream's MaterialRegistryEvent ordering - fluids land between
		// material data ingest and item registration.
		Stage("Registering fluids");
		TerrariaCompat.Loaders.FluidLoader.RegisterAll(this);
		// One bucket item per registered fluid - must run after FluidLoader so
		// FluidRegistry is fully populated.
		Stage("Registering fluid buckets");
		TerrariaCompat.Items.Fluids.FluidBucketRegistry.Register(this);
		// Block tiles register BEFORE items so MaterialItem.SetDefaults can
		// look up the matching tile type when wiring DefaultToPlaceableTile
		// for the block-prefix items.
		Stage("Registering material block tiles");
		TerrariaCompat.Tiles.MaterialBlockTileRegistry.Register(this);
		Stage("Registering material items");
		MaterialItemRegistry.Register(this);
		Stage("Registering ore tiles");
		OreTileRegistry.Register(this);
		Stage("Loading ore veins");
		VeinJsonLoader.Load(this);
		// Material-keyed wires (per upstream cable_blocks) - replaces the old
		// per-VoltageTier CableItemRegistry, which had no material identity
		// or amperage support.
		Stage("Registering wires & cables");
		WireItemRegistry.Register(this);
		// Material-keyed pipes - the pipe counterpart of WireItemRegistry,
		// dump-driven from the MaterialPipeBlockItem `pipe*` entries. Crafting
		// resources only for now (no placement / transport - see PipeItem).
		Stage("Registering pipes");
		TerrariaCompat.Items.Pipes.PipeItemRegistry.Register(this);

		// Rechargeable batteries - one ItemType per upstream battery,
		// materialised from the registry dump (ComponentItem entries with a
		// dischargeable ElectricStats). Charger slots on every
		// TieredEnergyMachine accept these and equalize their stored EU with
		// the machine's internal buffer each tick.
		Stage("Registering batteries");
		TerrariaCompat.Items.Batteries.BatteryItemLoader.Register(this);

		// Item magnets - chargeable electric ComponentItems with a pull behavior.
		// Must run before RegistryItemLoader so it claims the magnet ids instead
		// of letting them fall through to inert RegistryItems.
		Stage("Registering item magnets");
		TerrariaCompat.Items.Magnets.MagnetItemLoader.Register(this);

		// GregTech tools - one ToolItem per (material x GTToolType), generated
		// from each material's ToolProperty (materials.json `tool.types`), the
		// same way upstream GTMaterialItems.generateTools() walks the registry.
		// Must run after MaterialJsonLoader (needs MaterialRegistry populated).
		Stage("Registering GregTech tools");
		TerrariaCompat.Items.Tools.ToolItemLoader.Register(this);

		// "Gregith" - custom Zenith-clone ultimate weapon, one per metallic
		// material with a full non-electric toolset. Recipes consume one of
		// every non-electric tool of that material at a vanilla workbench.
		// Must run AFTER ToolItemLoader so the recipe ingredient ItemIDs exist.
		Stage("Registering Gregith weapons");
		TerrariaCompat.Items.Tools.GregithItemLoader.Register(this);

		// GregTech power armor - nano (HV) / quark (IV) x helmet/chest/legs.
		// Chargeable IElectricItem pieces; visible via reused vanilla equip
		// slots. Must run before RegistryItemLoader so the resolver + TryFind
		// dedup see the armor ids first.
		Stage("Registering power armor");
		TerrariaCompat.Items.Armor.ArmorItemLoader.Register(this);

		// Standing item pipeline - every inert GregTech item (circuits / SMD /
		// components / wafers / molds / clay-brick intermediates / boules / ...)
		// materialised from the authoritative registry dump
		// (Data/Registry/items.json, produced by `./gradlew runData` +
		// snapshot-registry.py). Resolver routes `gtceu:<id>` refs through
		// RegistryItemLoader.TryGet.
		Stage("Registering inert components from dump");
		TerrariaCompat.Items.Registry.RegistryItemLoader.Load(this);

		// Item tags from the same registry dump (gtceu:circuits/<tier>,
		// batteries, components, ...). Resolver expands `#tag` recipe refs to
		// member item types - must load before RecipeJsonLoader.
		Stage("Registering item tags");
		TerrariaCompat.Items.Registry.RegistryTagLoader.Load(this);

		// Covers - register CoverDefinitions into CoverRegistry, then
		// materialise the cover items (ComponentItem dump entries carrying a
		// `cover` field) so they bind item -> CoverDefinition.
		Stage("Registering covers");
		TerrariaCompat.Cover.GTCovers.Register();
		TerrariaCompat.Items.Covers.CoverItemLoader.Register(this);
		// Filter ITEM -> filter loader map - needs the cover item ItemIDs above.
		TerrariaCompat.Cover.GTCovers.RegisterFilterItems();

		// Fluid cells - 7 tiered ItemIDs (fluid_cell, universal_fluid_cell,
		// and 5 material-keyed large cells). NBT carries optional fluid
		// contents. Resolver routes `gtceu:*_fluid_cell` upstream ids here.
		Stage("Registering fluid cells");
		TerrariaCompat.Items.Fluids.FluidCellRegistry.Register(this);

		// === Machine registration - definition-driven (the entity-collapse) ==
		// Every machine is a MachineDefinition data row, not a C# subclass. The
		// ~8 behavioral-family ENTITY classes (WorkableTieredMachine,
		// SimpleGeneratorMachine, SteamSolidBoilerMachine, BatteryBufferMachine,
		// TransformerMachine, SolarPanel/Lamp/SuperTank) autoload - one
		// registered tile-entity type each, well under tML's byte-truncated
		// 256-type network ceiling. TieredMachineFactory registers the
		// per-(machine x tier) TILE + ITEM (ushort/int ids - no 256 cap).
		//
		// MUST run BEFORE RecipeJsonLoader: a recipe's ItemStackIngredient
		// resolves its upstream id to a Terraria item type eagerly at parse
		// time (via Mod.TryFind<ModItem>). Machine items are dump-class
		// MetaMachineItem - RegistryItemLoader skips that class, so the ONLY
		// place they register is here. If recipes parsed first, every machine
		// reference (gtceu:lv_macerator, gtceu:mv_transformer_2a, ...) would
		// resolve to 0 and show unresolved in the recipe browser.
		Stage("Building machine definitions");
		MachineDefinitions.RegisterAll();
		// Usage tooltips for the GUI-less long-distance pipeline endpoints. The
		// universal TieredMachineItem reads MachineTooltip.<id>_N from locale;
		// port-locale.py would emit these, but it needs runData - register the
		// English fallbacks here so the endpoint items always explain themselves.
		TerrariaCompat.Pipelike.LongDistance.LongDistanceLocale.RegisterAll();
		// Install English fallbacks for the `gtceu.multiblock.*` keys
		// `MultiblockDisplayText` (verbatim port of upstream's display-text
		// builder) consults. Raw upstream namespace - port-locale.py doesn't
		// own it, so we register at runtime via `Language.GetOrRegister`.
		TerrariaCompat.Machine.Multiblock.MultiblockLocale.RegisterAll();
		Stage("Registering machine tiles & items");
		TieredMachineFactory.RegisterAll(this);

		// Casing blocks - one placeable 2x2 CasingTile + CasingItem per gtceu
		// cube BlockItem in the dump (logic-less casings / multiblock parts /
		// decoration). Runs last among item registries so its duplicate-id
		// guard sees everything already registered; BEFORE RecipeJsonLoader so
		// `gtceu:<casing>` recipe refs resolve.
		Stage("Registering casing blocks");
		TerrariaCompat.Tiles.Casings.CasingRegistry.Register(this);

		// Turbine rotors - per-material multi-instance ModItem (one ItemID per
		// material with a ROTOR property). Also installs the NBT-aware item
		// resolver hook (`NBTPredicateIngredient.ResolveItemTypeFromNbt`) so
		// recipe ingredients referencing `gtceu:turbine_rotor` with an
		// `nbt: {GT.PartStats:{Material:X}}` payload land on the X-specific
		// ItemID at parse time. MUST run BEFORE RecipeJsonLoader.
		Stage("Registering turbine rotors");
		TerrariaCompat.Items.TurbineRotorItemLoader.Register(this);

		// Recipes must load AFTER MaterialItemRegistry - VanillaCraftingBridge
		// resolves (material, prefix) refs against it at AddRecipes time - and
		// AFTER machine registration (see above).
		Stage("Loading recipes (~32k)");
		RecipeJsonLoader.Load(this, TerrariaCompat.Recipes.IngredientResolverImpl.Instance);

		// Browser-display rows for the world-I/O multis (`large_miner` /
		// `fluid_drilling_rig`). Synthesizes one GTRecipe per biome per station
		// so the player can R-press to see "Underworld -> lava" without owning
		// a built rig. The controllers run their own OnTick - these recipes
		// are never executed.
		Stage("Synthesising biome world-I/O recipes");
		TerrariaCompat.Machine.Multiblock.Electric.BiomeWorldIORecipeSynth.Register(this);

		// Load-time sanity check - warns about any recipe-driven machine whose
		// recipe station resolved to zero recipes (a silent GTRecipeType id
		// mismatch, e.g. "combustion" vs upstream's "combustion_generator").
		Stage("Verifying recipe coverage");
		TerrariaCompat.Recipes.RecipeCoverageCheck.Verify(this);

		// Multiblock bags - one MultiblockBagItem per multi (every MachineDefinition
		// with a non-null PatternFactory). MUST run AFTER MachineDefinitions +
		// TieredMachineFactory so the controller items exist when the bag's
		// contents resolver walks them.
		Stage("Registering multiblock bags");
		TerrariaCompat.BossDrops.MultiblockBag.MultiblockBagLoader.Register(this);

		// Boss drop table - resolves the tier-keyed material + component ids
		// against the registry dump once. BossDropGlobalNPC reads the result in
		// ModifyNPCLoot. Must run AFTER MaterialItemRegistry + RegistryItemLoader
		// AND AFTER MultiblockBagLoader (the multiblock-bag boss linking inside
		// Resolve reads the bag item types).
		Stage("Resolving boss drops");
		TerrariaCompat.BossDrops.BossDropRegistry.Resolve(this);

		Stage("Ready");
	}

	// Vanilla crafting bridge moved to VanillaCraftingBridgeSystem (ModSystem)
	// per tML's recommendation - Mod.AddRecipes is obsolete.

	// Multiplayer packet dispatch. Every byte arriving on our ModPacket channel
	// (from Mod.GetPacket() on either side) lands here; NetRouter reads the
	// leading PacketType byte and routes to the right handler. See
	// Common/Net/ for the per-packet implementations.
	public override void HandlePacket(BinaryReader reader, int whoAmI)
	{
		NetRouter.Handle(reader, whoAmI);
	}

	public override void Unload()
	{
		// Dispose every runtime-baked Texture2D first. FNA's GraphicsDevice holds
		// a strong-reference list of all live GraphicsResources (lives in the
		// non-collectible FNA assembly), so an undisposed mod texture pins our
		// whole AssemblyLoadContext on reload - a real contributor to the
		// per-reload memory growth. See RuntimeTextureRegistry.
		RuntimeTextureRegistry.DisposeAll();

		TerrariaCompat.BossDrops.BossDropRegistry.Unload();
		TerrariaCompat.BossDrops.MultiblockBag.MultiblockBagLoader.Unload();
		RecipeRegistry.Clear();
		TerrariaCompat.Items.Tools.ToolItemLoader.Unload();
		TerrariaCompat.Items.Tools.GregithItemLoader.Unload();
		TerrariaCompat.Items.Armor.ArmorItemLoader.Unload();
		TerrariaCompat.Items.TurbineRotorItemLoader.Unload();
		TerrariaCompat.Items.Registry.RegistryItemLoader.Unload();
		TerrariaCompat.Items.Registry.RegistryTagLoader.Unload();
		TerrariaCompat.Items.Covers.CoverItemLoader.Unload();
		TerrariaCompat.Items.Registry.TagMembership.Clear();
		Api.Cover.Filter.FilterItemRegistry.Clear();
		Api.Cover.CoverRegistry.Clear();
		TerrariaCompat.Items.Registry.RegistryDump.Unload();
		WireItemRegistry.Unload();
		TerrariaCompat.Items.Pipes.PipeItemRegistry.Unload();
		VeinRegistry.Clear();
		OreTileRegistry.Unload();
		TerrariaCompat.Tiles.MaterialBlockTileRegistry.Unload();
		MaterialItemRegistry.Unload();
		MaterialRegistry.Clear();
		MachineRegistry.Clear();
	}
}
