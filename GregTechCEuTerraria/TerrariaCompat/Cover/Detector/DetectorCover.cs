#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Detector;

// Port of common.cover.detector.DetectorCover - polls the host every 20 ticks
// and emits redstone. Lives in TerrariaCompat (not Api) because the emit path
// is a Terraria wire pulse.
//
// DEVIATION (wire model): concrete update()s compute the verbatim
// upstream 0-15 value into _redstoneSignalOutput; the binary collapse + pulse
// happens once in SetRedstoneSignalOutput. onScrewdriverClick dropped (UI-only).
public abstract class DetectorCover : CoverBehavior, IControllable
{
	// Verbatim upstream quirk: kept so MachineControllerCover can target the
	// detector as an IControllable. Concrete update()s never gate on it.
	protected bool _isWorkingEnabled = true;
	private bool _isInverted;
	private TickableSubscription? _subscription;

	protected DetectorCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public bool IsInverted => _isInverted;

	public void SetInverted(bool inverted)
	{
		_isInverted = inverted;
		if (!CoverHolder.IsRemote) CoverHolder.NotifyBlockUpdate();
	}

	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool isWorkingAllowed) => _isWorkingEnabled = isWorkingAllowed;

	// field 1 = invert (shared by every detector). Advanced detectors override
	// for their own fields and call base for 1.
	public override void ApplySetting(int field, long value)
	{
		if (field == 1) SetInverted(value != 0);
		else base.ApplySetting(field, value);
	}

	public override void OnLoad()
	{
		base.OnLoad();
		_subscription = CoverHolder.SubscribeServerTick(Update);
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		_subscription?.Unsubscribe();
		_subscription = null;
	}

	protected abstract void Update();

	public override bool CanConnectRedstone() => true;
	public override bool CanPipePassThrough() => false;

	// Binary collapse + Terraria-wire bridge: base stores the 0-15 value, we
	// pulse on 0<->non-zero transitions.
	public override void SetRedstoneSignalOutput(int value)
	{
		bool wasActive = RedstoneSignalOutput > 0;
		base.SetRedstoneSignalOutput(value);
		bool nowActive = RedstoneSignalOutput > 0;
		if (wasActive != nowActive) CoverWire.TripFootprint(this);
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["inverted"] = _isInverted;
		tag["coverWorkingEnabled"] = _isWorkingEnabled;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		_isInverted = tag.GetBool("inverted");
		if (tag.ContainsKey("coverWorkingEnabled")) _isWorkingEnabled = tag.GetBool("coverWorkingEnabled");
	}
}
