#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Last-hovered item or fluid for the machine UI's "relevant recipes" panel.
// Items come from Main.HoverItem (covers vanilla + our UISlot). Fluids come
// from UIFluidSlot.SetFluid. Last-write-wins.
public sealed class HoverItemTracker : ModSystem
{
	public enum Kind { None, Item, Fluid }

	public static Kind LastKind { get; private set; } = Kind.None;
	public static int LastHoveredItemType { get; private set; }
	public static string? LastHoveredFluidId { get; private set; }

	// Browser arms this while the cursor is on its panels - hovering recipe
	// rows otherwise loops the filter back into itself. Click pushes bypass.
	private static bool _suppressNextHoverPick;

	public static void SuppressNextHoverPick() => _suppressNextHoverPick = true;

	public override void PostUpdateInput()
	{
		if (_suppressNextHoverPick)
		{
			_suppressNextHoverPick = false;
			return;
		}

		if (Main.HoverItem is { } h && !h.IsAir)
		{
			// Filled cells/buckets -> fluid filter (recipes consume them as fluid).
			var vanilla = VanillaFluidBridge.StackFor(h.type);
			if (!vanilla.IsEmpty)
			{
				SetFluid(vanilla.Type!.Id);
			}
			else if (h.ModItem is FluidCellItem cell && cell.GetFluidStack() is { IsEmpty: false } stack)
			{
				SetFluid(stack.Type!.Id);
			}
			else if (h.ModItem is FluidBucketItem bucket && bucket.Fluid is { } fluid)
			{
				SetFluid(fluid.Id);
			}
			else
			{
				LastHoveredItemType = h.type;
				LastKind = Kind.Item;
			}
		}
	}

	// Live fluid widgets call this on hover; recipe-browser cells use PushFluid on click.
	public static void SetFluid(string fluidId)
	{
		if (string.IsNullOrEmpty(fluidId)) return;
		LastHoveredFluidId = fluidId;
		LastKind = Kind.Fluid;
	}

	// Click-driven push - bypasses the suppress guard (browser continuously arms it).
	public static void PushItem(int itemType)
	{
		if (itemType <= 0) return;
		LastHoveredItemType = itemType;
		LastKind = Kind.Item;
	}

	public static void PushFluid(string fluidId) => SetFluid(fluidId);

	public override void OnWorldUnload()
	{
		LastHoveredItemType = ItemID.None;
		LastHoveredFluidId = null;
		LastKind = Kind.None;
	}
}
