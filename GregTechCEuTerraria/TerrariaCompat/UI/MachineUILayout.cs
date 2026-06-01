#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Data-only description of a machine's GUI. Pure spec - no entity reference,
// no widget instances. Lookup-by-entity-type at open time produces widgets
// bound to the specific entity (see MachineUIState.Bind).
//
// Sized to upstream's standard 176x166 by default (matches the inventory
// panel Forge/MC mods use). Layouts can override if a machine needs more.
public sealed class MachineUILayout
{
	public int Width { get; init; } = 176;
	public int Height { get; init; } = 166;
	public string Title { get; init; } = "";

	// Coordinates in WidgetSpec / Width / Height are in MC's 16px-tile units.
	// Vanilla Terraria slot textures render at ~52px, so we scale everything
	// up by default so our widgets look proportional alongside the inventory.
	// Per-layout override possible if a machine needs a different feel.
	public float Scale { get; init; } = 2.0f;

	public List<WidgetSpec> Widgets { get; init; } = new();
}
