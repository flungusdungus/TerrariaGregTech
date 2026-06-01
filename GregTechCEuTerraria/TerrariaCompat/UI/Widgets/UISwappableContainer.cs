#nullable enable
using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Swaps its CHILDREN when a signature changes - for ONE region of a layout that
// flips between widget sets on entity state (e.g. the item collector's filter
// editor: 3x3 phantom grid vs tag text field). Scoped to its own subtree so it
// doesn't rebuild the whole MachineUIState (which would close cover/browser
// popups). Defers the swap until no mouse button is held (rebuilding mid-click
// destroys press-edge state and re-fires the click every frame).
public sealed class UISwappableContainer : UIElement
{
	private readonly Func<int> _sig;
	private readonly Action<UISwappableContainer> _build;
	private int? _builtSig;

	public UISwappableContainer(Func<int> signature, Action<UISwappableContainer> build)
	{
		_sig = signature;
		_build = build;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		int sig = _sig();
		if (sig == _builtSig) return;
		if (Main.mouseLeft || Main.mouseRight) return; // defer past mouse-down (see header)
		_builtSig = sig;
		RemoveAllChildren();
		UITextField.UnfocusAll(); // text fields in the destroyed subtree
		_build(this);
		Recalculate();
	}
}
