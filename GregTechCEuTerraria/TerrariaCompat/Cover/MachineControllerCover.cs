#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using Terraria;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.MachineControllerCover - enables/disables the host
// machine (or a side cover) from a Terraria wire signal.
//
// DEVIATION (wire model): Terraria wire is pulse-based, no analog
// level. Each HitWire pulse (deduped per game-tick) flips a LATCHED signal -
// behaves like a lever. minRedstoneStrength dropped (no analog). Upstream's
// doOthersAllowWorking AND-across-siblings is dropped (it relied on level
// compare); last pulse wins when two controllers target one machine.
public class MachineControllerCover : CoverBehavior, IUICover, IWirePulseReceiver
{
	private bool _isInverted;
	private ControllerMode _controllerMode = ControllerMode.Machine;
	private bool _preventPowerFail;

	private bool _signalActive;          // latched, toggled by each wire pulse

	public MachineControllerCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public bool IsInverted => _isInverted;
	public ControllerMode ControllerMode => _controllerMode;

	// Verbatim upstream: RecipeLogic walks the cover container for this flag.
	public bool PreventPowerFail => _preventPowerFail;

	public override bool CanAttach() => base.CanAttach() && GetAllowedModes().Count > 0;

	public override void OnAttached(Item itemStack)
	{
		base.OnAttached(itemStack);
		var allowed = GetAllowedModes();
		SetControllerMode(allowed.Count == 0 ? ControllerMode.Machine : allowed[0]);
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		ResetCurrentControllable();
	}

	public override bool CanConnectRedstone() => true;

	public override void OnNeighborChanged() => UpdateInput();

	public void SetInverted(bool inverted)
	{
		_isInverted = inverted;
		UpdateInput();
	}

	public void SetPreventPowerFail(bool preventPowerFail) => _preventPowerFail = preventPowerFail;

	public void SetControllerMode(ControllerMode mode)
	{
		ResetCurrentControllable();
		_controllerMode = mode;
		UpdateInput();
	}

	// field 1=invert, 2=controller mode, 3=prevent-power-fail. Field 0 unused
	// (this cover isn't IControllable).
	public override void ApplySetting(int field, long value)
	{
		switch (field)
		{
			case 1: SetInverted(value != 0); break;
			case 2: SetControllerMode((ControllerMode)System.Math.Clamp(value, 0, 4)); break;
			case 3: SetPreventPowerFail(value != 0); break;
			default: base.ApplySetting(field, value); break;
		}
	}

	// One call per HitWire pulse on the host machine.
	public void OnWirePulse()
	{
		_signalActive = !_signalActive;
		UpdateInput();
		if (!CoverHolder.IsRemote) CoverHolder.NotifyBlockUpdate();
	}

	// ===== Controller logic ==================================================

	private IControllable? GetControllable(CoverSide? side)
	{
		if (side == null)
			return CoverHolder as IControllable;
		return CoverHolder.GetCoverAtSide(side.Value) as IControllable;
	}

	private void UpdateInput()
	{
		var controllable = GetControllable(SideOf(_controllerMode));
		controllable?.SetWorkingEnabled(ShouldAllowWorking());
	}

	private void ResetCurrentControllable()
	{
		var controllable = GetControllable(SideOf(_controllerMode));
		controllable?.SetWorkingEnabled(true);
	}

	// Latched-on wire disables by default; invert flips.
	private bool ShouldAllowWorking() => _signalActive == _isInverted;

	public List<ControllerMode> GetAllowedModes()
	{
		var list = new List<ControllerMode>();
		foreach (ControllerMode mode in Enum.GetValues<ControllerMode>())
		{
			if (SideOf(mode) == AttachedSide) continue;
			if (GetControllable(SideOf(mode)) != null) list.Add(mode);
		}
		return list;
	}

	private static CoverSide? SideOf(ControllerMode mode) => mode switch
	{
		ControllerMode.CoverUp => CoverSide.Up,
		ControllerMode.CoverDown => CoverSide.Down,
		ControllerMode.CoverLeft => CoverSide.Left,
		ControllerMode.CoverRight => CoverSide.Right,
		_ => null,
	};

	// ===== Persistence =======================================================

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["inverted"] = _isInverted;
		tag["mode"] = (int)_controllerMode;
		tag["preventPowerfail"] = _preventPowerFail;
		tag["signalActive"] = _signalActive;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		_isInverted = tag.GetBool("inverted");
		if (tag.ContainsKey("mode")) _controllerMode = (ControllerMode)tag.GetInt("mode");
		_preventPowerFail = tag.GetBool("preventPowerfail");
		_signalActive = tag.GetBool("signalActive");
	}
}
