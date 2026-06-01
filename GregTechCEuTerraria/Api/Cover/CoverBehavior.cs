#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.Api.Cover;

// Port of com.gregtechceu.gtceu.api.cover.CoverBehavior - a cover instance
// attached to one side of a machine.
//
// Documented adaptations:
//   - Rendering surface dropped (getCoverRenderer / shouldRenderPlate /
//     getAppearance / getDynamicRenderer) - covers are UI-only (Terraria 2D).
//   - World-tool interaction dropped (onToolClick / onScrewdriverClick /
//     onSoftMalletClick, IToolGridHighlight, sideTips) - covers are placed,
//     removed and configured entirely through the machine GUI. A cover with a
//     settings screen implements IUICover; the GUI opens it on right-click.
//   - Sync system dropped (ISyncManaged / @SaveField / @SyncToClient,
//     syncDataHolder) - cover state persists via Save/Load into the machine's
//     NBT and rides the machine sync packet; runtime edits go through a
//     server-authoritative cover action packet.
//   - redstoneSignalOutput / canConnectRedstone KEPT - to be mapped onto
//     Terraria wire signals when detector covers land (Phase 4).
//   - getItemHandlerCap / getFluidHandlerCap KEPT as IO-interception hooks
//     (used by filter / conveyor / pump covers - Phase 4, behaviour stubbed
//     until pipes exist).
public abstract class CoverBehavior
{
	public CoverDefinition CoverDefinition { get; }
	public ICoverable CoverHolder { get; }
	public CoverSide AttachedSide { get; }

	// The item this cover was attached from (stack forced to 1). Drives drops.
	public Item AttachItem { get; protected set; } = new Item();

	protected int _redstoneSignalOutput;
	public int RedstoneSignalOutput => _redstoneSignalOutput;

	protected CoverBehavior(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
	{
		CoverDefinition = definition;
		CoverHolder = coverHolder;
		AttachedSide = attachedSide;
	}

	// ===== Initialization =====================================================

	public void ScheduleRenderUpdate() => CoverHolder.NotifyBlockUpdate();

	// Server-side check whether the cover may attach. Upstream additionally
	// gates on the machine's front-facing; we have no front-facing, so the base
	// is unconditional. Subclasses still override (e.g. solar panel: top only).
	public virtual bool CanAttach() => true;

	// Called after attachment - `itemStack` is the cover item.
	public virtual void OnAttached(Item itemStack)
	{
		AttachItem = itemStack.Clone();
		AttachItem.stack = 1;
	}

	public virtual void OnLoad() { }

	public virtual void OnUnload() { }

	// ===== Misc ===============================================================

	// Human-readable per-cover status line, surfaced in the cover slot's hover
	// tooltip in the machine GUI. Returns null = no extra line. Concrete covers
	// override to surface live state (e.g. "Producing 32 EU/t" / "Sky blocked")
	// so the player can diagnose covers that silently do nothing.
	public virtual string? GetStatusText() => null;

	public Item GetPickItem() => AttachItem;

	// Additional drops beyond the cover item itself (e.g. a filter cover drops
	// its installed filter). Base = none.
	public virtual List<Item> GetAdditionalDrops() => new();

	// Called prior to cover removal on the server side.
	public virtual void OnRemoved() { }

	public virtual void OnNeighborChanged() { }

	// Virtual so DetectorCover can hook the change to pulse a Terraria wire.
	public virtual void SetRedstoneSignalOutput(int value)
	{
		if (_redstoneSignalOutput == value) return;
		_redstoneSignalOutput = value;
		CoverHolder.NotifyBlockUpdate();
	}

	public virtual bool CanConnectRedstone() => false;

	// Apply one server-authoritative setting change from the cover settings UI
	// (CoverConfigAction). `field` is cover-defined; field 0 is the universal
	// working-enabled toggle for any IControllable cover. Covers with extra
	// settings override this, handle their own field ids, and call base for 0.
	//
	// Terraria-adaptation hook: upstream routes per-widget edits through LDLib's
	// client-action system; we collapse that to one typed action + this dispatch.
	public virtual void ApplySetting(int field, long value)
	{
		if (field == 0 && this is IControllable controllable)
			controllable.SetWorkingEnabled(value != 0);
	}

	// Text-valued counterpart of ApplySetting - for cover settings whose value
	// is a string (the ender link channel name). Default no-op.
	public virtual void ApplySettingText(int field, string text) { }

	// ===== Filter UI access (cover settings popup) ==========================
	// Covers with a filter expose it here so the cover settings popup +
	// CoverFilterAction can read / mutate it uniformly. A cover that OWNS a
	// SimpleItemFilter overrides UiItemFilter directly; a cover that holds an
	// installable filter item overrides UiItemFilterHandler (UiItemFilter is
	// then derived from whatever filter is installed). Likewise for fluids.
	public virtual Filter.ItemFilterHandler? UiItemFilterHandler => null;
	public virtual Filter.FluidFilterHandler? UiFluidFilterHandler => null;
	public virtual Filter.SimpleItemFilter? UiItemFilter =>
		UiItemFilterHandler?.GetFilter() as Filter.SimpleItemFilter;
	public virtual Filter.SimpleFluidFilter? UiFluidFilter =>
		UiFluidFilterHandler?.GetFilter() as Filter.SimpleFluidFilter;
	// Tag-filter view, parallel to UiItemFilter/UiFluidFilter. Default reads
	// the installed filter via the handler (Conveyor / Pump / Detectors /
	// Ender links). Covers whose filter isn't behind a handler (the
	// upstream-faithful ItemFilterCover / FluidFilterCover with
	// AttachItem-driven lazy filter) override these to return their
	// directly-held TagFilter when it's a tag variant.
	public virtual Filter.TagItemFilter? UiTagItemFilter =>
		UiItemFilterHandler?.GetFilter() as Filter.TagItemFilter;
	public virtual Filter.TagFluidFilter? UiTagFluidFilter =>
		UiFluidFilterHandler?.GetFilter() as Filter.TagFluidFilter;

	// ===== Pipe / capability interception (Phase 4 IO covers) =================

	// Whether a pipe on this side and a pipe on the neighbour may connect.
	public virtual bool CanPipePassThrough() => true;

	// IO covers wrap the machine's handler to intercept transfer. Base =
	// passthrough (the default handler is returned unchanged).
	public virtual IItemHandler? GetItemHandlerCap(IItemHandler? defaultValue) => defaultValue;

	public virtual IFluidHandler? GetFluidHandlerCap(IFluidHandler? defaultValue) => defaultValue;

	// ===== Persistence ========================================================
	// Adaptation of upstream's @SaveField reflection. The base saves the attach
	// item + redstone output; subclasses override to add their own config and
	// MUST call base.Save / base.Load first.

	public virtual void Save(TagCompound tag)
	{
		tag["attachItem"] = ItemIO.Save(AttachItem);
		tag["redstone"] = _redstoneSignalOutput;
	}

	public virtual void Load(TagCompound tag)
	{
		if (tag.ContainsKey("attachItem"))
			AttachItem = ItemIO.Load(tag.GetCompound("attachItem"));
		_redstoneSignalOutput = tag.GetInt("redstone");
	}
}
