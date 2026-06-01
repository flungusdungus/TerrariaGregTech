#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.Machine;

// Client-side registry of active machine loop sounds. Consumers: ghost
// cleanup (Sweep), world reset (ClearAll), profiler-gated debug gizmo.
internal static class MachineLoopSoundRegistry
{
	private static readonly HashSet<MachineAudioTracker> _active = new();

	public static void Register(MachineAudioTracker tracker) => _active.Add(tracker);
	public static void Unregister(MachineAudioTracker tracker) => _active.Remove(tracker);

	public static IReadOnlyCollection<MachineAudioTracker> Active => _active;

	// Prune stopped trackers + mark-stopped any whose owner is gone.
	public static void Sweep()
	{
		if (_active.Count == 0) return;
		List<MachineAudioTracker>? dead = null;
		foreach (var t in _active)
		{
			if (!t.ShouldKeepPlaying) { (dead ??= new()).Add(t); continue; }
			if (!t.OwnerStillPlaced()) { t.MarkStopped(); (dead ??= new()).Add(t); }
		}
		if (dead != null)
			foreach (var t in dead) _active.Remove(t);
	}

	// Snapshot copy - MarkStopped unregisters and would mutate _active.
	public static void ClearAll()
	{
		if (_active.Count == 0) return;
		foreach (var t in new List<MachineAudioTracker>(_active))
			t.MarkStopped();
		_active.Clear();
	}
}
