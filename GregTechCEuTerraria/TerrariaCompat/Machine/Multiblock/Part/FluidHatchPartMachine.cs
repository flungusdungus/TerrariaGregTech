#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of FluidHatchPartMachine. Sister to ItemBusPartMachine for fluids.
// _1X (1 tank, 8 buckets/tier-1), _4X (4 tanks, 2 buckets), _9X (9 tanks, 1
// bucket). See ItemBusPartMachine header for the full adaptation list -
// same shape applies here.
public class FluidHatchPartMachine : TieredIOPartMachine, IHasCircuitSlot
{
	public const int BUCKET_VOLUME           = 1000;
	public const int INITIAL_TANK_CAPACITY_1X = 8 * BUCKET_VOLUME;
	public const int INITIAL_TANK_CAPACITY_4X = 2 * BUCKET_VOLUME;
	public const int INITIAL_TANK_CAPACITY_9X = 1 * BUCKET_VOLUME;

	protected override string Label => "Fluid Hatch";

	public NotifiableFluidTank?         Tank             { get; protected set; }
	public NotifiableItemStackHandler?  CircuitInventory { get; protected set; }
	public bool                         CircuitSlotEnabled { get; protected set; }

	// CircuitInventory deliberately NOT exposed.
	public override Api.Capability.IFluidHandler? ExposedFluidHandler => Tank;

	protected int Slots;
	protected int InitialCapacity;

	private TickableSubscription? _autoIOSubs;

	public FluidHatchPartMachine() : base() { }

	public override UI.Widgets.UIDirectionSelector.Mode PartIoConfigMode =>
		UI.Widgets.UIDirectionSelector.Mode.Fluids;

	public void Configure(int tier, IO io, int initialCapacity, int slots)
	{
		Tier            = tier;
		Io              = io;
		InitialCapacity = initialCapacity;
		Slots           = slots;
		EnsureTraits();
		EnsureAutoIOSubscription();
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		var def = Definition;
		if (def?.PartIo == null || def.PartFluidSlots == 0) return;
		int initialCapacity = def.PartFluidSlots switch
		{
			1 => INITIAL_TANK_CAPACITY_1X,
			4 => INITIAL_TANK_CAPACITY_4X,
			9 => INITIAL_TANK_CAPACITY_9X,
			_ => INITIAL_TANK_CAPACITY_1X,
		};
		Configure((int)((MetaMachine)this).Tier, def.PartIo.Value, initialCapacity, def.PartFluidSlots);
	}

	protected virtual NotifiableFluidTank CreateTank(int initialCapacity, int slots) =>
		new(slots, GetTankCapacity(initialCapacity, Tier), Io);

	public static int GetTankCapacity(int initialCapacity, int tier) =>
		initialCapacity * (1 << System.Math.Min(9, tier));

	private void EnsureTraits()
	{
		if (Tank != null) return;
		Tank = CreateTank(InitialCapacity, Slots);
		Traits.Attach(Tank);
		Traits.RegisterPersistent("Tank", Tank);

		if (Io == IO.IN)
		{
			CircuitSlotEnabled = true;
			CircuitInventory = new NotifiableItemStackHandler(1, IO.IN, IO.NONE)
				.SetFilter(IsProgrammedCircuit);
		}
		else
		{
			CircuitSlotEnabled = false;
			CircuitInventory = new NotifiableItemStackHandler(0, IO.NONE);
		}
		Traits.Attach(CircuitInventory);
		Traits.RegisterPersistent("CircuitInventory", CircuitInventory);
	}

	private static bool IsProgrammedCircuit(Item stack) =>
		stack != null && stack.ModItem is IntCircuitItem;

	// === IHasCircuitSlot ==================================================

	public bool IsCircuitSlotEnabled() => CircuitSlotEnabled;

	public void SetCircuitSlotEnabled(bool enabled)
	{
		CircuitSlotEnabled = enabled;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	// === Lifecycle =========================================================

	public override void AddedToController(MultiblockControllerMachine controller)
	{
		if (!controller.AllowCircuitSlots())
		{
			DropCircuitToWorld();
			SetCircuitSlotEnabled(false);
		}
		base.AddedToController(controller);
	}

	public override void RemovedFromController(MultiblockControllerMachine controller)
	{
		base.RemovedFromController(controller);
		foreach (var c in GetControllers())
		{
			if (!c.AllowCircuitSlots()) return;
		}
		SetCircuitSlotEnabled(true);
	}

	private void DropCircuitToWorld()
	{
		if (CircuitInventory == null) return;
		for (int i = 0; i < CircuitInventory.SlotCount; i++)
		{
			var stack = CircuitInventory.GetSlot(i);
			if (stack == null || stack.IsAir) continue;
			Item.NewItem(new Terraria.DataStructures.EntitySource_TileBreak(Position.X, Position.Y),
				Position.X * 16, Position.Y * 16, 16, 16, stack.type, stack.stack);
			CircuitInventory.Extract(i, stack.stack, simulate: false);
		}
	}

	private void EnsureAutoIOSubscription()
	{
		_autoIOSubs ??= SubscribeServerTick(AutoIOTick);
	}

	// See ItemBusPartMachine.AutoIOTick for the design notes.
	protected virtual void AutoIOTick()
	{
		if (GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;
		if (!WorkingEnabled || Tank == null) return;
		if (IoDirection == IODirection.None) return;
		if (Io == IO.OUT && !Tank.IsEmpty())
		{
			AdjacentFluidPush.Push(this, 0, Tank.Storages.Length, maxAmount: int.MaxValue, side: IoDirection);
		}
		else if (Io == IO.IN)
		{
			AdjacentFluidPull.Pull(this, maxAmount: int.MaxValue, side: IoDirection);
		}
		else if (Io == IO.BOTH)
		{
			if (!Tank.IsEmpty())
				AdjacentFluidPush.Push(this, 0, Tank.Storages.Length, maxAmount: int.MaxValue, side: IoDirection);
			AdjacentFluidPull.Pull(this, maxAmount: int.MaxValue, side: IoDirection.Opposite());
		}
	}

	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		int perTank = GetTankCapacity(InitialCapacity, Tier);
		if (Slots > 1)
			lines.Add($"Capacity: {Slots} x {perTank:N0} mB = {(long)Slots * perTank:N0} mB");
		else
			lines.Add($"Capacity: {perTank:N0} mB");
		lines.Add($"Side: {IoDirection}");

		if (Tank is null) return;
		bool anyShown = false;
		for (int i = 0; i < Tank.Storages.Length; i++)
		{
			var f = Tank.Storages[i].Fluid;
			if (f.IsEmpty) continue;
			if (!anyShown) { lines.Add("Contents:"); anyShown = true; }
			string name = f.Type?.DisplayName ?? "?";
			lines.Add($"  {f.Amount:N0} mB {name}");
		}
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["initialCapacity"]    = InitialCapacity;
		tag["slots"]              = Slots;
		tag["circuitSlotEnabled"] = CircuitSlotEnabled;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		InitialCapacity    = tag.GetInt("initialCapacity");
		Slots              = tag.GetInt("slots");
		CircuitSlotEnabled = !tag.ContainsKey("circuitSlotEnabled") || tag.GetBool("circuitSlotEnabled");
		EnsureTraits();
		// Re-load after late trait registration (ItemBus pattern).
		Traits.Load(tag);
		EnsureAutoIOSubscription();
	}
}
