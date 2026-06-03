#nullable enable
using GregTechCEuTerraria.Common.Energy;
using System;
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Api.Blockentity;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Capabilities.Handlers;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Base for any in-world machine. Mirrors upstream MetaMachine + TieredMachine
// flattened (we don't need MC's trait/capability layering). Lifecycle:
// MetaMachineTile.PlaceInWorld -> Place(i,j); KillMultiTile -> entity removal +
// OnKill().
public abstract class MetaMachine : ModTileEntity, ITickSubscription, ICoverable, IFancyTooltip, ISidedCapabilityProvider
{
	// tML Activator-builds entities via the parameterless ctor (losing the
	// prototype tier). _persistedTier is the per-instance source of truth -
	// stamped by OverrideIdentity on fresh placement, restored from save tag
	// or tile backstop on load.
	private Common.Energy.VoltageTier? _persistedTier;
	protected Common.Energy.VoltageTier ResolveTier(Common.Energy.VoltageTier fallback) =>
		_persistedTier ?? fallback;

	// Data-driven identity (MachineDefinition row). Entity-collapse target:
	// ONE ModTileEntity per behavioral family (~8 total) keeps registered
	// type count well under tML's byte-truncated 256 network ceiling.
	// Identity recovers from _persistedMdefId (placement / save) or the
	// tile backstop via MachineRegistry.TryResolveTile.
	private string? _persistedMdefId;
	private MachineDefinition? _definition;

	public MachineDefinition? Definition => _definition;

	// Idempotent; safe to call from any Ensure* hook.
	public void BindDefinition()
	{
		if (_definition != null) return;
		string? id = _persistedMdefId;
		if (id == null && Position.X > 0 && Position.X < Main.maxTilesX
		    && Position.Y > 0 && Position.Y < Main.maxTilesY)
		{
			int tileType = Main.tile[Position.X, Position.Y].TileType;
			if (MachineRegistry.TryResolveTile(tileType, out var rid, out var rtier))
			{
				id = rid;
				_persistedMdefId = rid;
				_persistedTier ??= rtier;
			}
		}
		if (id != null && MachineRegistry.TryGet(id, out var def))
		{
			_definition = def;
			OnDefinitionBound();
		}
	}

	// Authoritative identity stamp; carried into save/net blob by SaveData.
	internal void OverrideIdentity(string mdefId, Common.Energy.VoltageTier tier)
	{
		_persistedMdefId = mdefId;
		_persistedTier   = tier;
		_definition      = MachineRegistry.TryGet(mdefId, out var def) ? def : null;
		if (_definition != null) OnDefinitionBound();
	}

	// Fires once when _definition becomes non-null. Part entities override
	// to copy PartIo / PartAmperage / PartFluidSlots and Configure().
	// LoadData re-applies these on reload, so this only matters for the
	// fresh-placement path.
	protected virtual void OnDefinitionBound() { }

	// Per-(machine x tier) id like "lv_macerator"; distinct from Name (the
	// family class name). Drives tile / item / locale binding.
	public string? MachineId => _definition?.Id;
	public virtual string MachineKey => MachineId != null
		? (_definition!.Tiered
			? $"{Common.Energy.VoltageTiers.Id(Tier)}_{MachineId}"
			: MachineId)
		: GetType().Name;

	// Activator-ctor fallback; real tier is _persistedTier via Tier.
	protected readonly Common.Energy.VoltageTier _tier;

	protected MetaMachine()
	{
		_tier = Common.Energy.VoltageTier.LV;
		Traits = new MachineTraitHolder(this);
	}

	// Anchor-cell only - non-anchor tiles of a 2x2 machine return null.
	public static MetaMachine? GetMachineAt(int x, int y)
	{
		var pos = new Terraria.DataStructures.Point16(x, y);
		if (Terraria.DataStructures.TileEntity.ByPosition.TryGetValue(pos, out var te) && te is MetaMachine machine)
			return machine;
		return null;
	}

	protected MetaMachine(Common.Energy.VoltageTier tier)
	{
		_tier = tier;
		Traits = new MachineTraitHolder(this);
	}

	// Concrete machines attach traits via Ensure* hooks; lifecycle forwarded
	// from SystemTick / OnKill / SaveData / LoadData.
	public MachineTraitHolder Traits { get; }

	protected abstract string Label { get; }

	// Family class name (~8 of them post entity-collapse); per-(machine x tier)
	// id is MachineKey.
	public override string Name => GetType().Name;

	public virtual Common.Energy.VoltageTier Tier => ResolveTier(_tier);
	public virtual string DisplayName => Definition == null
		? Label
		: Definition.Tiered
			? $"{Common.Energy.VoltageTiers.ShortName(Tier)} {Definition.Label}"
			: Definition.Label;

	// Tile resolved via MachineKey ("lv_macerator"); the entity's tML Name
	// is the family class name post-entity-collapse, not a per-tier id.
	protected virtual int OwnerTileType =>
		Mod.TryFind<ModTile>(MachineKey, out var t) ? t.Type : 0;

	// Definition default is (2, 2); (1, 1) covers the pre-bind window.
	public virtual (int Width, int Height) Size => Definition?.Size ?? (1, 1);

	// Rendering active-overlay flag (upstream isActive() semantics).
	// WorkableTieredMachine forwards to RecipeLogic; passive consumers
	// override with their own brown-out logic.
	public virtual bool IsActive => false;

	// "Working" flag for the GUI/hover working-status line ("Idling" vs
	// "Running"). Defaults to IsActive; a machine whose IsActive means
	// "powered/ready" but which only does visible work on demand (HPCA -
	// powered with no computation requested is idle) overrides this so the
	// panel doesn't claim "Running Perfectly" while nothing happens.
	public virtual bool DisplayActive => IsActive;

	public IEnumerable<(int x, int y)> Cells()
	{
		var (w, h) = Size;
		int ox = Position.X, oy = Position.Y;
		for (int dx = 0; dx < w; dx++)
			for (int dy = 0; dy < h; dy++)
				yield return (ox + dx, oy + dy);
	}

	public override bool IsTileValidForEntity(int x, int y)
	{
		// OwnerTileType needs the bound definition; tML may call us before
		// LoadData restores it, so the tile-backstop branch of BindDefinition
		// recovers it.
		BindDefinition();
		Tile tile = Main.tile[x, y];
		return tile.HasTile && tile.TileType == OwnerTileType;
	}

	public virtual void AppendTooltip(List<string> lines)
	{
		lines.Add(DisplayName);
		OnAddFancyInformationTooltip(lines);
		// Gated on SupportsWorkingEnabledToggle so a super tank with
		// auto-output off doesn't show "Disabled" (it's still storing fluid).
		if (SupportsWorkingEnabledToggle && !WorkingEnabled)
			lines.Add("[c/FF4444:Disabled]");
	}

	void IFancyTooltip.AppendFancyTooltip(List<string> lines) =>
		OnAddFancyInformationTooltip(lines);

	// Upstream keys description on the tier-prefixed id (= MachineKey) and
	// falls back to the bare id for tooltipBuilder-driven machines.
	// port-locale.py flattens "<id>.tooltip.<N>" -> "<id>_<N>"; walk N until miss.
	public virtual void OnAddFancyInformationTooltip(List<string> lines) =>
		MachineTooltipLookup.AppendDescriptionAndBuilder(lines, MachineKey, MachineId, Definition);

	// Server-authority gate. IsServer = SP OR dedicated server; clients are
	// read-only snapshots populated by sync packets.
	public static bool IsServer       => Main.netMode != NetmodeID.MultiplayerClient;
	public static bool IsClient       => Main.netMode == NetmodeID.MultiplayerClient;
	public static bool IsMultiplayer  => Main.netMode != NetmodeID.SinglePlayer;

	// Server-only. MachineViewPacket maintains; periodic state-sync only
	// broadcasts to machines with viewers > 0 (no GUI = no per-tick traffic).
	private readonly HashSet<int> _viewers = new();
	public IEnumerable<int> Viewers => _viewers;
	public int  ViewerCount          => _viewers.Count;
	public bool HasViewers           => _viewers.Count > 0;
	public bool HasViewer(int whoAmI) => _viewers.Contains(whoAmI);

	// Server-side memo (not persisted) - drives MachineStateSync dirty-skip.
	// Null = never broadcast, forces next send.
	public byte[]? LastBroadcastBlob { get; set; }

	// Server-side memo (not persisted) - drives MachineEnergySync dirty-skip.
	// Null = never broadcast, forces next send. Energy is synced on its own
	// compact channel (see MachineEnergySyncPacket) so per-tick energy changes
	// don't re-send the whole state blob.
	public long? LastBroadcastEnergy { get; set; }

	// Energy is synced via the compact MachineEnergySyncPacket; the state blob
	// omits it (see NotifiableEnergyContainer.SaveForSync). Default = no energy.
	public virtual bool HasSyncEnergy => false;
	public virtual long SyncEnergyStored => 0;
	public virtual void ApplySyncEnergy(long energy) { }

	public void AddViewer(int whoAmI)
	{
		if (!IsServer) return;
		_viewers.Add(whoAmI);
	}
	public void RemoveViewer(int whoAmI)
	{
		if (!IsServer) return;
		_viewers.Remove(whoAmI);
	}

	// Drop disconnected viewers; called by EnergyNetSystem's sync pass.
	public void PruneViewers()
	{
		if (!IsServer || _viewers.Count == 0) return;
		_viewers.RemoveWhere(w => w < 0 || w >= Main.maxPlayers || !Main.player[w].active);
	}

	// Initial placement sync (tML auto-fires on TE Place / chunk-load / join).
	// Per-tick state changes ride MachineStateSyncPacket, not this.
	public override void NetSend(BinaryWriter writer)
	{
		var tag = new TagCompound();
		SaveData(tag);
		TagIO.Write(tag, writer);
	}

	public override void NetReceive(BinaryReader reader)
	{
		var tag = TagIO.Read(reader);
		LoadData(tag);
	}

	// Deferred-add (upstream waitingToAdd) prevents concurrent-modification
	// when a trait's tick body subscribes another callback.
	private readonly List<TickableSubscription> _serverTicks  = new();
	private readonly List<TickableSubscription> _waitingToAdd = new();

	// Verbatim port of MetaMachine.subscribeServerTick.
	public TickableSubscription? SubscribeServerTick(Action runnable)
	{
		if (IsClient) return null;
		var sub = new TickableSubscription(runnable);
		_waitingToAdd.Add(sub);
		return sub;
	}

	public void Unsubscribe(TickableSubscription? current) => current?.Unsubscribe();

	// Driven by EnergyNetSystem.PostUpdateWorld. Server-only - clients receive
	// snapshots via MachineStateSyncPacket. Subscriptions walk UNCONDITIONALLY;
	// pause lives inside RecipeLogic (which self-unsubscribes), so a paused
	// machine still ticks its AutoOutputTrait (empties finished output).
	internal virtual void SystemTick()
	{
		if (IsClient) return;
		EnsureLoaded();

		if (_waitingToAdd.Count > 0)
		{
			_serverTicks.AddRange(_waitingToAdd);
			_waitingToAdd.Clear();
		}
		for (int i = _serverTicks.Count - 1; i >= 0; i--)
		{
			var sub = _serverTicks[i];
			if (sub.StillSubscribed) sub.Run();
			if (!sub.StillSubscribed) _serverTicks.RemoveAt(i);
		}

		// Single 20 Hz gate for every OnTick body across every MetaMachine
		// subclass - upstream's MC cadence. Without this, anything that
		// advances state per OnTick (recipe progress / EU draw / liquid
		// pump / random-tick scan / passive EU emission) runs 3x too fast
		// at SimSpeed=1.0. Trait subscriptions ABOVE this gate still tick
		// every Terraria frame so AutoOutputTrait + per-trait cycle gates
		// (`% FromMcTicks(N)`, which use real-time `GameUpdateCount`) keep
		// their existing 60 Hz dispatch + internal modulo math.
		if (Main.GameUpdateCount % (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1) != 0) return;

		OnTick();
	}

	// Lazy OnMachineLoad - the Activator ctor runs before LoadData, so traits
	// can only safely subscribe AFTER state is rehydrated.
	private bool _machineLoaded;
	private void EnsureLoaded()
	{
		if (_machineLoaded) return;
		_machineLoaded = true;
		Traits.OnMachineLoad();
		OnMachineLoaded();
	}

	// First-tick post-load hook. Recipe machines override to restart their
	// loop sound for a machine loaded mid-recipe (no transition fires).
	// Server-side; MP clients restart via OnClientSync.
	protected virtual void OnMachineLoaded() { }

	protected virtual void OnTick() { }

	// Tier-coloured glow when working (aqua LV / cyan MV / gold HV / etc.).
	public virtual Microsoft.Xna.Framework.Vector3 WorkingLightColor =>
		Common.Energy.VoltageTiers.LightColor(Tier);

	// Per-cell conditional overlay, drawn after the active overlay. Default
	// no-op; rotor holder uses it for IS_FORMED-gated overlay_rotor_holder.
	public virtual void DrawCustomOverlay(Microsoft.Xna.Framework.Graphics.SpriteBatch sb, int i, int j) { }

	// Per-frame (NOT per-tick - 60Hz vs 20Hz); rate-limit via Main.rand.
	public virtual void OnClientFrame() { }

	// Fires after LoadData applies a MachineStateSync snapshot. Idempotent -
	// implementations should self-rate-limit (EnsureLoopSound is).
	internal virtual void OnClientSync() { }

	public virtual Item[]? GetSlotGroup(SlotGroup group) => null;

	// SlotAction's direct ref-write bypasses the handler's Insert/Extract
	// listeners; this base impl walks traits and fires OnContentsChanged on
	// every NotifiableItemStackHandler matching the group's IO.
	public virtual void NotifySlotGroupChanged(SlotGroup group)
	{
		if (group == SlotGroup.Charger) return;
		foreach (var trait in Traits.AllTraits)
		{
			if (trait is not Api.Machine.Trait.NotifiableItemStackHandler nish) continue;
			bool match = group switch
			{
				SlotGroup.Inventory       => true,
				SlotGroup.InventoryInput  => nish.HandlerIO.Supports(Api.Capability.Recipe.IO.IN),
				SlotGroup.InventoryOutput => nish.HandlerIO.Supports(Api.Capability.Recipe.IO.OUT),
				_                         => false,
			};
			if (match) nish.OnContentsChanged();
		}
	}

	// Identity by default; machines with separate import/export tanks override.
	public virtual int ResolveFluidTank(Api.Capability.Recipe.IO direction, int localIndex) => localIndex;

	// Pass-through to IControllable; non-IControllable machines can't pause.
	public bool WorkingEnabled
	{
		get => this is IControllable c ? c.IsWorkingEnabled() : true;
		set { if (this is IControllable c) c.SetWorkingEnabled(value); }
	}

	// SteamBoilerMachine further opts out (no power toggle in upstream).
	public virtual bool SupportsWorkingEnabledToggle => this is IControllable;

	// Drives the auto-attached I/O config panel content.
	public virtual bool SupportsAutoOutputItems  => false;
	public virtual bool SupportsAutoOutputFluids => false;

	// Null for machines without an auto-output config (transformers, boilers).
	public virtual AutoOutputTrait? AutoOutput => null;

	// Per-side capability resolver. THE pipe / adjacent-transfer entry point;
	// player interaction bypasses it. `useCoverCapability=false` returns the
	// io-gated handler without the cover wrapper (used by a cover to read its
	// OWN host - it must not re-apply its own wrapper). Verbatim upstream
	// MetaMachine.getXHandlerCap.
	public virtual IItemHandler? GetItemHandlerCap(IODirection side, bool useCoverCapability = true)
	{
		if (this is not IItemHandler handler) return null;
		IItemHandler? result = AutoOutput is { } ao && ao.BlocksInputFrom(side, fluid: false)
			? new ExtractOnlyItemHandler(handler)
			: handler;
		if (useCoverCapability && CoverSideOf(side) is { } cs && GetCoverAtSide(cs) is { } cover)
			result = cover.GetItemHandlerCap(result);
		return result;
	}

	public virtual IFluidHandler? GetFluidHandlerCap(IODirection side, bool useCoverCapability = true)
	{
		if (this is not IFluidHandler handler) return null;
		IFluidHandler? result = AutoOutput is { } ao && ao.BlocksInputFrom(side, fluid: true)
			? new ExtractOnlyFluidHandler(handler)
			: handler;
		if (useCoverCapability && CoverSideOf(side) is { } cs && GetCoverAtSide(cs) is { } cover)
			result = cover.GetFluidHandlerCap(result);
		return result;
	}

	// ISidedCapabilityProvider seam - forwards to the cover-aware resolvers above;
	// explicit impl so the public surface stays GetXHandlerCap.
	IItemHandler? ISidedCapabilityProvider.GetItemHandler(IODirection side) =>
		GetItemHandlerCap(side, useCoverCapability: true);

	IFluidHandler? ISidedCapabilityProvider.GetFluidHandler(IODirection side) =>
		GetFluidHandlerCap(side, useCoverCapability: true);

	// None / non-cardinal = sideless query (never cover-gated).
	private static CoverSide? CoverSideOf(IODirection side) => side switch
	{
		IODirection.Up    => CoverSide.Up,
		IODirection.Down  => CoverSide.Down,
		IODirection.Left  => CoverSide.Left,
		IODirection.Right => CoverSide.Right,
		_                 => null,
	};

	// Port of MetaMachine.getXCapFilter. No filter cover = pass-everything.
	public System.Func<Item, bool> GetItemCapFilter(IODirection side, IO io)
	{
		if (CoverSideOf(side) is { } cs && GetCoverAtSide(cs) is ItemFilterCover fc)
		{
			if (!fc.FilterMode.Filters(io))
			{
				if (fc.AllowFlow == ManualIOMode.Disabled)   return _ => false;
				if (fc.AllowFlow == ManualIOMode.Unfiltered) return _ => true;
			}
			return fc.GetItemFilter().Test;
		}
		return _ => true;
	}

	public System.Func<FluidStack, bool> GetFluidCapFilter(IODirection side, IO io)
	{
		if (CoverSideOf(side) is { } cs && GetCoverAtSide(cs) is FluidFilterCover fc)
		{
			if (!fc.FilterMode.Filters(io))
			{
				if (fc.AllowFlow == ManualIOMode.Disabled)   return _ => false;
				if (fc.AllowFlow == ManualIOMode.Unfiltered) return _ => true;
			}
			return fc.GetFluidFilter().Test;
		}
		return _ => true;
	}

	private readonly CoverBehavior?[] _covers = new CoverBehavior?[CoverSides.Count];

	public CoverBehavior? GetCoverAtSide(CoverSide side) => _covers[(int)side];
	public void SetCoverAtSide(CoverBehavior? cover, CoverSide side) => _covers[(int)side] = cover;

	public virtual bool SupportsCovers => true;

	// Per-cover / per-side gating (e.g. solar top-only) lives on CoverBehavior.CanAttach.
	public virtual bool CanPlaceCoverOnSide(CoverDefinition definition, CoverSide side) => SupportsCovers;

	// Covers are UI-only; runtime edits ride their own packet, no-op here.
	public void NotifyBlockUpdate() { }

	public bool IsRemote => IsClient;

	public Point16 GetBlockPos() => Position;

	// Per-position offset so covers on different machines don't sync up.
	// 60 Hz counter (raw Terraria GameUpdateCount). Safe to modulo against
	// FromMcTicks(N) from a trait/cover SubscribeServerTick body (which runs
	// every Terraria tick - the counter increments by 1 each call, so
	// `% FromMcTicks(N)` hits every FromMcTicks(N) ticks regardless of offset).
	public long GetOffsetTimer() =>
		(long)Main.GameUpdateCount + ((Position.X * 13L + Position.Y * 7L) & 0x3FF);

	// MC-tick-aligned (use INSIDE OnTick / OnWorking). GetOffsetTimer's 60 Hz
	// value mod FromMcTicks(N) shares factors with FromMcTicks(1) and silently
	// freezes the cadence for ~2/3 of world positions (the Large Boiler
	// "temperature stuck at 0" bug). This counter increments by 1 per OnTick
	// call, so use a RAW `% N` (no FromMcTicks).
	public long GetMcOffsetTimer() =>
		(long)(Main.GameUpdateCount / (uint)global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(1))
		+ ((Position.X * 13L + Position.Y * 7L) & 0x3FF);

	// Terraria fires HitWire per cell, so a multi-tile machine sees N pulses;
	// dedup to one per tick.
	private ulong _lastWirePulseTick;

	public bool TryConsumeWirePulse()
	{
		if (_lastWirePulseTick == Main.GameUpdateCount) return false;
		_lastWirePulseTick = Main.GameUpdateCount;
		return true;
	}

	// WTM / SteamWorkableMachine.PreventPowerFail route through here.
	protected bool HasPowerFailPreventingCover()
	{
		foreach (var cover in ((ICoverable)this).GetCovers())
			if (cover is MachineControllerCover { PreventPowerFail: true })
				return true;
		return false;
	}

	// Subclasses MUST call base.SaveData / base.LoadData. Tier is persisted
	// per-instance so it survives Type-ID shifts across mod versions.
	public override void SaveData(TagCompound tag)
	{
		tag["mte_tier"] = (byte)Tier;
		if (_persistedMdefId != null) tag["mdef"] = _persistedMdefId;
		// Working-enabled is NOT persisted here - it lives on the IControllable
		// owner (RecipeLogic's SUSPEND status / the battery buffer's own field),
		// each of which persists itself.
		Traits.Save(tag);
		((ICoverable)this).SaveCovers(tag);
	}

	public override void LoadData(TagCompound tag)
	{
		if (tag.ContainsKey("mte_tier"))
			_persistedTier = (Common.Energy.VoltageTier)tag.GetByte("mte_tier");
		if (tag.ContainsKey("mdef"))
		{
			_persistedMdefId = tag.GetString("mdef");
			BindDefinition();
		}
		Traits.Load(tag);
		((ICoverable)this).LoadCovers(tag);
	}

	// Wire snapshot for MachineStateSyncPacket. SaveData first (preserves every
	// subclass override), then Traits.SaveForSync overwrites trait subtrees so
	// noisy traits can omit per-tick fields. DO NOT inline SaveData fields -
	// any subclass field would be silently missed (e.g. MultiblockPartMachine's
	// _controllerPositions causing parts to not reskin to the controller casing).
	public virtual void SaveDataForSync(TagCompound tag)
	{
		SaveData(tag);
		Traits.SaveForSync(tag);
	}

	// MP client-only: invoked from EnergyNetSystem.PostUpdateEverything to
	// advance trait state between broadcasts (recipe progress interpolation).
	// Server uses the existing per-trait subscription path.
	internal void OnClientPostUpdate()
	{
		Traits.OnClientTick();
	}

	// Carried across break -> item -> re-place. Default no-op (machines drop
	// stateless items, upstream parity). DO NOT put slot inventory here -
	// OnKill drops slot contents loose, duplicating would double them.
	public virtual void WritePortableData(TagCompound tag) { }
	public virtual void ReadPortableData(TagCompound tag) { }

	// Walks SlotGroups + drops with NBT preservation (Clone-replace because
	// Item.NewItem alone loses ModItem instance state). Subclasses that need
	// extra cleanup override + call base first.
	public override void OnKill()
	{
		if (!IsServer) return;

		var (tw, th) = Size;
		int worldX = Position.X * 16, worldY = Position.Y * 16;
		int boxW   = tw * 16,           boxH   = th * 16;
		var src    = new Terraria.DataStructures.EntitySource_TileBreak(Position.X, Position.Y);

		// Inventory is a concat view unless Input/Output are missing - some machines
		// (BatteryBuffer / MufflerHatch / MaintenanceHatch / Crate) use Inventory
		// as primary backing. Heuristic: no Input/Output exposed -> Inventory IS primary.
		bool inventoryIsView = GetSlotGroup(SlotGroup.InventoryInput) != null
		                    || GetSlotGroup(SlotGroup.InventoryOutput) != null;
		foreach (SlotGroup group in System.Enum.GetValues(typeof(SlotGroup)))
		{
			if (group == SlotGroup.Inventory && inventoryIsView) continue;
			var inv = GetSlotGroup(group);
			if (inv is null) continue;
			DropInventory(inv, src, worldX, worldY, boxW, boxH);
		}

		foreach (var side in CoverSides.All)
			foreach (var drop in ((ICoverable)this).RemoveCover(side))
				SpawnItem(drop, src, worldX, worldY, boxW, boxH);

		// AFTER inventory drop so traits can read slot contents one last time.
		Traits.OnMachineDestroyed();
	}

	private static void DropInventory(Terraria.Item[] inv,
		Terraria.DataStructures.IEntitySource src,
		int worldX, int worldY, int boxW, int boxH)
	{
		for (int i = 0; i < inv.Length; i++)
		{
			var item = inv[i];
			if (item is null || item.IsAir) continue;
			SpawnItem(item, src, worldX, worldY, boxW, boxH);
			inv[i] = new Terraria.Item();
		}
	}

	// Clone-replace preserves ModItem NBT (vanilla-chest drop pattern).
	private static void SpawnItem(Terraria.Item item,
		Terraria.DataStructures.IEntitySource src,
		int worldX, int worldY, int boxW, int boxH)
	{
		if (item is null || item.IsAir) return;
		int idx = Terraria.Item.NewItem(src, worldX, worldY, boxW, boxH,
			item.type, item.stack, noBroadcast: true);
		Main.item[idx]          = item.Clone();
		Main.item[idx].position = new Microsoft.Xna.Framework.Vector2(
			worldX + boxW / 2f - item.width / 2f,
			worldY + boxH / 2f - item.height / 2f);
		Main.item[idx].whoAmI   = idx;
		if (Main.netMode == NetmodeID.Server)
			NetMessage.SendData(MessageID.SyncItem, number: idx, number2: 1f);
	}
}
