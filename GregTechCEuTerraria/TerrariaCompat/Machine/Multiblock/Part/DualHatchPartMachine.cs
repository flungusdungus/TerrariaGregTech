#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of DualHatchPartMachine. ItemBus + fluid tank, both I/O on the same
// IoDirection face every 5 ticks. Tier-keyed sizing: (tier-4)^2 slots, sqrt
// tanks, 16 buckets x 2^(tier-6) per tank.
public class DualHatchPartMachine : ItemBusPartMachine
{
	public const int INITIAL_TANK_CAPACITY = 16 * FluidHatchPartMachine.BUCKET_VOLUME;

	protected override string Label => "Dual Hatch";

	public NotifiableFluidTank? Tank { get; protected set; }

	public override Api.Capability.IFluidHandler? ExposedFluidHandler => Tank;

	public DualHatchPartMachine() : base() { }

	// Differs from ItemBus's (1 + min(9, tier))^2.
	protected override int GetInventorySize() => (Tier - 4) * (Tier - 4);

	public static int GetTankCapacity(int initialCapacity, int tier) =>
		initialCapacity * (1 << (tier - 6));

	// Register the Tank in OnDefinitionBound (after the base wires the item
	// inventory) - same convention as FluidHatch / SteamHatch. MetaMachine
	// .LoadData calls BindDefinition -> OnDefinitionBound BEFORE Traits.Load, so
	// the tank is in the persistent set before its saved state loads; and
	// OnDefinitionBound also runs on fresh placement (the path the old
	// `new Configure` + LoadData-rerun hack silently missed - a freshly placed
	// dual hatch had no tank until world reload). Tank sizing reads Tier only,
	// which BindDefinition restores, so no save fields are needed for it.
	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		EnsureTank();
	}

	private void EnsureTank()
	{
		if (Tank != null) return;
		int slots = (int)Math.Sqrt(GetInventorySize());
		Tank = new NotifiableFluidTank(slots, GetTankCapacity(INITIAL_TANK_CAPACITY, Tier), Io);
		Traits.Attach(Tank);
		Traits.RegisterPersistent("Tank", Tank);
	}

	protected override void AutoIOTick()
	{
		if (GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;
		if (!WorkingEnabled) return;
		// Push/Pull treat None as "every perimeter side"; explicitly skip here
		// (sibling ItemBus / FluidHatch do the same).
		if (IoDirection == IODirection.None) return;

		if (Io == IO.OUT)
		{
			if (Inventory != null && !Inventory.IsEmpty())
				AdjacentItemPush.Push(this, 0, Inventory.SlotCount, maxPerSlot: int.MaxValue, side: IoDirection);
			if (Tank != null && !Tank.IsEmpty())
				AdjacentFluidPush.Push(this, 0, Tank.Storages.Length, maxAmount: int.MaxValue, side: IoDirection);
		}
		else if (Io == IO.IN)
		{
			if (Inventory != null)
				AdjacentItemPull.Pull(this, 0, Inventory.SlotCount, maxPerSlot: int.MaxValue, side: IoDirection);
			if (Tank != null)
				AdjacentFluidPull.Pull(this, maxAmount: int.MaxValue, side: IoDirection);
		}
		else if (Io == IO.BOTH)
		{
			if (Inventory != null && !Inventory.IsEmpty())
				AdjacentItemPush.Push(this, 0, Inventory.SlotCount, maxPerSlot: int.MaxValue, side: IoDirection);
			if (Tank != null && !Tank.IsEmpty())
				AdjacentFluidPush.Push(this, 0, Tank.Storages.Length, maxAmount: int.MaxValue, side: IoDirection);
			if (Inventory != null)
				AdjacentItemPull.Pull(this, 0, Inventory.SlotCount, maxPerSlot: int.MaxValue, side: IoDirection.Opposite());
			if (Tank != null)
				AdjacentFluidPull.Pull(this, maxAmount: int.MaxValue, side: IoDirection.Opposite());
		}
	}
}
