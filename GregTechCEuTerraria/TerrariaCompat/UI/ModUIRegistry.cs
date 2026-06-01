#nullable enable
using System;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Mutual-exclusion for mod-side modal panels (machine UI, pipe settings,
// magnet, ...): opening one closes whichever was active, so they don't stack.
// Vanilla does this via per-UI globals (Player.chest, ...); we use one static
// "active closer". Each modal calls OnOpen(Close) in its open path and
// OnClose(Close) in its close path.
public static class ModUIRegistry
{
	private static Action? _activeCloser;

	public static void OnOpen(Action closeSelf)
	{
		// Same panel reopening - keep the registration.
		if (ReferenceEquals(_activeCloser, closeSelf)) return;

		// Clear _active BEFORE calling prev() so its OnClose doesn't see itself
		// as active and no-op the registry update.
		if (_activeCloser is { } prev)
		{
			_activeCloser = null;
			prev();
		}
		_activeCloser = closeSelf;
	}

	public static void OnClose(Action closeSelf)
	{
		if (ReferenceEquals(_activeCloser, closeSelf)) _activeCloser = null;
	}
}
