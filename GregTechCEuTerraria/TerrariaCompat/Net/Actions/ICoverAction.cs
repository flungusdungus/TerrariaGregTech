#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// One interactive intent against a single cover-holding target (machine or
// pipe). Sibling of IMachineAction - same lifecycle, but Apply takes an
// ICoverable instead of a MetaMachine so the SAME action class can drive
// machine-side covers AND pipe-side covers without forking logic.
//
// Lifecycle (same shape as IMachineAction):
//   1. Client constructs action, calls CoverActions.Send(action, target).
//   2. SP / Server: Send invokes Apply(target) in-process.
//   3. MP client: Send serializes via Write + ships to server. Server reads
//      via parameterless ctor + Read, resolves the ICoverable from the
//      target-kind byte the wire format carries before the action payload,
//      then calls Apply.
//
// Authority: Apply runs only on the authoritative side. Untrusted input -
// validate per-action invariants inside Apply (cover null-check etc.). The
// HandleIncoming dispatcher resolves the target and netmode-guards before
// calling Apply.
public interface ICoverAction
{
	PacketType Type { get; }

	void Write(BinaryWriter w);
	void Read(BinaryReader r);

	// Apply to the authoritative target. The dispatcher resolved the
	// ICoverable from the wire-format target prefix, so concrete actions
	// just call the ICoverable interface - same code path for machine and
	// pipe targets.
	void Apply(ICoverable target, int byWhoAmI);
}
