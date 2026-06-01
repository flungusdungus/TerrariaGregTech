#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Net;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of TieredIOPartMachine. Tiered part + IO direction + WorkingEnabled
// (auto-IO on/off) - base for input/output buses and energy hatches.
// Annotation-driven sync collapses to explicit MachineStateSyncPacket.Broadcast
// calls (no annotation scanner).
public abstract class TieredIOPartMachine : TieredPartMachine, IControllable
{
	public IO Io { get; protected set; }

	// Auto-IO push/pull side; IO.BOTH parts pull from IoDirection.Opposite().
	// Defaults to None (centre "off" of the IO-config cluster) - AutoIOTick
	// short-circuits on None to avoid spewing on every perimeter side.
	public IODirection IoDirection { get; protected set; } = IODirection.None;

	public void SetIoDirection(IODirection direction)
	{
		if (IoDirection == direction) return;
		IoDirection = direction;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	// Items vs fluids - picks the centre icon on the direction-selector cluster.
	public virtual UI.Widgets.UIDirectionSelector.Mode PartIoConfigMode =>
		UI.Widgets.UIDirectionSelector.Mode.Items;

	// Mirrors upstream `workingEnabled` - public surface is via IControllable.
	private bool _workingEnabled = true;

	protected TieredIOPartMachine() : base() { }

	public bool IsWorkingEnabled() => _workingEnabled;

	public void SetWorkingEnabled(bool workingEnabled)
	{
		if (_workingEnabled == workingEnabled) return;
		_workingEnabled = workingEnabled;
		if (IsServer) MachineStateSyncPacket.Broadcast(this);
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["io"]             = (byte)Io;
		tag["ioDirection"]    = (byte)IoDirection;
		tag["workingEnabled"] = _workingEnabled;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("io"))          Io          = (IO)tag.GetByte("io");
		if (tag.ContainsKey("ioDirection")) IoDirection = (IODirection)tag.GetByte("ioDirection");
		_workingEnabled = !tag.ContainsKey("workingEnabled") || tag.GetBool("workingEnabled");
	}
}
