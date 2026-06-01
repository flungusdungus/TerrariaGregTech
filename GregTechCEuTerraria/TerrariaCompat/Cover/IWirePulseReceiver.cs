#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// A cover that reacts to a Terraria red-wire pulse arriving at its host
// machine. MetaMachineTile.HitWire forwards each (per-tick deduped) pulse to
// every cover implementing this - MachineControllerCover and
// EnderRedstoneLinkCover (IN mode) are the receivers.
public interface IWirePulseReceiver
{
	void OnWirePulse();
}
