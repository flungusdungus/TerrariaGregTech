#nullable enable
using GregTechCEuTerraria.Api.Cover.Filter;
using GregTechCEuTerraria.TerrariaCompat.Items.Magnets;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Item magnet's held-item filter panel. Port of ItemMagnetBehavior.createUI:
// SIMPLE/TAG selector + the matching editor (3x3 grid or tag expression).
// Rebuilds on filter-type change, deferred out of the mouse-down window.
public sealed class MagnetUIState : UIState
{
	private const float Scale = 2f;

	private Item? _magnet;
	private int _builtOrdinal = -1;

	public Item? Magnet => _magnet;
	private MagnetItem? Mi => _magnet?.ModItem as MagnetItem;

	public void Bind(Item magnet)
	{
		_magnet = magnet;
		_builtOrdinal = -1;
		Rebuild();
	}

	public void Unbind()
	{
		_magnet = null;
		_builtOrdinal = -1;
		RemoveAllChildren();
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		// Modal mouse capture lives in MagnetUISystem.PostUpdateInput.

		// Rebuild on filter-type change, deferred until no mouse button is held.
		var mi = Mi;
		if (mi != null && mi.FilterOrdinal != _builtOrdinal && !Main.mouseLeft && !Main.mouseRight)
			Rebuild();
	}

	private void Rebuild()
	{
		RemoveAllChildren();
		Widgets.UITextField.UnfocusAll();

		var mi = Mi;
		if (mi is null) return;

		_builtOrdinal = mi.FilterOrdinal;

		const int Pad = 6, Slot = 22, W = 200;
		const int RowH = 16, BtnGap = 4;
		int rowY = Pad + 14;
		int blockY = rowY + RowH + 6;

		var panel = new UITerrariaPanel
		{
			Width  = StyleDimension.FromPixels(W * Scale),
			HAlign = 0.5f,
			VAlign = 0.36f,
		};

		panel.Append(new UIText("Item Magnet", 0.62f)
		{
			Left = StyleDimension.FromPixels(Pad * Scale),
			Top  = StyleDimension.FromPixels(Pad * Scale),
		});

		panel.Append(new UITextButton(
			() => "Magnet: " + (Mi?.MagnetActive == true ? "ON" : "OFF"),
			onLeft:  ToggleActive,
			onRight: ToggleActive,
			tooltip: "Pull nearby dropped items toward you (drains EU).",
			width:  (int)(90 * Scale),
			height: (int)(RowH * Scale))
		{
			Left = StyleDimension.FromPixels(Pad * Scale),
			Top  = StyleDimension.FromPixels(rowY * Scale),
		});
		panel.Append(new UITextButton(
			() => "Filter: " + (Mi?.FilterOrdinal == 1 ? "Tags" : "Items"),
			onLeft:  CycleFilterType,
			onRight: CycleFilterType,
			tooltip: "Items - match by a 3x3 phantom item grid\nTags - match by a tag expression",
			width:  (int)(90 * Scale),
			height: (int)(RowH * Scale))
		{
			Left = StyleDimension.FromPixels((Pad + 90 + BtnGap) * Scale),
			Top  = StyleDimension.FromPixels(rowY * Scale),
		});

		int blockH;
		if (mi.FilterOrdinal == 1)
			blockH = BuildTagEditor(panel, Pad, blockY, Slot);
		else
			blockH = BuildSimpleEditor(panel, Pad, blockY, Slot);

		panel.Height = StyleDimension.FromPixels((blockY + blockH + Pad) * Scale);
		Append(panel);
	}

	private int BuildSimpleEditor(UITerrariaPanel panel, int x, int y, int slot)
	{
		const int ToggleW = 96, ToggleH = 16;

		for (int i = 0; i < 9; i++)
		{
			int gx = x + (i % 3) * slot;
			int gy = y + (i / 3) * slot;
			panel.Append(new UIMagnetPhantomSlot(() => Mi?.SimpleFilter, i)
			{
				Left   = StyleDimension.FromPixels(gx * Scale),
				Top    = StyleDimension.FromPixels(gy * Scale),
				Width  = StyleDimension.FromPixels(slot * Scale),
				Height = StyleDimension.FromPixels(slot * Scale),
			});
		}

		int tx = x + 3 * slot + 6;
		panel.Append(new UITextButton(
			() => (Mi?.SimpleFilter.IsBlackList == true) ? "Mode: Blacklist" : "Mode: Whitelist",
			onLeft:  ToggleBlacklist,
			onRight: ToggleBlacklist,
			tooltip: "Whitelist - only listed items are pulled\nBlacklist - listed items are NOT pulled\n(an empty whitelist pulls nothing)",
			width:  (int)(ToggleW * Scale),
			height: (int)(ToggleH * Scale))
		{
			Left = StyleDimension.FromPixels(tx * Scale),
			Top  = StyleDimension.FromPixels(y * Scale),
		});
		panel.Append(new UITextButton(
			() => "Ignore NBT: " + ((Mi?.SimpleFilter.IgnoreNbt == true) ? "Yes" : "No"),
			onLeft:  ToggleIgnoreNbt,
			onRight: ToggleIgnoreNbt,
			tooltip: "Ignore item NBT data when matching",
			width:  (int)(ToggleW * Scale),
			height: (int)(ToggleH * Scale))
		{
			Left = StyleDimension.FromPixels(tx * Scale),
			Top  = StyleDimension.FromPixels((y + ToggleH + 4) * Scale),
		});

		return 3 * slot;
	}

	private int BuildTagEditor(UITerrariaPanel panel, int x, int y, int slot)
	{
		panel.Append(new Widgets.UITextField(
			() => Mi?.TagFilter.OreDictFilterExpression ?? "",
			txt => Mi?.TagFilter.SetOreDict(TagItemFilter.NormalizeExpression(txt)),
			maxLength: 64,
			placeholder: "tag expression  *  e.g.  *dusts/gold | !*lv",
			tooltip: TagFilterInfo)
		{
			Left   = StyleDimension.FromPixels(x * Scale),
			Top    = StyleDimension.FromPixels(y * Scale),
			Width  = StyleDimension.FromPixels((3 * slot + 6 + 96) * Scale),
			Height = StyleDimension.FromPixels(18 * Scale),
		});
		return 18;
	}

	private void ToggleActive()      { if (Mi is { } m) m.MagnetActive = !m.MagnetActive; }
	private void CycleFilterType()   { if (Mi is { } m) m.FilterOrdinal = m.FilterOrdinal == 1 ? 0 : 1; }
	private void ToggleBlacklist()   { if (Mi is { } m) m.SimpleFilter.SetBlackList(!m.SimpleFilter.IsBlackList); }
	private void ToggleIgnoreNbt()   { if (Mi is { } m) m.SimpleFilter.SetIgnoreNbt(!m.SimpleFilter.IgnoreNbt); }

	// Verbatim from CoverSettingsUI (upstream lang cover.tag_filter.info.*).
	private const string TagFilterInfo =
		"Accepts complex expressions:\n"
		+ "a & b = AND   *   a | b = OR   *   a ^ b = XOR\n"
		+ "!a = NOT   *   (a) for grouping\n"
		+ "* = wildcard   *   $ = untagged\n"
		+ "Tags are 'namespace:tag/subtype'.\n"
		+ "The 'forge:' namespace is assumed if one isn't given.\n"
		+ "Example: *dusts/gold | (gtceu:circuits & !*lv)\n"
		+ "Type, then press Enter (or click away) to set.";
}
