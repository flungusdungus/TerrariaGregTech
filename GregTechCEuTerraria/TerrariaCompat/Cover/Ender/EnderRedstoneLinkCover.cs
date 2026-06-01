#nullable enable
using System;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of common.cover.ender.EnderRedstoneLinkCover. Joins a virtual-redstone
// channel: IN writes incoming wire to its slot, OUT drives the wire from the
// channel max.
//
// DEVIATION (wire model): IN latches a binary state per HitWire
// pulse (like MachineControllerCover); OUT pulses on 0<->non-zero transitions
// (like DetectorCover). VirtualRedstone's 0-15 max collapses to binary.
public class EnderRedstoneLinkCover : AbstractEnderLinkCover<VirtualRedstone>, IWirePulseReceiver
{
	private VirtualRedstone? _storage;
	private Guid _uuid = Guid.NewGuid();

	private bool _wireSignalActive;       // latched, flipped per HitWire pulse

	public EnderRedstoneLinkCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	protected override string Identifier() => "ERLink#";
	protected override VirtualRedstone? GetEntry() => _storage;
	protected override EnderEntryType GetEntryType() => EnderEntryType.Redstone;

	protected override void SetEntry(VirtualEntry entry)
	{
		_storage?.RemoveMember(_uuid);
		_storage = (VirtualRedstone)entry;
		_storage.AddMember(_uuid);
	}

	protected override void Transfer()
	{
		if (_storage == null) return;
		switch (Io)
		{
			case IO.IN:  _storage.SetSignal(_uuid, _wireSignalActive ? 15 : 0); break;
			case IO.OUT: SetRedstoneSignalOutput(_storage.Signal); break;
		}
	}

	public override bool CanConnectRedstone() => true;

	// HitWire flips the latched IN signal and pushes immediately.
	public void OnWirePulse()
	{
		_wireSignalActive = !_wireSignalActive;
		if (Io == IO.IN && _storage != null)
			_storage.SetSignal(_uuid, _wireSignalActive ? 15 : 0);
	}

	// OUT-mode bridge - pulse on 0<->non-zero channel-signal transitions.
	public override void SetRedstoneSignalOutput(int value)
	{
		bool wasActive = RedstoneSignalOutput > 0;
		base.SetRedstoneSignalOutput(value);
		bool nowActive = RedstoneSignalOutput > 0;
		if (wasActive != nowActive) CoverWire.TripFootprint(this);
	}

	public override void OnRemoved()
	{
		_storage?.RemoveMember(_uuid);
		base.OnRemoved();
	}

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["uuid"] = _uuid.ToString();
		tag["wireSignal"] = _wireSignalActive;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("uuid") && Guid.TryParse(tag.GetString("uuid"), out var id))
			_uuid = id;
		_wireSignalActive = tag.GetBool("wireSignal");
	}
}
