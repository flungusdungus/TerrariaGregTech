#nullable enable
using System;
using System.Collections.Generic;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Central insertion of mod-side draw layers. Two anchors with shared fallback:
//   InsertButton - `Info Accessories Bar + 1` (TMI / Questbook).
//   InsertModal  - before `Mouse Text` (panel above HUD, below tooltips / cursor).
// Fallbacks: `Info Accessories Bar + 1` -> `Inventory + 1` -> append.
public static class UILayers
{
	// Higher priority draws on top. ModSystem.ModifyInterfaceLayers fires in
	// unstable order, so InsertModal walks the priority map to land in place.
	private static readonly Dictionary<string, int> _modalPriority = new();

	// Open-state probes for IsAnyHigherPriorityModalOpen - lower-priority
	// modals defer mouse processing while a higher-priority modal is up.
	private static readonly Dictionary<string, Func<bool>> _modalIsOpen = new();

	public static void RegisterModal(string name, Func<bool> isOpen, int priority = 0)
	{
		_modalIsOpen[name] = isOpen;
		if (!_modalPriority.ContainsKey(name))
			_modalPriority[name] = priority;
	}

	// True iff another modal with strictly greater priority is open. Callers
	// skip _ui.Update so click leaks through. Close + Esc still run.
	public static bool IsAnyHigherPriorityModalOpen(string myName)
	{
		if (!_modalPriority.TryGetValue(myName, out var myPriority))
			myPriority = 0;
		foreach (var (name, isOpen) in _modalIsOpen)
		{
			if (name == myName) continue;
			if (!_modalPriority.TryGetValue(name, out var otherPri) || otherPri <= myPriority) continue;
			if (isOpen()) return true;
		}
		return false;
	}

	public static void InsertButton(
		List<GameInterfaceLayer> layers, string layerName, GameInterfaceDrawMethod drawFn)
		=> Insert(layers, layerName, drawFn, AnchorPlacement.AboveAccessoryBar, 0);

	public static void InsertModal(
		List<GameInterfaceLayer> layers, string layerName, GameInterfaceDrawMethod drawFn,
		int priority = 0)
		=> Insert(layers, layerName, drawFn, AnchorPlacement.BelowMouseText, priority);

	private enum AnchorPlacement { AboveAccessoryBar, BelowMouseText }

	private static void Insert(
		List<GameInterfaceLayer> layers, string layerName, GameInterfaceDrawMethod drawFn,
		AnchorPlacement placement, int priority)
	{
		int idx;
		if (placement == AnchorPlacement.BelowMouseText)
		{
			_modalPriority[layerName] = priority;

			idx = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (idx >= 0)
			{
				// Walk back past higher-priority modals so we sit below them.
				// Symmetric - a later higher-priority insert lands after us.
				int insertAt = idx;
				while (insertAt > 0
					&& _modalPriority.TryGetValue(layers[insertAt - 1].Name, out var otherPri)
					&& otherPri > priority)
				{
					insertAt--;
				}
				layers.Insert(insertAt, MakeLayer(layerName, drawFn));
				return;
			}
		}

		idx = layers.FindIndex(l => l.Name == "Vanilla: Info Accessories Bar");
		if (idx < 0) idx = layers.FindIndex(l => l.Name == "Vanilla: Inventory");
		if (idx < 0) idx = layers.Count - 1;
		layers.Insert(idx + 1, MakeLayer(layerName, drawFn));
	}

	private static LegacyGameInterfaceLayer MakeLayer(string name, GameInterfaceDrawMethod draw)
		=> new(name, draw, InterfaceScaleType.UI);
}
