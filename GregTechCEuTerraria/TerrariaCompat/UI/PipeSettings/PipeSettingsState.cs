#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.PipeSettings;

// Pipe settings UIState - 5 plus-shaped sub-panels (UP/LEFT/CENTER/RIGHT/DOWN).
// Each side carries a neighbour header, the Off/Passive/Active mode row, and
// the CoverSettingsUI panel for the cover currently installed there. CENTER
// shows pipe + network stats. Mode -> PipeSideModePacket; cover edits -> CoverActions.
public sealed class PipeSettingsState : UIState
{
	private int _pipeX, _pipeY;
	private PipeKind _layer;
	private UIElement? _outer;

	// Per-side render snapshot - the panel rebuilds when any field changes.
	// Single source of truth; rebuild is deferred out of the mouse-down window
	// so the click that caused the change isn't destroyed mid-press.
	private readonly SideSignature[] _builtSig = new SideSignature[4];

	private readonly struct SideSignature : System.IEquatable<SideSignature>
	{
		public readonly SideNeighbourKind Kind;
		public readonly PipeSideMode      Mode;
		public readonly PipeCoverable.PipeFilterType FilterType;
		public readonly System.Type?      CoverType;
		public readonly int               TransferMode;   // -1 if no active cover

		public SideSignature(SideNeighbourKind kind, PipeSideMode mode,
			PipeCoverable.PipeFilterType filterType, System.Type? coverType, int transferMode)
		{
			Kind = kind; Mode = mode; FilterType = filterType;
			CoverType = coverType; TransferMode = transferMode;
		}

		public bool Equals(SideSignature o) =>
			Kind == o.Kind && Mode == o.Mode && FilterType == o.FilterType &&
			ReferenceEquals(CoverType, o.CoverType) && TransferMode == o.TransferMode;

		public override bool Equals(object? obj) => obj is SideSignature s && Equals(s);
		public override int GetHashCode() =>
			System.HashCode.Combine((int)Kind, (int)Mode, (int)FilterType, CoverType, TransferMode);
		public static bool operator ==(SideSignature a, SideSignature b) =>  a.Equals(b);
		public static bool operator !=(SideSignature a, SideSignature b) => !a.Equals(b);
	}

	private SideSignature CaptureSignature(CoverSide side)
	{
		var pcv = GetSides();
		var cov = pcv?.GetCoverAtSide(side);
		return new SideSignature(
			kind:         ResolveSideKind(side),
			mode:         (pcv is PipeCoverable pipe) ? pipe.GetMode(side) : PipeSideMode.Off,
			filterType:   (PipeCoverable.PipeFilterType)CurrentFilterType(side),
			coverType:    cov?.GetType(),
			transferMode: ActiveTransferMode(cov) is { } t ? (int)t : -1);
	}

	public PipeKind Layer => _layer;
	public int PipeX => _pipeX;
	public int PipeY => _pipeY;

	public void Bind(int x, int y, PipeKind layer)
	{
		_pipeX = x;
		_pipeY = y;
		_layer = layer;
		Rebuild();
	}

	public void Unbind()
	{
		RemoveAllChildren();
		_outer = null;
		for (int i = 0; i < 4; i++)
			_builtSig[i] = default;
	}

	public override void Update(Microsoft.Xna.Framework.GameTime gameTime)
	{
		base.Update(gameTime);

		// Rebuild on signature change, deferred until no mouse button is held
		// so the rebuild doesn't destroy the widget that was just clicked.
		if (Main.mouseLeft || Main.mouseRight) return;
		if (!NeedsRebuild()) return;
		Rebuild();
	}

	private bool NeedsRebuild()
	{
		for (int i = 0; i < 4; i++)
			if (CaptureSignature((CoverSide)i) != _builtSig[i]) return true;
		return false;
	}

	private ICoverable? GetSides() => _layer == PipeKind.Fluid
		? FluidPipeLayerSystem.GetSides(_pipeX, _pipeY)
		: ItemPipeLayerSystem .GetSides(_pipeX, _pipeY);

	// True for Simple Item/Fluid pipes (sentinel MaterialId + IsSimple flag).
	// Drives the 3-state OFF/INSERT/EXTRACT side row, no filter / robot-arm zones.
	private bool IsSimpleCell() => _layer == PipeKind.Fluid
		? (FluidPipeLayerSystem.Pipes.CellAt(_pipeX, _pipeY) is { } fc && fc.IsSimple)
		: (ItemPipeLayerSystem .Pipes.CellAt(_pipeX, _pipeY) is { } ic && ic.IsSimple);

	// Plus geometry - (col,row) in {0..2}^2, corners empty:
	// (1,0)=UP, (0,1)=LEFT, (1,1)=CENTER, (2,1)=RIGHT, (1,2)=DOWN.
	private const int CellW = 280;
	private const int CellH = 210;
	private const int CellPad = 8;
	private const float OuterScale = 1.0f;  // cells already authored at target size

	private void Rebuild()
	{
		RemoveAllChildren();

		int outerW = CellW * 3 + CellPad * 4;
		int outerH = CellH * 3 + CellPad * 4;

		// Transparent container - each cell brings its own panel. PlusHitElement
		// reports a hit only when a child cell contains the point so the empty
		// corner gaps don't swallow clicks via ModalEscape's ContainsPoint test.
		_outer = new PlusHitElement
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			Width  = StyleDimension.FromPixels(outerW * OuterScale),
			Height = StyleDimension.FromPixels(outerH * OuterScale),
		};

		var pcv = GetSides();
		PlaceSideCell(CoverSide.Up,    col: 1, row: 0, pcv);
		PlaceSideCell(CoverSide.Left,  col: 0, row: 1, pcv);
		PlaceCenterCell(pcv,           col: 1, row: 1);
		PlaceSideCell(CoverSide.Right, col: 2, row: 1, pcv);
		PlaceSideCell(CoverSide.Down,  col: 1, row: 2, pcv);

		Append(_outer);
		Recalculate();

		for (int i = 0; i < 4; i++)
			_builtSig[i] = CaptureSignature((CoverSide)i);
	}

	private void PlaceSideCell(CoverSide side, int col, int row, ICoverable? pcv)
	{
		// Only Inventory neighbours need configurable per-side state. Pipe-to-pipe
		// is routed by the network; empty sides have nothing to configure. The
		// signature tracks Kind so a fresh neighbour Rebuilds the cell.
		if (ResolveSideKind(side) != SideNeighbourKind.Inventory) return;

		var cell = BuildSideCell(side, pcv);
		cell.Left = StyleDimension.FromPixels((CellPad + col * (CellW + CellPad)) * OuterScale);
		cell.Top  = StyleDimension.FromPixels((CellPad + row * (CellH + CellPad)) * OuterScale);
		_outer!.Append(cell);
	}

	private void PlaceCenterCell(ICoverable? pcv, int col, int row)
	{
		var cell = BuildCenterCell(pcv);
		cell.Left = StyleDimension.FromPixels((CellPad + col * (CellW + CellPad)) * OuterScale);
		cell.Top  = StyleDimension.FromPixels((CellPad + row * (CellH + CellPad)) * OuterScale);
		_outer!.Append(cell);
	}

	private UIElement BuildSideCell(CoverSide side, ICoverable? pcv)
	{
		// UITerrariaPanel chrome also claims mouseInterface over its rect.
		var cell = new UITerrariaPanel
		{
			Width  = StyleDimension.FromPixels(CellW * OuterScale),
			Height = StyleDimension.FromPixels(CellH * OuterScale),
		};

		// Header: "Up: <neighbour name>". Caller guards kind = Inventory.
		string neighbourName = ResolveNeighbourName(side);
		SideNeighbourKind kind = ResolveSideKind(side);
		cell.Append(new UIText($"{SideWord(side)}: {neighbourName}", 0.75f)
		{
			Left = StyleDimension.FromPixels(6),
			Top  = StyleDimension.FromPixels(4),
		});

		// Mode row - 3 radio buttons.
		const int btnH = 18, btnGap = 4;
		int rowY = 26;
		int rowX = 6;
		int btnW = (CellW - rowX * 2 - btnGap * 2) / 3;

		// Simple pipes: 3-state row only (see SimpleSideMode for the cover-state translation).
		if (IsSimpleCell())
		{
			AppendSimpleModeButton(cell, side, SimpleSideMode.Off,     "Off",     kind, rowX + (btnW + btnGap) * 0, rowY, btnW, btnH);
			AppendSimpleModeButton(cell, side, SimpleSideMode.Insert,  "Insert",  kind, rowX + (btnW + btnGap) * 1, rowY, btnW, btnH);
			AppendSimpleModeButton(cell, side, SimpleSideMode.Extract, "Extract", kind, rowX + (btnW + btnGap) * 2, rowY, btnW, btnH);
			return cell;
		}

		AppendModeButton(cell, side, PipeSideMode.Off,     "Off",     kind, rowX + (btnW + btnGap) * 0, rowY, btnW, btnH);
		AppendModeButton(cell, side, PipeSideMode.Passive, "Passive", kind, rowX + (btnW + btnGap) * 1, rowY, btnW, btnH);
		AppendModeButton(cell, side, PipeSideMode.Active,  "Active",  kind, rowX + (btnW + btnGap) * 2, rowY, btnW, btnH);

		var mode = ReadMode(side);
		var pipeCov = pcv as PipeCoverable;
		if (mode != PipeSideMode.Off && pipeCov is not null)
		{
			// Filter editor reads/writes the active-side cover (filterCover in
			// Passive, robotArm in Active) - both expose UiItemFilterHandler.
			BuildFilterZone(cell, side, pipeCov, x: 6, y: 52, w: 130);

			if (mode == PipeSideMode.Active)
				BuildRobotArmZone(cell, side, pipeCov, x: 144, y: 52, w: 130);
		}

		return cell;
	}

	// Simple-pipe variant - writes via SimplePipeSideSetPacket / reads GetSimpleMode.
	private void AppendSimpleModeButton(UIElement cell, CoverSide side, SimpleSideMode mode,
		string label, SideNeighbourKind kind, int x, int y, int w, int h)
	{
		bool disabled = kind != SideNeighbourKind.Inventory && mode != SimpleSideMode.Off;
		string disabledTooltip = kind switch
		{
			SideNeighbourKind.Pipe => "Pipe is connected on this side.",
			_                      => "No connectable neighbour on this side.",
		};
		var btn = new UITextButton(
			label: () => label,
			onLeft: () => SimplePipeSideSetPacket.Send(_layer, _pipeX, _pipeY, side, mode),
			tooltip: SimpleModeTooltip(mode),
			width: w,
			height: h)
		{
			Left = StyleDimension.FromPixels(x),
			Top  = StyleDimension.FromPixels(y),
			IsActive = () => GetSides() is PipeCoverable pcv && pcv.GetSimpleMode(side) == mode,
			IsDisabled = () => disabled,
			DisabledTooltip = disabledTooltip,
		};
		cell.Append(btn);
	}

	private static string SimpleModeTooltip(SimpleSideMode m) => m switch
	{
		SimpleSideMode.Off     => "Off - this side passes nothing.",
		SimpleSideMode.Insert  => "Insert - pipe pushes into the adjacent storage (allow-all).",
		SimpleSideMode.Extract => "Extract - pipe pulls from the adjacent storage (allow-all).",
		_                      => "",
	};

	private void AppendModeButton(UIElement cell, CoverSide side, PipeSideMode mode,
		string label, SideNeighbourKind kind, int x, int y, int w, int h)
	{
		// `Off` mode is always clickable (lets the player turn off a side
		// regardless of what's on the other end). Other modes require an
		// `Inventory` neighbour.
		bool disabled = kind != SideNeighbourKind.Inventory && mode != PipeSideMode.Off;
		string disabledTooltip = kind switch
		{
			SideNeighbourKind.Pipe => "Pipe is connected on this side.",
			_                      => "No connectable neighbour on this side.",
		};
		var btn = new UITextButton(
			label: () => label,
			onLeft: () => PipeSideModePacket.Send(_layer, _pipeX, _pipeY, side, mode),
			tooltip: ModeTooltip(mode),
			width: w,
			height: h)
		{
			Left = StyleDimension.FromPixels(x),
			Top  = StyleDimension.FromPixels(y),
			IsActive = () => ReadMode(side) == mode,
			IsDisabled = () => disabled,
			DisabledTooltip = disabledTooltip,
		};
		cell.Append(btn);
	}

	private static string ModeTooltip(PipeSideMode m) => m switch
	{
		PipeSideMode.Off     => "Off - this side passes nothing.",
		PipeSideMode.Passive => "Passive - items flow through, gated by the side's filter (no active push or pull).",
		PipeSideMode.Active  => "Active - the robot arm push/pulls at its configured rate, gated by the side's filter.",
		_                    => "",
	};

	// 0=None / 1=Simple / 2=Tag - matches CoverFilterAction.Op.SetFilterType.
	private const int FilterTypeNone   = 0;
	private const int FilterTypeSimple = 1;
	private const int FilterTypeTag    = 2;

	private int CurrentFilterType(CoverSide side)
	{
		if (GetSides() is not PipeCoverable pcv) return FilterTypeNone;
		return (int)pcv.GetFilterType(side);
	}

	private void BuildFilterZone(UIElement cell, CoverSide side, PipeCoverable pcv,
		int x, int y, int w)
	{
		bool isFluid = _layer == PipeKind.Fluid;

		const int btnH = 16, btnGap = 3;
		int btnW = (w - btnGap * 2) / 3;
		AppendFilterTypeButton(cell, side, FilterTypeNone,   "None",   x + (btnW + btnGap) * 0, y, btnW, btnH);
		AppendFilterTypeButton(cell, side, FilterTypeSimple, "Simple", x + (btnW + btnGap) * 1, y, btnW, btnH);
		AppendFilterTypeButton(cell, side, FilterTypeTag,    "Tag",    x + (btnW + btnGap) * 2, y, btnW, btnH);

		// Editor only renders when a filter is installed. Cover is read via
		// GetCoverAtSide per frame; sub-panel rebuilds on cover-identity change.
		int editorY = y + btnH + 4;
		int curType = CurrentFilterType(side);
		if (curType == FilterTypeSimple)
		{
			cell.Append(new UITextButton(
				label:  () => (FilterIsBlackList(pcv.GetCoverAtSide(side), isFluid)) ? "Blacklist" : "Whitelist",
				onLeft:  () => CoverActions.Send(CoverFilterAction.Toggle(side, isFluid, CoverFilterAction.Op.ToggleBlacklist), pcv),
				onRight: () => CoverActions.Send(CoverFilterAction.Toggle(side, isFluid, CoverFilterAction.Op.ToggleBlacklist), pcv),
				tooltip: "Whitelist - only listed " + (isFluid ? "fluids" : "items") + " pass\n" +
				         "Blacklist - listed " + (isFluid ? "fluids" : "items") + " are blocked\n" +
				         "(an empty whitelist blocks everything; default for fresh Active sides)",
				width: w, height: btnH)
			{
				Left = StyleDimension.FromPixels(x),
				Top  = StyleDimension.FromPixels(editorY),
			});

			const int Slot = 28;
			int gridY = editorY + btnH + 4;
			int gridX = x + (w - Slot * 3) / 2;   // center the 3x28 grid in `w`
			for (int i = 0; i < 9; i++)
			{
				int gx = gridX + (i % 3) * Slot;
				int gy = gridY  + (i / 3) * Slot;
				UIElement slot = isFluid
					? new UIPhantomFluidSlot(pcv, side, i)
					: new UIPhantomItemSlot (pcv, side, i);
				slot.Left   = StyleDimension.FromPixels(gx);
				slot.Top    = StyleDimension.FromPixels(gy);
				slot.Width  = StyleDimension.FromPixels(Slot);
				slot.Height = StyleDimension.FromPixels(Slot);
				cell.Append(slot);
			}
		}
		else if (curType == FilterTypeTag)
		{
			// Tag expression - verbatim with CoverSettingsUI.BuildSimpleFilter.
			cell.Append(new UITextField(
				current: () =>
				{
					var c = pcv.GetCoverAtSide(side);
					TagFilter? tag = isFluid ? (TagFilter?)c?.UiTagFluidFilter
					                         : (TagFilter?)c?.UiTagItemFilter;
					return tag?.OreDictFilterExpression ?? "";
				},
				onConfirm: txt => CoverActions.Send(CoverFilterAction.TagExpr(side, isFluid, TagFilter.NormalizeExpression(txt)), pcv),
				maxLength: 64,
				placeholder: "e.g. *dusts/iron",
				tooltip: TagExprHelp)
			{
				Left   = StyleDimension.FromPixels(x),
				Top    = StyleDimension.FromPixels(editorY),
				Width  = StyleDimension.FromPixels(w),
				Height = StyleDimension.FromPixels(18),
			});
		}

		// FilterMode + ManualIOMode cycle - only on ItemFilterCover/FluidFilterCover
		// (Passive). Fields 10 = FilterMode, 11 = ManualIOMode (verbatim with
		// CoverSettingsUI.BuildSimpleFilter).
		if (TryReadFilterModeAllowFlow(pcv.GetCoverAtSide(side), out _, out _))
		{
			const int SimpleSlot = 28;
			int extraY = editorY + (curType == FilterTypeSimple ? btnH + 4 + SimpleSlot * 3 + 4
			                       : curType == FilterTypeTag    ? 18 + 4
			                       : 0);
			cell.Append(new UITextButton(
				() => { TryReadFilterModeAllowFlow(pcv.GetCoverAtSide(side), out var m, out _); return FilterModeShort(m); },
				onLeft:  () => CycleFilterModePipe(pcv, side),
				onRight: () => CycleFilterModePipe(pcv, side),
				tooltip: "Which direction the filter applies to\n"
				       + "Inv->Pipe - filter " + (isFluid ? "fluids" : "items") + " entering the pipe from the connected inventory\n"
				       + "Pipe->Inv - filter " + (isFluid ? "fluids" : "items") + " leaving the pipe into the connected inventory\n"
				       + "Both - filter both directions",
				width: w / 2 - 2, height: btnH)
			{
				Left = StyleDimension.FromPixels(x),
				Top  = StyleDimension.FromPixels(extraY),
			});
			cell.Append(new UITextButton(
				() => { TryReadFilterModeAllowFlow(pcv.GetCoverAtSide(side), out _, out var f); return ManualIoShort(f); },
				onLeft:  () => CycleFilterAllowFlowPipe(pcv, side),
				onRight: () => CycleFilterAllowFlowPipe(pcv, side),
				tooltip: "Behaviour for the direction the filter doesn't apply to\n"
				       + "Block - no flow in that direction\n"
				       + "Filter - apply the filter in that direction too\n"
				       + "Free - pass everything through unchecked",
				width: w / 2 - 2, height: btnH)
			{
				Left = StyleDimension.FromPixels(x + w / 2 + 2),
				Top  = StyleDimension.FromPixels(extraY),
			});
		}
	}

	private static bool FilterIsBlackList(CoverBehavior? cover, bool fluid)
	{
		if (cover is null) return false;
		return fluid ? (cover.UiFluidFilter?.IsBlackList ?? false)
		             : (cover.UiItemFilter ?.IsBlackList ?? false);
	}

	// ItemFilterCover + FluidFilterCover both declare FilterMode + AllowFlow
	// independently; CoverBehavior doesn't surface them - hence the switch.
	private static bool TryReadFilterModeAllowFlow(CoverBehavior? cover, out FilterMode mode, out ManualIOMode flow)
	{
		switch (cover)
		{
			case ItemFilterCover  i: mode = i.FilterMode; flow = i.AllowFlow; return true;
			case FluidFilterCover f: mode = f.FilterMode; flow = f.AllowFlow; return true;
			default: mode = FilterMode.FilterInsert; flow = ManualIOMode.Disabled; return false;
		}
	}

	// Pipe-context wording - directional names instead of upstream's ambiguous
	// Insert/Extract. Underlying enum is upstream-verbatim.
	private static string FilterModeShort(FilterMode m) => m switch
	{
		FilterMode.FilterInsert  => "Inv->Pipe",
		FilterMode.FilterExtract => "Pipe->Inv",
		FilterMode.FilterBoth    => "Both",
		_                        => "?",
	};
	private static string ManualIoShort(ManualIOMode m) => m switch
	{
		ManualIOMode.Disabled   => "Block",
		ManualIOMode.Filtered   => "Filter",
		ManualIOMode.Unfiltered => "Free",
		_                       => "?",
	};
	private static void CycleFilterModePipe(PipeCoverable pcv, CoverSide side)
	{
		if (TryReadFilterModeAllowFlow(pcv.GetCoverAtSide(side), out var mode, out _))
			CoverActions.Send(new CoverConfigAction(side, 10, ((int)mode + 1) % 3), pcv);
	}
	private static void CycleFilterAllowFlowPipe(PipeCoverable pcv, CoverSide side)
	{
		if (TryReadFilterModeAllowFlow(pcv.GetCoverAtSide(side), out _, out var flow))
			CoverActions.Send(new CoverConfigAction(side, 11, ((int)flow + 1) % 3), pcv);
	}

	private void AppendFilterTypeButton(UIElement cell, CoverSide side, int type,
		string label, int x, int y, int w, int h)
	{
		var pcv = GetSides() as PipeCoverable;
		bool isFluid = _layer == PipeKind.Fluid;
		string what = isFluid ? "fluid" : "item";
		cell.Append(new UITextButton(
			label: () => label,
			onLeft: () =>
			{
				if (pcv is not null)
					CoverActions.Send(CoverFilterAction.SetType(side, isFluid, type), pcv);
			},
			tooltip: type switch
			{
				FilterTypeNone   => $"No filter - every {what} passes.",
				FilterTypeSimple => $"Simple - match {what}s by example (3x3 phantom grid + blacklist toggle).",
				FilterTypeTag    => $"Tag - match {what}s by tag expression (e.g. *dusts/iron, *ingots/* & !forge:ingots/iron).",
				_                => "",
			},
			width: w, height: h)
		{
			Left = StyleDimension.FromPixels(x),
			Top  = StyleDimension.FromPixels(y),
			IsActive = () => CurrentFilterType(side) == type,
		});
	}

	private const string TagExprHelp =
		"Accepts complex expressions:\n"
		+ "a & b = AND   *   a | b = OR   *   a ^ b = XOR\n"
		+ "!a = NOT   *   (a) for grouping\n"
		+ "* = wildcard   *   $ = untagged\n"
		+ "Tags are 'namespace:tag/subtype' (forge: assumed by default).\n"
		+ "Type, then press Enter (or click away) to set.";

	// CoverConfigAction field numbers are layer-asymmetric: PumpCover claims
	// field 4 for BucketMode, so FluidRegulatorCover's TransferMode + Limit
	// slide up by one vs RobotArmCover's. Two helpers resolve the right field.
	private static int ActiveTransferModeField(CoverBehavior? cover) => cover switch
	{
		RobotArmCover        => 4,
		FluidRegulatorCover  => 5,
		_                    => 4,
	};
	private static int ActiveLimitField(CoverBehavior? cover) => cover switch
	{
		RobotArmCover        => 5,
		FluidRegulatorCover  => 6,
		_                    => 5,
	};

	// Layer-neutral accessors over RobotArmCover (items) and FluidRegulatorCover
	// (fluids). Io is read via PipeCoverable.ActiveIoAt (shared with the renderer).
	private static TransferMode? ActiveTransferMode(CoverBehavior? cover) => cover switch
	{
		RobotArmCover r        => r.TransferMode,
		FluidRegulatorCover f  => f.TransferMode,
		_                      => null,
	};
	private static long? ActiveGlobalLimit(CoverBehavior? cover) => cover switch
	{
		RobotArmCover r        => r.GlobalTransferLimit,
		FluidRegulatorCover f  => f.GlobalTransferLimit,
		_                      => null,
	};

	private void BuildRobotArmZone(UIElement cell, CoverSide side, PipeCoverable pcv,
		int x, int y, int w)
	{
		const int btnH = 16, btnGap = 3, rowGap = 4;
		int rowY = y;
		bool isFluid = _layer == PipeKind.Fluid;
		string itemOrFluid = isFluid ? "fluids" : "items";

		// Push/Pull radio - field 1 (SetIo) on both ConveyorCover + PumpCover.
		int dirBtnW = (w - btnGap) / 2;
		cell.Append(new UITextButton(
			label: () => "Pull",
			onLeft: () => CoverActions.Send(new CoverConfigAction(side, 1, (long)IO.IN), pcv),
			tooltip: $"Pull - {itemOrFluid} flow from the connected inventory into the pipe (Inv->Pipe).",
			width: dirBtnW, height: btnH)
		{
			Left = StyleDimension.FromPixels(x),
			Top  = StyleDimension.FromPixels(rowY),
			IsActive = () => PipeCoverable.ActiveIoAt(pcv, side) == IO.IN,
		});
		cell.Append(new UITextButton(
			label: () => "Push",
			onLeft: () => CoverActions.Send(new CoverConfigAction(side, 1, (long)IO.OUT), pcv),
			tooltip: $"Push - {itemOrFluid} flow from the pipe into the connected inventory (Pipe->Inv).",
			width: dirBtnW, height: btnH)
		{
			Left = StyleDimension.FromPixels(x + dirBtnW + btnGap),
			Top  = StyleDimension.FromPixels(rowY),
			IsActive = () => PipeCoverable.ActiveIoAt(pcv, side) == IO.OUT,
		});

		// Transfer rate stepper - field 3 = SetTransferRate.
		rowY += btnH + rowGap;
		cell.Append(new UITextButton(
			label: () => "-",
			onLeft:  () => StepRate(side, pcv, -1, false),
			onRight: () => StepRate(side, pcv, -1, true),
			tooltip: "Decrease transfer rate (Shift = x16).",
			width: 18, height: btnH)
		{
			Left = StyleDimension.FromPixels(x),
			Top  = StyleDimension.FromPixels(rowY),
		});
		cell.Append(new UITextField(
			current: () => ((pcv.GetCoverAtSide(side) as IIOCover)?.TransferRate ?? 0).ToString(),
			onConfirm: txt => { if (long.TryParse(txt, out long v))
				CoverActions.Send(new CoverConfigAction(side, 3, System.Math.Max(0, v)), pcv); },
			maxLength: 10,
			filter: ch => ch >= '0' && ch <= '9',
			placeholder: "rate /t",
			tooltip: "Transfer rate per tick (typing replaces the value).")
		{
			Left   = StyleDimension.FromPixels(x + 21),
			Top    = StyleDimension.FromPixels(rowY),
			Width  = StyleDimension.FromPixels(w - 21 - 21),
			Height = StyleDimension.FromPixels(btnH),
		});
		cell.Append(new UITextButton(
			label: () => "+",
			onLeft:  () => StepRate(side, pcv, +1, false),
			onRight: () => StepRate(side, pcv, +1, true),
			tooltip: "Increase transfer rate (Shift = x16).",
			width: 18, height: btnH)
		{
			Left = StyleDimension.FromPixels(x + w - 18),
			Top  = StyleDimension.FromPixels(rowY),
		});

		// Transfer mode row - Any / Exact / Keep Exact. Field 4.
		rowY += btnH + rowGap;
		int tmBtnW = (w - btnGap * 2) / 3;
		AppendTransferModeButton(cell, side, pcv, TransferMode.TransferAny,   "Any",   x + (tmBtnW + btnGap) * 0, rowY, tmBtnW, btnH);
		AppendTransferModeButton(cell, side, pcv, TransferMode.TransferExact, "Exact", x + (tmBtnW + btnGap) * 1, rowY, tmBtnW, btnH);
		AppendTransferModeButton(cell, side, pcv, TransferMode.KeepExact,     "Keep",  x + (tmBtnW + btnGap) * 2, rowY, tmBtnW, btnH);

		// Amount field - visible only when transfer mode != Any. Field
		// number layer-asymmetric (5 for item, 6 for fluid).
		var activeCover = pcv.GetCoverAtSide(side);
		var activeTm = ActiveTransferMode(activeCover);
		if (activeTm is not null && activeTm.Value != TransferMode.TransferAny)
		{
			int limitField = ActiveLimitField(activeCover);
			rowY += btnH + rowGap;
			cell.Append(new UIText("Amount:", 0.55f)
			{
				Left = StyleDimension.FromPixels(x),
				Top  = StyleDimension.FromPixels(rowY + 3),
			});
			cell.Append(new UITextField(
				current: () => (ActiveGlobalLimit(pcv.GetCoverAtSide(side)) ?? 0L).ToString(),
				onConfirm: txt => { if (long.TryParse(txt, out long v))
					CoverActions.Send(new CoverConfigAction(side, limitField, System.Math.Max(0, v)), pcv); },
				maxLength: 10,
				filter: ch => ch >= '0' && ch <= '9',
				placeholder: "per-type amount",
				tooltip: "Per-type amount used by Exact / Keep Exact modes.")
			{
				Left   = StyleDimension.FromPixels(x + 50),
				Top    = StyleDimension.FromPixels(rowY),
				Width  = StyleDimension.FromPixels(w - 50),
				Height = StyleDimension.FromPixels(btnH),
			});
		}
	}

	private static void StepRate(CoverSide side, PipeCoverable pcv, int dir, bool shift)
	{
		if (pcv.GetCoverAtSide(side) is not IIOCover c) return;
		int step = (shift ? 16 : 1) * dir;
		long next = System.Math.Max(0, (long)c.TransferRate + step);
		CoverActions.Send(new CoverConfigAction(side, 3, next), pcv);
	}

	private void AppendTransferModeButton(UIElement cell, CoverSide side, PipeCoverable pcv,
		TransferMode mode, string label, int x, int y, int w, int h)
	{
		// KEEP_EXACT is a no-op on pipe covers in PULL (the pipe-net target has
		// no countable storage - upstream RobotArmCover.DoTransferItems guard).
		// Disabled in this combo for UX clarity; PUSH keeps it enabled.
		bool keepExactInPull = mode == TransferMode.KeepExact
			&& PipeCoverable.ActiveIoAt(pcv, side) == IO.IN;

		cell.Append(new UITextButton(
			label: () => label,
			onLeft: () =>
			{
				int field = ActiveTransferModeField(pcv.GetCoverAtSide(side));
				CoverActions.Send(new CoverConfigAction(side, field, (long)mode), pcv);
			},
			tooltip: mode switch
			{
				TransferMode.TransferAny   => "Any - move whatever fits each tick.",
				TransferMode.TransferExact => "Exact - only move complete batches of 'amount' items per type.",
				TransferMode.KeepExact     => keepExactInPull
					? "Keep Exact - keep at most 'amount' of each type in the connected inventory; refill the shortfall each tick.\n(Disabled in Pull mode: a pipe has no storage to keep items in. Use Exact or Any here, or switch to Push.)"
					: "Keep Exact - keep at most 'amount' of each type in the connected inventory; refill the shortfall each tick.",
				_                          => "",
			},
			width: w, height: h)
		{
			Left = StyleDimension.FromPixels(x),
			Top  = StyleDimension.FromPixels(y),
			IsActive   = () => ActiveTransferMode(pcv.GetCoverAtSide(side)) == mode,
			IsDisabled = () => mode == TransferMode.KeepExact
				&& PipeCoverable.ActiveIoAt(pcv, side) == IO.IN,
		});
	}

	private UIElement BuildCenterCell(ICoverable? pcv)
	{
		var cell = new UITerrariaPanel
		{
			Width  = StyleDimension.FromPixels(CellW * OuterScale),
			Height = StyleDimension.FromPixels(CellH * OuterScale),
		};

		string kindWord = _layer == PipeKind.Fluid ? "Fluid Pipe" : "Item Pipe";
		string material = ResolveMaterialLabel();
		cell.Append(new UIText($"{material} {kindWord}", 0.85f)
		{
			HAlign = 0.5f,
			Top    = StyleDimension.FromPixels(6),
		});
		cell.Append(new UIText($"({_pipeX}, {_pipeY})", 0.55f)
		{
			HAlign = 0.5f,
			Top    = StyleDimension.FromPixels(26),
		});

		cell.Append(new UIDynamicLabel(() => "Throughput: " + ResolveThroughputLabel(), 0.6f)
		{
			Left = StyleDimension.FromPixels(8),
			Top  = StyleDimension.FromPixels(50),
		});

		cell.Append(new UIDynamicLabel(() => "Network: " + ResolveNetworkSizeLabel(), 0.6f)
		{
			Left = StyleDimension.FromPixels(8),
			Top  = StyleDimension.FromPixels(70),
		});
		cell.Append(new UIDynamicLabel(() => TransferredLabelPrefix() + ResolveTransferredLabel(), 0.6f)
		{
			Left = StyleDimension.FromPixels(8),
			Top  = StyleDimension.FromPixels(90),
		});

		return cell;
	}

	private PipeSideMode ReadMode(CoverSide side)
	{
		var pcv = GetSides() as PipeCoverable;
		if (pcv is null) return PipeSideMode.Off;
		return PipeSideModePacket.ReadCurrentMode(pcv, side);
	}

	private SideNeighbourKind ResolveSideKind(CoverSide side)
	{
		var probe = PipeNeighborProbe.Probe(_pipeX, _pipeY, _layer);
		return probe[(int)side];
	}

	// Resolution: MetaMachine display name -> same-kind pipe -> ModTile name -> "Empty".
	private string ResolveNeighbourName(CoverSide side)
	{
		(int dx, int dy) = side switch
		{
			CoverSide.Up    => (0, -1),
			CoverSide.Down  => (0, +1),
			CoverSide.Left  => (-1, 0),
			CoverSide.Right => (+1, 0),
			_               => (0, 0),
		};
		int nx = _pipeX + dx, ny = _pipeY + dy;
		if (nx < 0 || ny < 0 || nx >= Main.maxTilesX || ny >= Main.maxTilesY) return "Empty";

		if (MachineCellResolver.TryFindMachineAt(nx, ny, out var mach))
			return mach.Definition?.Label ?? mach.Name;

		bool sameKindPipe = _layer == PipeKind.Fluid
			? FluidPipeLayerSystem.Pipes.Has(nx, ny)
			: ItemPipeLayerSystem .Pipes.Has(nx, ny);
		if (sameKindPipe) return _layer == PipeKind.Fluid ? "Fluid Pipe" : "Item Pipe";

		var tile = Main.tile[nx, ny];
		if (!tile.HasTile) return "Empty";

		// Vanilla tiles have no cheaply-accessible display name -> fall back to "Tile".
		var modTile = TileLoader.GetTile(tile.TileType);
		if (modTile is not null) return modTile.Name;
		return "Tile";
	}

	private string ResolveMaterialLabel()
	{
		if (_layer == PipeKind.Fluid)
		{
			var cell = FluidPipeLayerSystem.Pipes.CellAt(_pipeX, _pipeY);
			return cell?.MaterialId ?? "";
		}
		else
		{
			var cell = ItemPipeLayerSystem.Pipes.CellAt(_pipeX, _pipeY);
			return cell?.MaterialId ?? "";
		}
	}

	private string ResolveThroughputLabel()
	{
		// Sustained per-second cap at default SimulationSpeed. Items:
		// ceil(TransferRate x 64) per 1-s window (matches ItemNetHandler).
		// Fluids: static rating x 20 (net routing isn't ported yet).
		if (_layer == PipeKind.Fluid)
		{
			var cell = FluidPipeLayerSystem.Pipes.CellAt(_pipeX, _pipeY);
			return cell.HasValue ? $"{cell.Value.Throughput * 20} mB/s" : "-";
		}
		else
		{
			var cell = ItemPipeLayerSystem.Pipes.CellAt(_pipeX, _pipeY);
			if (!cell.HasValue) return "-";
			int perSec = (int)System.Math.Ceiling(cell.Value.TransferRate * 64f);
			return $"{perSec} items/s";
		}
	}

	private string ResolveNetworkSizeLabel()
	{
		if (_layer == PipeKind.Fluid)
		{
			var net = FluidPipeNetSystem.Level?.GetNetFromPos((_pipeX, _pipeY));
			return net is null ? "-" : $"{net.AllNodes.Count} pipes";
		}
		else
		{
			var net = ItemPipeNetSystem.Level?.GetNetFromPos((_pipeX, _pipeY));
			return net is null ? "-" : $"{net.AllNodes.Count} pipes";
		}
	}

	// Item label = throughput counter (resets every 20t); fluid label = live tank.
	private string TransferredLabelPrefix() =>
		_layer == PipeKind.Fluid ? "Pipe contents: " : "Last 1s: ";

	private string ResolveTransferredLabel()
	{
		if (_layer == PipeKind.Fluid)
		{
			// MP client tank contents arrive via FluidPipeStatsPacket (the
			// per-cell state isn't sync'd through PipeCoverSync).
			global::GregTechCEuTerraria.Api.Fluids.FluidStack[]? fluids;
			if (Main.netMode == NetmodeID.MultiplayerClient)
			{
				FluidPipeLayerSystem.ClientTankSnapshots.TryGetValue((_pipeX, _pipeY), out fluids);
			}
			else
			{
				var state = FluidPipeLayerSystem.GetState(_pipeX, _pipeY);
				fluids = state?.GetContainedFluids();
			}
			if (fluids is null) return "empty";
			int total = 0;
			string? name = null;
			foreach (var f in fluids)
			{
				if (f.IsEmpty) continue;
				total += f.Amount;
				name ??= f.Type?.DisplayName ?? f.Type?.Id;
			}
			return total > 0 ? $"{total} mB {name}" : "empty";
		}
		// SP/server reads PipeCoverable; MP client reads the PipeStatsPacket
		// cache (the counter is server-only state, bumped by ItemNetHandler).
		if (Main.netMode == NetmodeID.MultiplayerClient)
		{
			return ItemPipeNetSystem.ClientTransferStats.TryGetValue((_pipeX, _pipeY), out int v)
				? $"{v} items"
				: "0 items";
		}
		var pcv = ItemPipeLayerSystem.GetSides(_pipeX, _pipeY);
		if (pcv is null) return "-";
		return $"{pcv.TransferredItems} items";
	}

	// Plus-shape hit-test: only the 5 actual cells count, so corner gaps don't
	// trigger ModalEscape clobbers on world/inventory clicks.
	private sealed class PlusHitElement : UIElement
	{
		public override bool ContainsPoint(Microsoft.Xna.Framework.Vector2 point)
		{
			if (!base.ContainsPoint(point)) return false;
			foreach (var child in Children)
				if (child.ContainsPoint(point)) return true;
			return false;
		}
	}

	private static string SideWord(CoverSide side) => side switch
	{
		CoverSide.Up    => "Up",
		CoverSide.Down  => "Down",
		CoverSide.Left  => "Left",
		CoverSide.Right => "Right",
		_               => "?",
	};
}
