#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Machine;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// One interactive intent against a single MetaMachine. Send invokes Apply
// in-process on SP/server, or ships Write's payload to the server (which Reads
// into a parameterless-ctor instance and Applies) on an MP client - both end at
// the same Apply, so SP and MP can't diverge.
//
// Apply runs ONLY on the authoritative side and MUST treat the action as
// untrusted (validate slot indices / invariants - a hacked client sends
// anything). HandleIncoming pre-guards netmode / entity / viewer membership.
public interface IMachineAction
{
	PacketType Type { get; }

	// Payload only - the type byte + entity position are written by Send.
	void Write(BinaryWriter w);
	void Read(BinaryReader r);

	// Authoritative-side only; caller handles the post-apply broadcast.
	void Apply(MetaMachine entity, int byWhoAmI);
}
