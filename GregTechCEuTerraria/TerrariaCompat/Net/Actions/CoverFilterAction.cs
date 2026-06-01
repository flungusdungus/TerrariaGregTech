#nullable enable
using System;
using System.IO;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using Terraria;
using Terraria.ID;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Server-authoritative cover-filter edit - the matcher (phantom) slots, the
// blacklist / ignore-NBT toggles, and the filter-item install slot.
//
// The matcher-slot logic is a verbatim port of upstream PhantomSlotWidget
// .slotClickPhantom: a phantom slot holds a "ghost" item/fluid (type + count),
// never a real one. LMB with a held item sets it (count = held count); RMB
// sets count 1; empty-handed LMB/RMB steps the count -1/+1; Shift halves /
// doubles; middle-click clears. The held item is read-only here - phantom
// slots don't consume it (the one upstream divergence: no JEI ghost-drag, so
// you click with a real held item / a fluid container instead).
//
// The filter-item slot IS a real cursor<->slot swap (the filter item is a real
// inventory item) - server-confirmed cursor write-back, same as CoverAction.
public sealed class CoverFilterAction : ICoverAction
{
	public enum Op : byte
	{
		MatcherClick    = 0,
		ToggleBlacklist = 1,
		ToggleIgnoreNbt = 2,
		FilterSlot      = 3,
		SetTagExpr      = 4,   // tag-filter expression text (TagFilter.SetOreDict)
		// Set the filter TYPE on a cover programmatically (no cursor item) -
		// used by the pipe settings panel where filter installation has no
		// item economy. _index carries the FilterType byte (0=none, 1=simple,
		// 2=tag). Server resolves the matching filter ITEM and installs it
		// via the cover's UiItemFilterHandler / UiFluidFilterHandler.
		SetFilterType   = 5,
	}

	public PacketType Type => PacketType.CoverFilter;

	private CoverSide _side;
	private Op _op;
	private bool _fluid;
	private int _index;
	private byte _button;
	private bool _shift;
	private Item _held = new();
	private string _text = "";

	public CoverFilterAction() { }

	public static CoverFilterAction Matcher(CoverSide side, bool fluid, int index, int button, bool shift, Item held) =>
		new() { _side = side, _op = Op.MatcherClick, _fluid = fluid, _index = index,
		        _button = (byte)button, _shift = shift, _held = held.Clone() };

	public static CoverFilterAction Toggle(CoverSide side, bool fluid, Op toggleOp) =>
		new() { _side = side, _op = toggleOp, _fluid = fluid };

	public static CoverFilterAction FilterItem(CoverSide side, bool fluid, Item held) =>
		new() { _side = side, _op = Op.FilterSlot, _fluid = fluid, _held = held.Clone() };

	public static CoverFilterAction TagExpr(CoverSide side, bool fluid, string expr) =>
		new() { _side = side, _op = Op.SetTagExpr, _fluid = fluid, _text = expr ?? "" };

	// `type` is the PipeFilterType byte (see PipeSettingsState).
	public static CoverFilterAction SetType(CoverSide side, bool fluid, int type) =>
		new() { _side = side, _op = Op.SetFilterType, _fluid = fluid, _index = type };

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_side);
		w.Write((byte)_op);
		w.Write(_fluid);
		w.Write(_index);
		w.Write(_button);
		w.Write(_shift);
		w.WriteItem(_held);
		w.Write(_text);
	}

	public void Read(BinaryReader r)
	{
		_side = (CoverSide)r.ReadByte();
		_op = (Op)r.ReadByte();
		_fluid = r.ReadBoolean();
		_index = r.ReadInt32();
		_button = r.ReadByte();
		_shift = r.ReadBoolean();
		_held = r.ReadItem();
		_text = r.ReadString();
	}

	public void Apply(ICoverable target, int byWhoAmI)
	{
		// Op.SetFilterType is the one op that doesn't require a pre-existing
		// cover - its whole purpose is to CREATE / REPLACE the cover at the
		// side. Dispatch it before the existing-cover gate. Every other op
		// reads from / mutates an existing cover, so the gate stands.
		if (_op == Op.SetFilterType)
		{
			if (target is TerrariaCompat.Pipelike.PipeCoverable pipe)
			{
				var t = (TerrariaCompat.Pipelike.PipeCoverable.PipeFilterType)
					System.Math.Clamp(_index, 0, 2);
				pipe.SetFilterType(_side, t);
			}
			return;
		}

		var cover = target.GetCoverAtSide(_side);
		if (cover is null) return;

		switch (_op)
		{
			case Op.MatcherClick:
				if (_fluid)
				{
					if (cover.UiFluidFilter is { } ff) FluidMatcherClick(ff, _index, _button, _shift, HeldFluid(_held));
				}
				else if (cover.UiItemFilter is { } itf)
				{
					ItemFilterEdit.MatcherClick(itf, _index, _button, _shift, _held);
				}
				break;

			case Op.ToggleBlacklist:
				if (_fluid) cover.UiFluidFilter?.SetBlackList(!cover.UiFluidFilter.IsBlackList);
				else        cover.UiItemFilter?.SetBlackList(!cover.UiItemFilter.IsBlackList);
				break;

			case Op.ToggleIgnoreNbt:
				if (_fluid) cover.UiFluidFilter?.SetIgnoreNbt(!cover.UiFluidFilter.IgnoreNbt);
				else        cover.UiItemFilter?.SetIgnoreNbt(!cover.UiItemFilter.IgnoreNbt);
				break;

			case Op.FilterSlot:
				if (_fluid) FilterSlotSwap(cover.UiFluidFilterHandler, byWhoAmI);
				else        FilterSlotSwap(cover.UiItemFilterHandler, byWhoAmI);
				break;

			case Op.SetTagExpr:
			{
				// The installed filter is a tag filter - set its expression.
				// Layer-agnostic via the UiTagItemFilter / UiTagFluidFilter
				// accessors on CoverBehavior: default impls read via the
				// handler (Conveyor / Pump / Detector / Ender), filter-cover
				// overrides read off the cover's AttachItem-driven lazy filter.
				TagFilter? tag = _fluid ? (TagFilter?)cover.UiTagFluidFilter
				                        : (TagFilter?)cover.UiTagItemFilter;
				tag?.SetOreDict(_text);
				break;
			}

			// Op.SetFilterType handled above the existing-cover gate - it's the
			// one op that creates / replaces a cover so it doesn't require a
			// pre-existing one to read from.
		}
	}

	// === Phantom matcher clicks - verbatim PhantomSlotWidget.slotClickPhantom ==
	// The item variant lives in Api.Cover.Filter.ItemFilterEdit (shared with the
	// item-magnet filter UI); the fluid variant stays here - covers are its only
	// consumer.

	private static void FluidMatcherClick(SimpleFluidFilter filter, int index, int button, bool shift, FluidStack held)
	{
		if (index < 0 || index >= filter.Matches.Length) return;
		FluidStack slot = filter.Matches[index];

		if (button == 2)
			filter.Matches[index] = FluidStack.Empty;
		else if (button == 0 || button == 1)
		{
			if (slot.IsEmpty)
			{
				if (!held.IsEmpty) filter.Matches[index] = FluidFill(held, button, filter.MaxStackSize);
			}
			else if (held.IsEmpty)
				filter.Matches[index] = FluidAdjust(slot, button, shift, filter.MaxStackSize);
			else
				filter.Matches[index] = FluidFill(held, button, filter.MaxStackSize);
		}
		filter.OnUpdated();
	}

	private static FluidStack FluidFill(FluidStack held, int button, int maxStack)
	{
		int amount = Math.Clamp(button == 0 ? held.Amount : 1, 1, maxStack);
		return held.WithAmount(amount);
	}

	private static FluidStack FluidAdjust(FluidStack slot, int button, bool shift, int maxStack)
	{
		int cur = slot.Amount;
		int next = shift ? (button == 0 ? (cur + 1) / 2 : cur * 2)
		                 : (button == 0 ? cur - 1 : cur + 1);
		next = Math.Min(next, maxStack);
		return next <= 0 ? FluidStack.Empty : slot.WithAmount(next);
	}

	// === Filter-item install slot - a real cursor<->slot swap ===================
	// Empty slot + held filter item -> install one (cursor loses one). Occupied
	// slot + empty cursor -> remove it back to the cursor. (Occupied + held is a
	// no-op - take the installed filter out first; matches the cover-slot rule.)
	private void FilterSlotSwap(ItemFilterHandler? handler, int byWhoAmI) => FilterSlotSwapImpl(handler, byWhoAmI);
	private void FilterSlotSwap(FluidFilterHandler? handler, int byWhoAmI) => FilterSlotSwapImpl(handler, byWhoAmI);

	private void FilterSlotSwapImpl<TR, TF>(FilterHandler<TR, TF>? handler, int byWhoAmI)
		where TF : class, IFilter<TR>
	{
		if (handler is null) return;
		if (handler.FilterItem.IsAir)
		{
			if (_held.IsAir || !handler.CanInsertFilterItem(_held)) return;
			var one = _held.Clone();
			one.stack = 1;
			handler.SetFilterItem(one);
			var remainder = _held.Clone();
			if (--remainder.stack <= 0) remainder.TurnToAir();
			WriteBackCursor(byWhoAmI, remainder);
		}
		else if (_held.IsAir)
		{
			var removed = handler.FilterItem;
			handler.SetFilterItem(new Item());
			WriteBackCursor(byWhoAmI, removed);
		}
	}

	private static void WriteBackCursor(int byWhoAmI, Item cursor)
	{
		if (Main.netMode == NetmodeID.Server)
			CursorUpdatePacket.SendTo(byWhoAmI, cursor, CursorUpdatePacket.Delivery.Cursor);
		else
			Main.mouseItem = cursor;
	}

	// Resolve the fluid carried by a held container - vanilla bucket, GT
	// per-fluid bucket, or any IFluidHandlerItem (fluid cell). Empty otherwise.
	private static FluidStack HeldFluid(Item held)
	{
		if (held is null || held.IsAir) return FluidStack.Empty;
		var vanilla = VanillaFluidBridge.StackFor(held.type);
		if (!vanilla.IsEmpty) return vanilla;
		if (held.ModItem is FluidBucketItem bucket && bucket.Fluid is { } gf)
			return new FluidStack(gf, VanillaFluidBridge.BucketAmount);
		if (held.ModItem is IFluidHandlerItem container)
			return container.GetTank(0);
		return FluidStack.Empty;
	}
}
