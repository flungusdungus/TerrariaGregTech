#nullable enable
using System.IO;
using GregTechCEuTerraria.TerrariaCompat.Items;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Server-authoritative circuit configuration. Mirrors upstream
// GhostCircuitSlotWidget.handleClientAction (SET_TO_EMPTY / SET_TO_N) - the
// client sends a target value (NoConfig sentinel = -1 or 0..32), the server
// replaces the machine's circuitInventory[0] accordingly.
//
// Absolute target (not delta) so a duplicated packet - double-click, slow
// network, rapid cycling - converges on what the client intended. Clamping
// runs server-side; a hacked client can't push value out of range.
//
// Empty / N representation matches upstream IntCircuitBehaviour.stack(N):
//   NoConfig  -> CircuitInventory[0] = empty
//   0..32     -> CircuitInventory[0] = IntCircuitItem at that Configuration
public sealed class CircuitSetAction : IMachineAction
{
	public PacketType Type => PacketType.CircuitSet;

	// Signed so NoConfig (-1) round-trips. byte width covers 0..32 and -1.
	private int _value;

	public CircuitSetAction() { }
	public CircuitSetAction(int value) { _value = value; }

	public void Write(BinaryWriter w) => w.Write((sbyte)_value);
	public void Read (BinaryReader r) => _value = r.ReadSByte();

	public void Apply(MetaMachine entity, int byWhoAmI)
	{
		if (entity is not Api.Machine.Feature.IHasCircuitSlot holder) return;
		if (!holder.IsCircuitSlotEnabled()) return;
		var inv = holder.CircuitInventory;
		if (inv is null || inv.SlotCount == 0) return;

		int v = _value;
		if (v < UICircuitButton.NoConfig) v = UICircuitButton.NoConfig;
		if (v > UICircuitButton.MaxCircuit) v = UICircuitButton.MaxCircuit;

		if (v == UICircuitButton.NoConfig)
		{
			inv.SetSlot(0, new Item());
		}
		else
		{
			var item = new Item();
			item.SetDefaults(Terraria.ModLoader.ModContent.ItemType<IntCircuitItem>());
			if (item.ModItem is IntCircuitItem ic) ic.Configuration = v;
			inv.SetSlot(0, item);
		}
	}
}
