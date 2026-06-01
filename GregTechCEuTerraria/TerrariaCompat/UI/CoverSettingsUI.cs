#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.TerrariaCompat.Cover;
using GregTechCEuTerraria.TerrariaCompat.Cover.Detector;
using GregTechCEuTerraria.TerrariaCompat.Cover.Ender;
using GregTechCEuTerraria.TerrariaCompat.Cover.Voiding;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Net.Actions;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Per-cover settings-popup builder (cover analogue of MachineLayoutRegistry).
// Widgets resolve the live cover via entity.GetCoverAtSide(side) per access -
// captured CoverBehavior refs go stale across MachineStateSyncPacket rebuilds.
// Mutations are server-authoritative via CoverConfigAction -> CoverBehavior.ApplySetting.
public static class CoverSettingsUI
{
	public static UITerrariaPanel? Build(ICoverable entity, CoverSide side, float scale)
	{
		return entity.GetCoverAtSide(side) switch
		{
			// Advanced voiding extends basic voiding - match first.
			AdvancedItemVoidingCover or AdvancedFluidVoidingCover => BuildAdvancedVoiding(entity, side, scale),
			ItemVoidingCover or FluidVoidingCover => BuildVoiding(entity, side, scale),
			MachineControllerCover => BuildMachineController(entity, side, scale),
			AdvancedItemDetectorCover or AdvancedFluidDetectorCover or AdvancedEnergyDetectorCover
				=> BuildAdvancedDetector(entity, side, scale),
			IEnderLinkCover => BuildEnderLink(entity, side, scale),
			ItemFilterCover  => BuildSimpleFilter(entity, side, scale, fluid: false),
			FluidFilterCover => BuildSimpleFilter(entity, side, scale, fluid: true),
			// Conveyor/pump + their robot-arm/fluid-regulator subclasses.
			ConveyorCover => BuildIOCover(entity, side, scale, fluid: false),
			PumpCover     => BuildIOCover(entity, side, scale, fluid: true),
			ShutterCover  => BuildShutter(entity, side, scale),
			_ => null,
		};
	}

	// 0 = none, 1 = SimpleFilter grid, 2 = TagFilter expression field.
	// MachineUIState polls this and rebuilds the popup when it changes.
	public static int FilterEditorSignature(ICoverable entity, CoverSide side)
	{
		var c = entity.GetCoverAtSide(side);
		if (c is null) return 0;
		if (c.UiItemFilter is not null || c.UiFluidFilter is not null) return 1;
		if (c.UiTagItemFilter is not null || c.UiTagFluidFilter is not null) return 2;
		return 0;
	}

	// Verbatim from upstream lang cover.tag_filter.info.* (section-codes stripped).
	private const string TagFilterInfo =
		"Accepts complex expressions:\n"
		+ "a & b = AND   *   a | b = OR   *   a ^ b = XOR\n"
		+ "!a = NOT   *   (a) for grouping\n"
		+ "* = wildcard   *   $ = untagged\n"
		+ "Tags are 'namespace:tag/subtype'.\n"
		+ "The 'forge:' namespace is assumed if one isn't given.\n"
		+ "Example: *dusts/gold | (gtceu:circuits & !*lv)\n"
		+ "matches all gold dusts, or all circuits except LV ones.\n"
		+ "Type, then press Enter (or click away) to set.";

	// Item/Fluid Filter cover - owns its SimpleFilter directly (no install slot).
	// Popup: title + the shared filter block (matcher grid + toggles).
	private static UITerrariaPanel BuildSimpleFilter(ICoverable entity, CoverSide side, float scale, bool fluid)
	{
		const int Pad = 6, W = 180, BtnH = 16;

		var panel = new UITerrariaPanel();
		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		// FilterMode + ManualIOMode cycle - verbatim port of upstream
		// ItemFilterCover.createUIWidget. Fields 10 / 11 per ApplySetting.
		int rowY = Pad + 16;
		panel.Append(new UITextButton(
			() => FilterModeName((entity.GetCoverAtSide(side) as ItemFilterCover)?.FilterMode ?? FilterMode.FilterInsert),
			onLeft:  () => CycleFilterMode(entity, side),
			onRight: () => CycleFilterMode(entity, side),
			tooltip: "Which direction the filter applies to\n"
			       + "Inv->Machine - filter items entering the machine from the adjacent inventory\n"
			       + "Machine->Inv - filter items leaving the machine into the adjacent inventory\n"
			       + "Both - filter both directions",
			width:  (int)((W - Pad * 2) * scale),
			height: (int)(BtnH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(rowY * scale),
		});
		rowY += BtnH + 4;
		panel.Append(new UITextButton(
			() => ManualIoName((entity.GetCoverAtSide(side) as ItemFilterCover)?.AllowFlow ?? ManualIOMode.Disabled),
			onLeft:  () => CycleFilterAllowFlow(entity, side),
			onRight: () => CycleFilterAllowFlow(entity, side),
			tooltip: "Behaviour for the direction the filter doesn't apply to\n"
			       + "Block - no flow in that direction\n"
			       + "Filter - apply the filter in that direction too\n"
			       + "Free - pass everything through unchecked",
			width:  (int)((W - Pad * 2) * scale),
			height: (int)(BtnH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(rowY * scale),
		});
		rowY += BtnH + 4;

		int blockH = AppendFilterBlock(panel, entity, side, fluid, Pad, rowY, scale);

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((rowY + blockH + Pad) * scale);
		return panel;
	}

	// Machine-context wording; PipeSettingsState uses pipe/inv antipods.
	private static string FilterModeName(FilterMode m) => m switch
	{
		FilterMode.FilterInsert  => "Inv->Machine",
		FilterMode.FilterExtract => "Machine->Inv",
		FilterMode.FilterBoth    => "Both",
		_                        => "?",
	};

	private static void CycleFilterMode(ICoverable entity, CoverSide side)
	{
		if (entity.GetCoverAtSide(side) is ItemFilterCover f)
			CoverActions.Send(new CoverConfigAction(side, 10, ((int)f.FilterMode + 1) % 3), entity);
	}

	private static void CycleFilterAllowFlow(ICoverable entity, CoverSide side)
	{
		if (entity.GetCoverAtSide(side) is ItemFilterCover f)
			CoverActions.Send(new CoverConfigAction(side, 11, ((int)f.AllowFlow + 1) % 3), entity);
	}

	// Basic voiding - single working-enabled toggle (voiding on/off). The cover
	// ships disabled; this toggle is what actually switches voiding on.
	private static UITerrariaPanel BuildVoiding(ICoverable entity, CoverSide side, float scale)
	{
		const int W = 180, Pad = 6, Btn = 22;
		bool fluid = entity.GetCoverAtSide(side) is FluidVoidingCover;

		var panel = new UITerrariaPanel();

		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		panel.Append(new UIPowerToggle(
			() => (entity.GetCoverAtSide(side) as IControllable)?.IsWorkingEnabled() ?? false,
			v => CoverActions.Send(new CoverConfigAction(side, 0, v ? 1 : 0), entity),
			width:  (int)(Btn * scale),
			height: (int)(Btn * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels((Pad + 16) * scale),
		});

		int blockY = Pad + 16 + Btn + 4;
		int blockH = AppendFilterBlock(panel, entity, side, fluid, Pad, blockY, scale);

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((blockY + blockH + Pad) * scale);
		return panel;
	}

	// Advanced voiding - power toggle + voiding-mode selector + keep-limit stepper.
	private static UITerrariaPanel BuildAdvancedVoiding(ICoverable entity, CoverSide side, float scale)
	{
		const int W = 180, Pad = 6, Btn = 22;
		bool fluid = entity.GetCoverAtSide(side) is AdvancedFluidVoidingCover;

		var panel = new UITerrariaPanel();

		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		int row1 = Pad + 16;

		// field 0: voiding on/off
		panel.Append(new UIPowerToggle(
			() => (entity.GetCoverAtSide(side) as IControllable)?.IsWorkingEnabled() ?? false,
			v => CoverActions.Send(new CoverConfigAction(side, 0, v ? 1 : 0), entity),
			width:  (int)(Btn * scale),
			height: (int)(Btn * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(row1 * scale),
		});

		// field 1: voiding mode
		panel.Append(new UITextButton(
			() => Voiding(entity, side)?.VoidingMode == VoidingMode.VoidOverflow ? "Void Overflow" : "Void Any",
			onLeft:  () => CycleVoidingMode(entity, side),
			onRight: () => CycleVoidingMode(entity, side),
			tooltip: "Voiding mode\nVoid Any - delete everything\nVoid Overflow - keep the limit, delete the surplus",
			width:  (int)((W - Pad * 2 - Btn - 4) * scale),
			height: (int)(Btn * scale))
		{
			Left = StyleDimension.FromPixels((Pad + Btn + 4) * scale),
			Top  = StyleDimension.FromPixels(row1 * scale),
		});

		// field 2: VoidOverflow keep-limit
		int row2 = row1 + Btn + 4;
		panel.Append(new UITextButton(
			() => $"Keep: {Voiding(entity, side)?.VoidLimit ?? 0}",
			onLeft:  () => StepVoidLimit(entity, side, +1),
			onRight: () => StepVoidLimit(entity, side, -1),
			tooltip: "VoidOverflow keep-limit (kept per type)\nL +  *  R -  *  hold Shift for a bigger step",
			width:  (int)((W - Pad * 2) * scale),
			height: (int)(16 * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(row2 * scale),
		});

		int blockY = row2 + 16 + 4;
		int blockH = AppendFilterBlock(panel, entity, side, fluid, Pad, blockY, scale);

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((blockY + blockH + Pad) * scale);
		return panel;
	}

	private static IAdvancedVoidingCover? Voiding(ICoverable entity, CoverSide side) =>
		entity.GetCoverAtSide(side) as IAdvancedVoidingCover;

	private static void CycleVoidingMode(ICoverable entity, CoverSide side)
	{
		var v = Voiding(entity, side);
		if (v is null) return;
		int next = ((int)v.VoidingMode + 1) % 2;
		CoverActions.Send(new CoverConfigAction(side, 1, next), entity);
	}

	private static void StepVoidLimit(ICoverable entity, CoverSide side, int dir)
	{
		var v = Voiding(entity, side);
		if (v is null) return;
		bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		bool isFluid = entity.GetCoverAtSide(side) is AdvancedFluidVoidingCover;
		int step = (shift ? (isFluid ? 1000 : 16) : (isFluid ? 100 : 1)) * dir;
		long next = System.Math.Max(0, (long)v.VoidLimit + step);
		CoverActions.Send(new CoverConfigAction(side, 2, next), entity);
	}

	// Machine controller - invert / controller-mode / prevent-power-fail.
	// minRedstoneStrength dropped - Terraria wire has no analog level.
	private static UITerrariaPanel BuildMachineController(ICoverable entity, CoverSide side, float scale)
	{
		const int W = 184, H = 84, Pad = 6, RowH = 16, RowGap = 20;

		var panel = new UITerrariaPanel
		{
			Width  = StyleDimension.FromPixels(W * scale),
			Height = StyleDimension.FromPixels(H * scale),
		};

		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		int btnW = W - Pad * 2;
		int y0 = Pad + 16;

		// field 2: controller mode
		panel.Append(new UITextButton(
			() => "Controls: " + ControllerModeName(Controller(entity, side)?.ControllerMode ?? ControllerMode.Machine),
			onLeft:  () => CycleControllerMode(entity, side, +1),
			onRight: () => CycleControllerMode(entity, side, -1),
			tooltip: "What this cover enables / disables - the host machine, or a cover on one of its sides",
			width:  (int)(btnW * scale),
			height: (int)(RowH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(y0 * scale),
		});

		// field 1: invert
		panel.Append(new UITextButton(
			() => "Inverted: " + ((Controller(entity, side)?.IsInverted ?? false) ? "Yes" : "No"),
			onLeft:  () => ToggleInvert(entity, side),
			onRight: () => ToggleInvert(entity, side),
			tooltip: "Invert the wire signal - by default a pulse pauses the target; inverted, a pulse runs it",
			width:  (int)(btnW * scale),
			height: (int)(RowH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels((y0 + RowGap) * scale),
		});

		// field 3: prevent power-fail
		panel.Append(new UITextButton(
			() => "Prevent power-fail: " + ((Controller(entity, side)?.PreventPowerFail ?? false) ? "Yes" : "No"),
			onLeft:  () => TogglePreventPowerFail(entity, side),
			onRight: () => TogglePreventPowerFail(entity, side),
			tooltip: "Keep the target machine from suspending when it runs out of power mid-recipe",
			width:  (int)(btnW * scale),
			height: (int)(RowH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels((y0 + RowGap * 2) * scale),
		});

		return panel;
	}

	private static MachineControllerCover? Controller(ICoverable entity, CoverSide side) =>
		entity.GetCoverAtSide(side) as MachineControllerCover;

	private static void ToggleInvert(ICoverable entity, CoverSide side)
	{
		var c = Controller(entity, side);
		if (c is not null)
			CoverActions.Send(new CoverConfigAction(side, 1, c.IsInverted ? 0 : 1), entity);
	}

	private static void TogglePreventPowerFail(ICoverable entity, CoverSide side)
	{
		var c = Controller(entity, side);
		if (c is not null)
			CoverActions.Send(new CoverConfigAction(side, 3, c.PreventPowerFail ? 0 : 1), entity);
	}

	// Cycle through the cover's currently-allowed modes (a side with no
	// IControllable target isn't offered), mirroring upstream's selectNextMode.
	private static void CycleControllerMode(ICoverable entity, CoverSide side, int dir)
	{
		var c = Controller(entity, side);
		if (c is null) return;
		var allowed = c.GetAllowedModes();
		if (allowed.Count == 0) return;
		int idx = allowed.IndexOf(c.ControllerMode);
		if (idx < 0) idx = 0;
		idx = ((idx + dir) % allowed.Count + allowed.Count) % allowed.Count;
		CoverActions.Send(new CoverConfigAction(side, 2, (int)allowed[idx]), entity);
	}

	private static string ControllerModeName(ControllerMode m) => m switch
	{
		ControllerMode.Machine    => "this machine",
		ControllerMode.CoverUp    => "cover (up)",
		ControllerMode.CoverDown  => "cover (down)",
		ControllerMode.CoverLeft  => "cover (left)",
		ControllerMode.CoverRight => "cover (right)",
		_                         => "?",
	};

	// Advanced detector covers - invert / min / max / 4th-row mode
	// (latch for item+fluid, EU-vs-percent for energy) + filter for item/fluid.
	private static UITerrariaPanel BuildAdvancedDetector(ICoverable entity, CoverSide side, float scale)
	{
		const int W = 184, Pad = 6, RowH = 16, RowGap = 20;

		var panel = new UITerrariaPanel();
		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		int btnW = W - Pad * 2;
		int y0 = Pad + 16;

		UITextButton Row(int row, System.Func<string> label, System.Action onLeft, System.Action onRight, string tip)
			=> new(label, onLeft, onRight, tip, (int)(btnW * scale), (int)(RowH * scale))
			{
				Left = StyleDimension.FromPixels(Pad * scale),
				Top  = StyleDimension.FromPixels((y0 + RowGap * row) * scale),
			};

		// field 1: invert
		panel.Append(Row(0,
			() => "Inverted: " + ((Detector(entity, side)?.IsInverted ?? false) ? "Yes" : "No"),
			() => ToggleDetectorInvert(entity, side), () => ToggleDetectorInvert(entity, side),
			"Invert the emitted signal"));

		// fields 2/3: min / max threshold
		panel.Append(Row(1,
			() => $"Min: {Detector(entity, side)?.MinValue ?? 0}",
			() => StepDetectorValue(entity, side, 2, +1), () => StepDetectorValue(entity, side, 2, -1),
			"Lower threshold  *  L +  *  R -  *  hold Shift for a bigger step"));
		panel.Append(Row(2,
			() => $"Max: {Detector(entity, side)?.MaxValue ?? 0}",
			() => StepDetectorValue(entity, side, 3, +1), () => StepDetectorValue(entity, side, 3, -1),
			"Upper threshold  *  L +  *  R -  *  hold Shift for a bigger step"));

		// 4th row: latch (item/fluid) or EU/percent (energy).
		if (entity.GetCoverAtSide(side) is AdvancedEnergyDetectorCover)
			panel.Append(Row(3,
				() => "Mode: " + ((entity.GetCoverAtSide(side) as AdvancedEnergyDetectorCover)?.UsePercent ?? true
					? "Percent" : "EU"),
				() => ToggleEnergyPercent(entity, side), () => ToggleEnergyPercent(entity, side),
				"Compare stored EU as a percentage of capacity, or as a raw EU amount"));
		else
			panel.Append(Row(3,
				() => "Latched: " + (DetectorLatch(entity, side) ? "Yes" : "No"),
				() => ToggleDetectorLatch(entity, side), () => ToggleDetectorLatch(entity, side),
				"Latched output - holds until the value crosses the opposite threshold"));

		// item/fluid detectors carry a filter; energy doesn't.
		int bottom = y0 + RowGap * 3 + RowH;
		var cover = entity.GetCoverAtSide(side);
		if (cover is AdvancedItemDetectorCover or AdvancedFluidDetectorCover)
		{
			bool fluid = cover is AdvancedFluidDetectorCover;
			int blockY = bottom + 4;
			bottom = blockY + AppendFilterBlock(panel, entity, side, fluid, Pad, blockY, scale);
		}

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((bottom + Pad) * scale);
		return panel;
	}

	private static IAdvancedDetectorCover? Detector(ICoverable entity, CoverSide side) =>
		entity.GetCoverAtSide(side) as IAdvancedDetectorCover;

	private static void ToggleDetectorInvert(ICoverable entity, CoverSide side)
	{
		var d = Detector(entity, side);
		if (d is not null)
			CoverActions.Send(new CoverConfigAction(side, 1, d.IsInverted ? 0 : 1), entity);
	}

	private static void StepDetectorValue(ICoverable entity, CoverSide side, int field, int dir)
	{
		var d = Detector(entity, side);
		if (d is null) return;
		bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		bool energyPercent = entity.GetCoverAtSide(side) is AdvancedEnergyDetectorCover { UsePercent: true };
		int step = (shift ? (energyPercent ? 10 : 64) : 1) * dir;
		long cur = field == 2 ? d.MinValue : d.MaxValue;
		long next = System.Math.Max(0, cur + step);
		CoverActions.Send(new CoverConfigAction(side, field, next), entity);
	}

	private static bool DetectorLatch(ICoverable entity, CoverSide side) => entity.GetCoverAtSide(side) switch
	{
		AdvancedItemDetectorCover a  => a.IsLatched,
		AdvancedFluidDetectorCover f => f.IsLatched,
		_                            => false,
	};

	private static void ToggleDetectorLatch(ICoverable entity, CoverSide side) =>
		CoverActions.Send(new CoverConfigAction(side, 4, DetectorLatch(entity, side) ? 0 : 1), entity);

	private static void ToggleEnergyPercent(ICoverable entity, CoverSide side)
	{
		if (entity.GetCoverAtSide(side) is AdvancedEnergyDetectorCover e)
			CoverActions.Send(new CoverConfigAction(side, 5, e.UsePercent ? 0 : 1), entity);
	}

	// Ender link covers - channel field + working-enabled + IO direction;
	// item/fluid variants also get a channel-contents view + filter block.
	private static UITerrariaPanel BuildEnderLink(ICoverable entity, CoverSide side, float scale)
	{
		const int W = 188, Pad = 6, Btn = 22, FieldH = 18;

		var panel = new UITerrariaPanel();
		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		int y0 = Pad + 16;

		// field 0 (text): channel name (8-hex)
		panel.Append(new UITextField(
			() => Ender(entity, side)?.ColorStr ?? VirtualEntry.DefaultColor,
			txt => CoverActions.Send(CoverConfigAction.OfText(side, 0, txt), entity),
			maxLength: 8,
			filter: IsHexDigit,
			placeholder: "channel (8 hex)",
			tooltip: "Ender channel - a hex code: digits 0-9 and letters A-F only.\n"
			       + "Up to 8 characters; anything shorter is padded with F\n"
			       + "(so '1A2B' becomes '1A2BFFFF').\n"
			       + "Type, then press Enter (or click away) to set.",
			forceUpper: true)
		{
			Left   = StyleDimension.FromPixels(Pad * scale),
			Top    = StyleDimension.FromPixels(y0 * scale),
			Width  = StyleDimension.FromPixels((W - Pad * 2) * scale),
			Height = StyleDimension.FromPixels(FieldH * scale),
		});

		int y1 = y0 + FieldH + 4;

		// field 0 (long): working-enabled
		panel.Append(new UIPowerToggle(
			() => Ender(entity, side)?.IsWorkingEnabled() ?? false,
			v => CoverActions.Send(new CoverConfigAction(side, 0, v ? 1 : 0), entity),
			width:  (int)(Btn * scale),
			height: (int)(Btn * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(y1 * scale),
		});

		// field 1: IO direction
		panel.Append(new UITextButton(
			() => Ender(entity, side)?.Io == IO.IN
				? "Import (machine -> channel)" : "Export (channel -> machine)",
			onLeft:  () => ToggleEnderIo(entity, side),
			onRight: () => ToggleEnderIo(entity, side),
			tooltip: "Import - pull from the host machine into the channel\n"
			       + "Export - push from the channel into the host machine",
			width:  (int)((W - Pad * 2 - Btn - 4) * scale),
			height: (int)(Btn * scale))
		{
			Left = StyleDimension.FromPixels((Pad + Btn + 4) * scale),
			Top  = StyleDimension.FromPixels(y1 * scale),
		});

		// item/fluid links carry a channel-contents view + filter; redstone doesn't.
		int bottom = y1 + Btn;
		var cover = entity.GetCoverAtSide(side);
		if (cover is EnderItemLinkCover or EnderFluidLinkCover)
		{
			bool fluid = cover is EnderFluidLinkCover;
			// Channel-contents view - server-synced via EnderChannelSyncPacket.
			int viewY = bottom + 6;
			int viewH = fluid ? 16 : 22;
			panel.Append(new UIText("Channel", 0.5f)
			{
				Left = StyleDimension.FromPixels(Pad * scale),
				Top  = StyleDimension.FromPixels((viewY + (viewH - 10) / 2) * scale),
			});
			panel.Append(new UIEnderChannelView(entity, side)
			{
				Left   = StyleDimension.FromPixels((Pad + 52) * scale),
				Top    = StyleDimension.FromPixels(viewY * scale),
				Width  = StyleDimension.FromPixels((fluid ? (W - Pad * 2 - 52) : 22) * scale),
				Height = StyleDimension.FromPixels(viewH * scale),
			});
			bottom = viewY + viewH;

			int blockY = bottom + 6;
			bottom = blockY + AppendFilterBlock(panel, entity, side, fluid, Pad, blockY, scale);
		}

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((bottom + Pad) * scale);
		return panel;
	}

	private static IEnderLinkCover? Ender(ICoverable entity, CoverSide side) =>
		entity.GetCoverAtSide(side) as IEnderLinkCover;

	private static void ToggleEnderIo(ICoverable entity, CoverSide side)
	{
		var e = Ender(entity, side);
		if (e is null) return;
		IO next = e.Io == IO.IN ? IO.OUT : IO.IN;
		CoverActions.Send(new CoverConfigAction(side, 1, (long)next), entity);
	}

	private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');

	// Conveyor (item) / Pump (fluid) covers - IO direction, manual-IO mode,
	// transfer rate (+ bucket unit for pump), + shared filter block.
	private static UITerrariaPanel BuildIOCover(ICoverable entity, CoverSide side, float scale, bool fluid)
	{
		const int W = 196, Pad = 6, RowH = 16, RowGap = 20;

		var panel = new UITerrariaPanel();
		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		int btnW = W - Pad * 2;
		int y0 = Pad + 16;

		UITextButton Cycle(int row, System.Func<string> label, System.Action act, string tip)
			=> new(label, act, act, tip, (int)(btnW * scale), (int)(RowH * scale))
			{
				Left = StyleDimension.FromPixels(Pad * scale),
				Top  = StyleDimension.FromPixels((y0 + RowGap * row) * scale),
			};

		// field 1: IO direction
		panel.Append(Cycle(0,
			() => "Mode: " + (Io(entity, side)?.Io == IO.IN
				? "Import (adjacent -> machine)" : "Export (machine -> adjacent)"),
			() => ToggleIoCoverIo(entity, side),
			"Import - pull from the adjacent inventory into this machine\n"
			+ "Export - push from this machine into the adjacent inventory"));

		// field 2: manual-IO mode
		panel.Append(Cycle(1,
			() => "Manual I/O: " + ManualIoName(Io(entity, side)?.ManualIOMode ?? ManualIOMode.Disabled),
			() => CycleManualIo(entity, side),
			"How transfer behaves on the filter's non-filtered direction:\n"
			+ "Disabled - blocked  *  Filtered - the filter applies  *  Unfiltered - all pass"));

		// field 3: transfer rate (stepper)
		panel.Append(new UITextButton(
			() => RateLabel(entity, side, fluid),
			onLeft:  () => StepTransferRate(entity, side, +1, fluid),
			onRight: () => StepTransferRate(entity, side, -1, fluid),
			tooltip: "Transfer rate  *  L +  *  R -  *  hold Shift for a bigger step",
			width:  (int)(btnW * scale),
			height: (int)(RowH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels((y0 + RowGap * 2) * scale),
		});

		int rows = 3;
		if (fluid)
		{
			// field 4 (pump only): bucket/mB display unit
			panel.Append(Cycle(3,
				() => "Unit: " + ((entity.GetCoverAtSide(side) as Cover.PumpCover)?.BucketMode == BucketMode.Bucket
					? "Buckets" : "milliBuckets"),
				() => CycleBucketMode(entity, side),
				"Display unit for the transfer rate above"));
			rows = 4;
		}

		// RobotArm/FluidRegulator: TransferMode + per-type limit.
		if (entity.GetCoverAtSide(side) is ITransferModeCover)
		{
			int modeField  = fluid ? 5 : 4;
			int limitField = fluid ? 6 : 5;
			panel.Append(Cycle(rows,
				() => "Transfer: " + TransferModeName(
					(entity.GetCoverAtSide(side) as ITransferModeCover)?.TransferMode ?? TransferMode.TransferAny),
				() => CycleTransferMode(entity, side, modeField),
				"Any - move whatever fits\n"
				+ "Exact - move only whole configured per-type stacks\n"
				+ "Keep Exact - top the target up to the configured per-type amount"));
			rows++;
			panel.Append(new UITextField(
				() => ((entity.GetCoverAtSide(side) as ITransferModeCover)?.GlobalTransferLimit ?? 0).ToString(),
				txt => { if (long.TryParse(txt, out long v))
					CoverActions.Send(new CoverConfigAction(side, limitField, v), entity); },
				maxLength: 10,
				filter: ch => ch >= '0' && ch <= '9',
				placeholder: "per-type limit",
				tooltip: "Per-type amount used by the Exact / Keep Exact modes"
				       + (fluid ? "  (in mB)" : ""))
			{
				Left   = StyleDimension.FromPixels(Pad * scale),
				Top    = StyleDimension.FromPixels((y0 + RowGap * rows) * scale),
				Width  = StyleDimension.FromPixels(btnW * scale),
				Height = StyleDimension.FromPixels(18 * scale),
			});
			rows++;
		}

		int bottom = y0 + RowGap * (rows - 1) + RowH;
		int blockY = bottom + 4;
		bottom = blockY + AppendFilterBlock(panel, entity, side, fluid, Pad, blockY, scale);

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((bottom + Pad) * scale);
		return panel;
	}

	private static IIOCover? Io(ICoverable entity, CoverSide side) =>
		entity.GetCoverAtSide(side) as IIOCover;

	private static string ManualIoName(ManualIOMode m) => m switch
	{
		ManualIOMode.Disabled   => "Block",
		ManualIOMode.Filtered   => "Filter",
		ManualIOMode.Unfiltered => "Free",
		_                       => "?",
	};

	private static void ToggleIoCoverIo(ICoverable entity, CoverSide side)
	{
		var c = Io(entity, side);
		if (c is not null)
			CoverActions.Send(new CoverConfigAction(side, 1, (long)(c.Io == IO.IN ? IO.OUT : IO.IN)), entity);
	}

	private static void CycleManualIo(ICoverable entity, CoverSide side)
	{
		var c = Io(entity, side);
		if (c is not null)
			CoverActions.Send(new CoverConfigAction(side, 2, ((int)c.ManualIOMode + 1) % 3), entity);
	}

	private static void CycleBucketMode(ICoverable entity, CoverSide side)
	{
		if (entity.GetCoverAtSide(side) is Cover.PumpCover p)
			CoverActions.Send(new CoverConfigAction(side, 4, ((int)p.BucketMode + 1) % 2), entity);
	}

	private static bool BucketUnit(ICoverable entity, CoverSide side) =>
		entity.GetCoverAtSide(side) is Cover.PumpCover { BucketMode: BucketMode.Bucket };

	private static string RateLabel(ICoverable entity, CoverSide side, bool fluid)
	{
		int rate = Io(entity, side)?.TransferRate ?? 0;
		if (!fluid) return $"Transfer rate: {rate} /t";
		return BucketUnit(entity, side)
			? $"Transfer rate: {rate / 1000} B/t"
			: $"Transfer rate: {rate} mB/t";
	}

	private static void StepTransferRate(ICoverable entity, CoverSide side, int dir, bool fluid)
	{
		var c = Io(entity, side);
		if (c is null) return;
		bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
		int unit = fluid && BucketUnit(entity, side) ? 1000 : 1;
		int step = unit * (shift ? 16 : 1) * dir;
		long next = System.Math.Max(0, (long)c.TransferRate + step);
		CoverActions.Send(new CoverConfigAction(side, 3, next), entity);
	}

	private static string TransferModeName(TransferMode m) => m switch
	{
		TransferMode.TransferAny   => "Any",
		TransferMode.TransferExact => "Exact",
		TransferMode.KeepExact     => "Keep Exact",
		_                          => "?",
	};

	private static void CycleTransferMode(ICoverable entity, CoverSide side, int field)
	{
		if (entity.GetCoverAtSide(side) is ITransferModeCover c)
			CoverActions.Send(new CoverConfigAction(side, field, ((int)c.TransferMode + 1) % 3), entity);
	}

	// Shutter - one Open/Closed toggle on IControllable field 0.
	private static UITerrariaPanel BuildShutter(ICoverable entity, CoverSide side, float scale)
	{
		const int W = 200, Pad = 6, RowH = 16;

		var panel = new UITerrariaPanel();
		panel.Append(new UIText(Title(entity, side), 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels(Pad * scale),
		});

		panel.Append(new UITextButton(
			() => "Shutter: " + (((entity.GetCoverAtSide(side) as IControllable)?.IsWorkingEnabled() ?? true)
				? "Closed (blocks I/O)" : "Open"),
			onLeft:  () => ToggleShutter(entity, side),
			onRight: () => ToggleShutter(entity, side),
			tooltip: "Closed - no items / fluids may pass this side\nOpen - transfer flows normally",
			width:  (int)((W - Pad * 2) * scale),
			height: (int)(RowH * scale))
		{
			Left = StyleDimension.FromPixels(Pad * scale),
			Top  = StyleDimension.FromPixels((Pad + 16) * scale),
		});

		panel.Width  = StyleDimension.FromPixels(W * scale);
		panel.Height = StyleDimension.FromPixels((Pad + 16 + RowH + Pad) * scale);
		return panel;
	}

	private static void ToggleShutter(ICoverable entity, CoverSide side)
	{
		if (entity.GetCoverAtSide(side) is IControllable c)
			CoverActions.Send(new CoverConfigAction(side, 0, c.IsWorkingEnabled() ? 0 : 1), entity);
	}

	// Shared filter editor: optional install slot + 3x3 matcher grid +
	// blacklist/ignore-NBT toggles. Returns the block height in logical px.
	// Live-resolved per frame; inert until a filter is present.
	private static int AppendFilterBlock(
		UITerrariaPanel panel, ICoverable entity, CoverSide side, bool fluid, int x, int y, float scale)
	{
		const int Slot = 22, ToggleW = 96, ToggleH = 16;

		var cover = entity.GetCoverAtSide(side);
		bool hasHandler = cover is not null &&
			(fluid ? cover.UiFluidFilterHandler is not null : cover.UiItemFilterHandler is not null);

		int gridY = y;

		// Install slot - handler covers only (detector / voiding / ender).
		if (hasHandler)
		{
			panel.Append(new UIFilterItemSlot(entity, side, fluid)
			{
				Left   = StyleDimension.FromPixels(x * scale),
				Top    = StyleDimension.FromPixels(y * scale),
				Width  = StyleDimension.FromPixels(Slot * scale),
				Height = StyleDimension.FromPixels(Slot * scale),
			});
			panel.Append(new UIText("Filter item", 0.5f)
			{
				Left = StyleDimension.FromPixels((x + Slot + 6) * scale),
				Top  = StyleDimension.FromPixels((y + 7) * scale),
			});
			gridY = y + Slot + 4;
		}

		// SimpleFilter -> grid + toggles. TagFilter -> expression field. Nothing
		// installed -> nothing. Popup rebuilds via FilterEditorSignature change.
		bool hasSimple = fluid ? cover?.UiFluidFilter is not null : cover?.UiItemFilter is not null;
		if (!hasSimple)
		{
			TagFilter? tag = fluid ? (TagFilter?)cover?.UiTagFluidFilter
			                       : (TagFilter?)cover?.UiTagItemFilter;
			if (tag is null)
				return gridY - y;

			// 64-char expression field; setter runs NormalizeExpression before SetOreDict.
			panel.Append(new UITextField(
				() => (fluid
						? (TagFilter?)entity.GetCoverAtSide(side)?.UiTagFluidFilter
						: (TagFilter?)entity.GetCoverAtSide(side)?.UiTagItemFilter)
					?.OreDictFilterExpression ?? "",
				txt => CoverActions.Send(
					CoverFilterAction.TagExpr(side, fluid, TagFilter.NormalizeExpression(txt)), entity),
				maxLength: 64,
				placeholder: "tag expression  *  e.g.  *dusts/gold | !*lv",
				tooltip: TagFilterInfo)
			{
				Left   = StyleDimension.FromPixels(x * scale),
				Top    = StyleDimension.FromPixels(gridY * scale),
				Width  = StyleDimension.FromPixels((3 * Slot + 6 + ToggleW) * scale),
				Height = StyleDimension.FromPixels(18 * scale),
			});
			return (gridY - y) + 18;
		}

		for (int i = 0; i < 9; i++)
		{
			int gx = x + (i % 3) * Slot;
			int gy = gridY + (i / 3) * Slot;
			UIElement slot = fluid
				? new UIPhantomFluidSlot(entity, side, i)
				: new UIPhantomItemSlot(entity, side, i);
			slot.Left   = StyleDimension.FromPixels(gx * scale);
			slot.Top    = StyleDimension.FromPixels(gy * scale);
			slot.Width  = StyleDimension.FromPixels(Slot * scale);
			slot.Height = StyleDimension.FromPixels(Slot * scale);
			panel.Append(slot);
		}

		int tx = x + 3 * Slot + 6;   // toggles, right of the grid
		panel.Append(new UITextButton(
			() => FilterBlackList(entity, side, fluid) ? "Mode: Blacklist" : "Mode: Whitelist",
			onLeft:  () => ToggleFilterBlacklist(entity, side, fluid),
			onRight: () => ToggleFilterBlacklist(entity, side, fluid),
			tooltip: "Whitelist - only listed types pass\nBlacklist - listed types are blocked",
			width:  (int)(ToggleW * scale),
			height: (int)(ToggleH * scale))
		{
			Left = StyleDimension.FromPixels(tx * scale),
			Top  = StyleDimension.FromPixels(gridY * scale),
		});
		panel.Append(new UITextButton(
			() => "Ignore NBT: " + (FilterIgnoreNbt(entity, side, fluid) ? "Yes" : "No"),
			onLeft:  () => ToggleFilterIgnoreNbt(entity, side, fluid),
			onRight: () => ToggleFilterIgnoreNbt(entity, side, fluid),
			tooltip: "Ignore item / fluid NBT data when matching",
			width:  (int)(ToggleW * scale),
			height: (int)(ToggleH * scale))
		{
			Left = StyleDimension.FromPixels(tx * scale),
			Top  = StyleDimension.FromPixels((gridY + ToggleH + 4) * scale),
		});

		return (gridY - y) + 3 * Slot;
	}

	private static bool FilterBlackList(ICoverable entity, CoverSide side, bool fluid)
	{
		var c = entity.GetCoverAtSide(side);
		return fluid ? (c?.UiFluidFilter?.IsBlackList ?? false)
		             : (c?.UiItemFilter?.IsBlackList ?? false);
	}

	private static bool FilterIgnoreNbt(ICoverable entity, CoverSide side, bool fluid)
	{
		var c = entity.GetCoverAtSide(side);
		return fluid ? (c?.UiFluidFilter?.IgnoreNbt ?? false)
		             : (c?.UiItemFilter?.IgnoreNbt ?? false);
	}

	private static void ToggleFilterBlacklist(ICoverable entity, CoverSide side, bool fluid) =>
		CoverActions.Send(CoverFilterAction.Toggle(side, fluid, CoverFilterAction.Op.ToggleBlacklist), entity);

	private static void ToggleFilterIgnoreNbt(ICoverable entity, CoverSide side, bool fluid) =>
		CoverActions.Send(CoverFilterAction.Toggle(side, fluid, CoverFilterAction.Op.ToggleIgnoreNbt), entity);

	// e.g. "Item Voiding Cover  *  Up". Shared by every cover popup.
	private static string Title(ICoverable entity, CoverSide side)
	{
		var cover = entity.GetCoverAtSide(side);
		string name = cover is null || cover.AttachItem.IsAir ? "Cover" : cover.AttachItem.Name;
		return $"{name}  *  {side}";
	}
}
