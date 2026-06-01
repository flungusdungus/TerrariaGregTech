#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Cable;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Tiles.Machines.Transformers;

// 1:1 port of com.gregtechceu.gtceu.common.machine.electric.TransformerMachine.
// Steps energy between two adjacent voltage tiers via ONE asymmetric
// NotifiableEnergyContainer (the buffer mediates; no per-tick conversion logic).
//
// DEVIATION: no facing - the 2x2 back is split into two FIXED faces,
// UPPER row = HV, LOWER row = LV (EnergyFaceForCell). isTransformUp (screwdriver
// RMB) only swaps which face is in vs out; HV is always the upper row:
//   Down (default): HV in (V*4, baseAmp A) -> LV out (V, 4*baseAmp A)
//   Up:             LV in (V, 4*baseAmp A)  -> HV out (V*4, baseAmp A)
//
// Four baseAmp variants (1/2/4/16); registered ULV..OpV (steps tier<->tier+1, so MAX has none).
public class TransformerMachine : TieredEnergyMachine
{
	// 2x2 footprint's two faces: HV = upper row, LV = lower row.
	public const IODirection HvFace = IODirection.Up;
	public const IODirection LvFace = IODirection.Down;

	public TransformerMachine() { }
	public TransformerMachine(VoltageTier tier) : base(tier) { }

	protected override string Label => Definition?.Label ?? "Transformer";

	// Upstream baseAmp (1/2/4/16) from the bound MachineDefinition.
	public virtual int BaseAmp => Definition?.BaseAmp ?? 1;

	// ULV..OpV (steps tier<->tier+1, so MAX has none).
	public static readonly VoltageTier[] Tiers =
	{
		VoltageTier.ULV, VoltageTier.LV,  VoltageTier.MV,  VoltageTier.HV,
		VoltageTier.EV,  VoltageTier.IV,  VoltageTier.LuV, VoltageTier.ZPM,
		VoltageTier.UV,  VoltageTier.UHV, VoltageTier.UEV, VoltageTier.UIV,
		VoltageTier.UXV, VoltageTier.OpV,
	};

	// Upstream isTransformUp (@SaveField @SyncToClient). false = Down (HV in, LV out).
	private bool _isTransformUp;
	public bool IsTransformUp => _isTransformUp;

	// V[tier] * 8 * lowAmperage (lowAmperage = baseAmp*4); identical both directions.
	public override long EnergyCapacity =>
		VoltageTiers.Voltage(Tier) * 8L * (BaseAmp * 4L);

	public override bool CanAccept  => true;
	public override bool CanExtract => true;

	// Upper footprint row -> HV face, lower row -> LV (Position is top-left origin).
	public override IODirection EnergyFaceForCell(int cx, int cy) =>
		cy == Position.Y ? HvFace : LvFace;

	// getEnergyContainer(tier, amps). ApplyTransformConfig immediately resets it
	// to the direction-correct figures, so the ctor values are transient.
	protected override NotifiableEnergyContainer CreateEnergyContainer()
	{
		long v = VoltageTiers.Voltage(Tier);
		var c = new NotifiableEnergyContainer(v * 8L, v * 4L, BaseAmp, v, 4L * BaseAmp);
		ApplyTransformConfig(c, _isTransformUp);
		return c;
	}

	// updateEnergyContainer(isTransformUp) - resets voltage/amperage + per-face I/O.
	private void ApplyTransformConfig(NotifiableEnergyContainer c, bool up)
	{
		long v = VoltageTiers.Voltage(Tier);
		int lowAmperage = BaseAmp * 4;
		if (up)
		{
			// storage = n amp high; input = tier; amperage = 4n; output = tier*4; amperage = n
			c.ResetBasicInfo(v * 8L * lowAmperage, v, lowAmperage, v * 4L, BaseAmp);
			c.SideInputCondition  = s => s == LvFace && WorkingEnabled;
			c.SideOutputCondition = s => s == HvFace && WorkingEnabled;
		}
		else
		{
			// storage = n amp high; input = tier*4; amperage = n; output = tier; amperage = 4n
			c.ResetBasicInfo(v * 8L * lowAmperage, v * 4L, BaseAmp, v, lowAmperage);
			c.SideInputCondition  = s => s == HvFace && WorkingEnabled;
			c.SideOutputCondition = s => s == LvFace && WorkingEnabled;
		}
	}

	// setTransformUp - server-authoritative (clients pick it up via state sync).
	public void SetTransformUp(bool up)
	{
		if (_isTransformUp == up || !IsServer) return;
		_isTransformUp = up;
		ApplyTransformConfig(EnergyContainer, up);
		// Faces swapped producer<->consumer; force a re-link or the net keeps
		// routing per the old direction.
		EnergyNetSystem.MarkEndpointsDirty();
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["transformUp"] = _isTransformUp;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		_isTransformUp = tag.ContainsKey("transformUp") && tag.GetBool("transformUp");
		// Re-apply after load - the container was created before the saved direction was known.
		ApplyTransformConfig(EnergyContainer, _isTransformUp);
	}

	public override void AppendTooltip(List<string> lines)
	{
		base.AppendTooltip(lines);
		var c = EnergyContainer;
		string inFace  = _isTransformUp ? "bottom" : "top";
		string outFace = _isTransformUp ? "top"    : "bottom";
		string arrow   = _isTransformUp ? "Step Up"   : "Step Down";
		lines.Add($"{arrow}: IN {inFace} {c.InputVoltage:N0}V @{c.InputAmperage}A  ->  OUT {outFace} {c.OutputVoltage:N0}V @{c.OutputAmperage}A");
		lines.Add("Screwdriver-RMB to flip direction");
	}
}
