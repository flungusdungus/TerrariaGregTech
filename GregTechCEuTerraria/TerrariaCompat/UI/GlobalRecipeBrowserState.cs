#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Items.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Loot;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
// Disambiguate - vanilla also has Terraria.GameContent.UI.Elements.UISearchBar.
using UISearchBar = GregTechCEuTerraria.TerrariaCompat.UI.Widgets.UISearchBar;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Full-screen JEI-style recipe browser over RecipeRegistry. Plain substring
// tokens match recipe id / ingredients / display names; `@station` tokens
// match the recipe's station (substring, so `@assemb` hits both `assembler`
// and `assembly_line`). An optional item filter (the chip row, driven by
// R/U hover) scopes the browser to "how to obtain X" / "X used as ingredient".
public sealed class GlobalRecipeBrowserState : UIState
{
	// How an active item filter scopes `_all`.
	public enum BrowseFilter { None, Output, Input }

	// Recipes = JEI-style recipe list. Items = Cheat-Sheet-style item grid
	// (clicks spawn into inventory). Loot = LootRegistry sources (NPC drops,
	// shops, shimmer); clicks pivot to Recipes "how to obtain". Equippable =
	// Items narrowed to wearables with a per-category hide column.
	public enum BrowseMode { Recipes, Items, Loot, Equippable }

	// Equippable-mode hide categories - an item belongs to one or more; the
	// equippable universe = items with any flag set.
	[System.Flags]
	private enum EquipCat
	{
		None     = 0,
		Helmet   = 1 << 0,
		Shirt    = 1 << 1,
		Pants    = 1 << 2,
		Trinket  = 1 << 3,
		Dye      = 1 << 4,
		Mount    = 1 << 5,
		Hook     = 1 << 6,
		Minecart = 1 << 7,
		Pet      = 1 << 8,
		LightPet = 1 << 9,
	}

	// Toggle labels + flags, in display order.
	private static readonly (EquipCat Cat, string Name)[] _equipCats =
	{
		(EquipCat.Helmet,   "Helmets"),
		(EquipCat.Shirt,    "Shirts"),
		(EquipCat.Pants,    "Pants"),
		(EquipCat.Trinket,  "Trinkets"),
		(EquipCat.Dye,      "Dyes"),
		(EquipCat.Mount,    "Mounts"),
		(EquipCat.Hook,     "Hooks"),
		(EquipCat.Minecart, "Minecarts"),
		(EquipCat.Pet,      "Pet"),
		(EquipCat.LightPet, "Light Pet"),
	};

	private UITerrariaPanel? _panel;
	private UITerrariaPanel? _settingsPanel;
	private UIRecipeList? _list;
	private UIItemGrid? _grid;
	private UIItemGrid? _equipGrid;
	private UILootList? _loot;
	private UISearchBar? _search;
	private UIFavoritesPanel? _favorites;
	private UITextButton? _haveOnlyToggle;
	private UITextButton? _hideObviousToggle;
	private UITextButton? _modeToggle;
	private BrowseMode _mode = BrowseMode.Recipes;
	// Mode swaps mutate _panel.Children; click handlers fire inside DrawChildren,
	// so defer to Update() like _chipPending to avoid "Collection was modified".
	private bool _modeSwapPending;

	// Single source of truth for the active mode - every call site routes here
	// so the widget re-mounts on the next Update; no direct `_mode = ...` writes.
	private void SetMode(BrowseMode m)
	{
		if (m == _mode) return;
		_mode = m;
		_modeSwapPending = true;
	}
	// Items mode universe (ContentSamples.ItemsByType with valid texture),
	// built once on first use.
	private List<int>? _allItems;
	private List<int> _filteredItems = new();
	// Equippable mode - wearable subset; hidden-category bitmask is static so
	// it persists across opens (all shown by default).
	private List<int>? _allEquippable;
	private List<int> _filteredEquippable = new();
	private static EquipCat _hiddenEquip = EquipCat.None;
	private static readonly Dictionary<int, EquipCat> _equipCatCache = new();
	// Per-category "Hide <X>" toggle column - swaps into the mod-hide region
	// while in Equippable mode (see ApplySettingsForMode).
	private UIList? _equipList;
	private UIScrollbar? _equipScroll;
	private List<LootRegistry.LootEntry> _filteredLoot = new();
	// Filter chip: _chipHint (no filter) or _chipButton (active filter) - one
	// at a time, tracked via _chipShown so UpdateChip swaps cleanly.
	private UIText? _chipHint;
	private UITextButton? _chipButton;
	private UIElement? _chipShown;
	// Pending swap stashed by UpdateChip, applied in Update - swapping inline
	// from a click handler mutates panel children mid-DrawChildren.
	private UIElement? _chipPending;
	private string _chipLabel = "";
	private bool _haveOnly;
	// "Hide obvious recipes" - drops every recipe whose upstream category ends
	// in `_recycling` (item -> constituent dusts; ~30% of the bundle). Real
	// maceration under `gtceu:macerator` is kept. Persisted statically.
	private bool _hideObvious;
	private static bool _lastHideObvious = true;
	// Per-mod "Hide <mod>" toggles, persisted across opens (static set of
	// internal mod names, default OFF). A recipe's owning mod = mod of its
	// primary output, preferring modded over vanilla; falls back to inputs,
	// then GregTech for fluid-only recipes. Cached per recipe; _modOrder
	// caches the display order (Vanilla, GregTech, others alpha).
	private static readonly HashSet<string> _hiddenMods = new();
	private static List<string>? _modOrder;
	// ModOrder reads the item universe, only fully populated post-load.
	// BuildLayout can run at ModSystem.Load (mod items still registering);
	// the first Open drops any provisional cache so the next ModOrder is complete.
	private static bool _modOrderReady;
	private static readonly Dictionary<GTRecipe, string> _recipeModCache = new();
	private const string VanillaMod = "Terraria";
	private const string GregTechMod = "GregTechCEuTerraria";
	// Mod-hide toggles live in a scrollable UIList so heavily-modded installs
	// don't overflow the panel. RebuildModToggles runs deferred since the item
	// universe may still be populating at BuildLayout time.
	private UIList? _modList;
	private UIScrollbar? _modScroll;
	private int _modBtnW;
	// Periodic re-filter while have-only is active (~0.5 s).
	private int _haveOnlyTick;
	private List<GTRecipe> _all = new();
	private List<GTRecipe> _filtered = new();

	// Persists the last search + mode across close -> re-open. ApplyItemFilter /
	// ApplyFluidFilter still reset the query (explicit pivot).
	private static string _lastQuery = "";
	private static BrowseMode _lastMode = BrowseMode.Recipes;
	public string CurrentQuery => _search?.Text ?? "";
	public void SaveQueryForReopen()
	{
		_lastQuery = CurrentQuery;
		_lastMode = _mode;
		_lastHideObvious = _hideObvious;
	}

	// Active item filter - exactly one of _filterItem / _filterFluid /
	// _filterTagItems is set when _filter != None.
	private BrowseFilter _filter = BrowseFilter.None;
	private int _filterItem;
	private string? _filterFluid;
	private string? _filterFluidLabel;
	private string? _filterTagLabel;
	private HashSet<int>? _filterTagItems;

	private const int HeaderPad = 8;
	private const int SearchH   = 26;
	private const int ChipH     = 18;
	// Hint (no filter) is two lines tall; chip BUTTON (active filter) stays one.
	private const int HintH     = 32;
	// Ctrl-click Journey-mode cheat applies in every mode - shared line 2 below.
	private const string CheatHintLine =
		"Ctrl+LMB: cheat 1 to inventory  *  Ctrl+RMB: a full stack (Journey Mode)";

	// Single source of truth for the no-filter header hint per mode (used by
	// BuildLayout + every UpdateChip refresh, so the cheat line can never drift).
	private static string HintFor(BrowseMode mode) => mode switch
	{
		BrowseMode.Items =>
			"Items mode: LMB = 1 * Shift+LMB / RMB = full stack * Alt+LMB = pin favorite\n" + CheatHintLine,
		BrowseMode.Equippable =>
			"Equippable mode: LMB = 1 * Shift+LMB / RMB = full stack * Alt+LMB = pin favorite\n" + CheatHintLine,
		BrowseMode.Loot =>
			"Loot mode: hover an item and press R to scope to that item's vanilla sources\n" + CheatHintLine,
		_ =>
			"Tip: hover an item and press R (how to obtain) or U (used as ingredient)\n" + CheatHintLine,
	};

	public override void OnInitialize() => BuildLayout();

	// (Re)builds the panel for the current screen size. UIState.OnInitialize
	// runs once, so without this the panel would keep its launch-time pixel
	// size after any resolution / UI-scale change.
	private void BuildLayout()
	{
		UIItemGrid.WarmVanillaItemTextures();


		string preservedQuery = _search?.Text ?? "";
		RemoveAllChildren();

		// Three-column layout: [settings] [main] [favorites], centred together
		// as a group. Settings (left) holds the mode / filter toggles + result
		// count; the main panel's top bar is just the search field + close X.
		const float SetWidth = 156f;
		const float SetGap   = 6f;
		float FavWidth = UIFavoritesPanel.PanelWidth;   // real pane width (4-col grid)
		const float FavGap   = 6f;
		// Total horizontal footprint of the two side panels + their gaps - the
		// main panel width is added on top of this. Used to keep the group on
		// screen at the small end.
		float GroupSidePx = SetWidth + SetGap + FavGap + FavWidth;

		// Fixed fraction of the screen, clamped min/max - large monitors render
		// at a consistent pixel size, only genuinely small screens shrink.
		// Max width capped so the [settings|main|favorites] group fits.
		// All dims UI-space (post-UIScale).
		float uiScale = Main.UIScale <= 0 ? 1f : Main.UIScale;
		float uiW = Main.screenWidth / uiScale;
		float uiH = Main.screenHeight / uiScale;
		float maxW = System.Math.Min(1024f, uiW - GroupSidePx - 24f);
		float maxH = System.Math.Min(860f, uiH - 24f);
		float w = System.Math.Clamp(uiW * 0.62f, System.Math.Min(560f, maxW), maxW);
		float h = System.Math.Clamp(uiH * 0.82f, System.Math.Min(420f, maxH), maxH);

		// Each panel HAlign=0.5; Left offsets its centre from screen centre.
		// The whole group [set | main | fav] is centred via these offsets.
		float mainLeft = (SetWidth + SetGap - FavGap - FavWidth) / 2f;
		float setLeft  = -(SetGap + w + FavGap + FavWidth) / 2f;
		float favLeft  =  (SetWidth + SetGap + w + FavGap) / 2f;

		_panel = new UITerrariaPanel
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			Left   = StyleDimension.FromPixels(mainLeft),
			Width  = StyleDimension.FromPixels(w),
			Height = StyleDimension.FromPixels(h),
		};
		Append(_panel);

		_favorites = new UIFavoritesPanel
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			Left   = StyleDimension.FromPixels(favLeft),
		};
		_favorites.SetHeight(h);
		Append(_favorites);

		const int CloseW = 24;
		const int RowGap = 6;

		// Search bar - now the full width of the top row minus the close X.
		_search = new UISearchBar(
			placeholder: "Search...  |  RMB to clear",
			onChanged: Refilter)
		{
			Left  = StyleDimension.FromPixels(HeaderPad),
			Top   = StyleDimension.FromPixels(HeaderPad),
			Width = StyleDimension.FromPixels(w - HeaderPad * 2 - CloseW - RowGap),
			Height = StyleDimension.FromPixels(SearchH),
		};
		_panel.Append(_search);

		// Close button - small 'X' top-right. Vanilla UI parity: ESC also closes.
		var close = new UIText("X", 1.1f, large: false)
		{
			HAlign = 1f,
			Left   = StyleDimension.FromPixels(-HeaderPad),
			Top    = StyleDimension.FromPixels(HeaderPad + 2),
			Width  = StyleDimension.FromPixels(CloseW),
			Height = StyleDimension.FromPixels(SearchH),
		};
		close.OnLeftClick += (_, _) => GlobalRecipeBrowserSystem.Close();
		_panel.Append(close);

		// Left settings panel. Toggle set is shared across modes (no IsVisible
		// gating). hide-obvious + have-ingredients are recipe-only no-ops in
		// other modes but stay put so the layout is stable.
		_settingsPanel = new UITerrariaPanel
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			Left   = StyleDimension.FromPixels(setLeft),
			Width  = StyleDimension.FromPixels(SetWidth),
			Height = StyleDimension.FromPixels(h),
		};
		Append(_settingsPanel);

		const int SetPad  = 8;
		int setBtnW       = (int)SetWidth - SetPad * 2;
		int setRowH       = SearchH + 6;
		int setRow0       = 52;   // below the "Settings" title + count line

		var setTitle = new UIText("Settings", 0.8f, large: false)
		{
			Left = StyleDimension.FromPixels(SetPad),
			Top  = StyleDimension.FromPixels(8),
		};
		_settingsPanel.Append(setTitle);

		// Result count "shown / total" for the active mode.
		var count = new UIDynamicLabel(() => _mode switch
		{
			BrowseMode.Items      => $"{_filteredItems.Count:N0} / {(_allItems?.Count ?? 0):N0}",
			BrowseMode.Loot       => $"{_filteredLoot.Count:N0} / {LootRegistry.All.Count:N0}",
			BrowseMode.Equippable => $"{_filteredEquippable.Count:N0} / {(_allEquippable?.Count ?? 0):N0}",
			_                     => $"{_filtered.Count:N0} / {_all.Count:N0}",
		}, 0.85f)
		{
			Left   = StyleDimension.FromPixels(SetPad),
			Top    = StyleDimension.FromPixels(28),
			Width  = StyleDimension.FromPixels(setBtnW),
			Height = StyleDimension.FromPixels(20),
		};
		_settingsPanel.Append(count);

		_modeToggle = new UITextButton(
			label:    ModeLabel,
			onLeft:   ToggleMode,
			tooltip:  "Switch between Recipes (search by recipe), Items (cheat into inventory), Loot (vanilla sources) and Equippable (wearables only)",
			width:    setBtnW,
			height:   SearchH)
		{
			Left = StyleDimension.FromPixels(SetPad),
			Top  = StyleDimension.FromPixels(setRow0),
		};
		_settingsPanel.Append(_modeToggle);

		_hideObviousToggle = new UITextButton(
			label:    HideObviousLabel,
			onLeft:   ToggleHideObvious,
			tooltip:  "Hide recycling recipes (machines, tools, covers -> dusts)",
			width:    setBtnW,
			height:   SearchH)
		{
			Left = StyleDimension.FromPixels(SetPad),
			Top  = StyleDimension.FromPixels(setRow0 + setRowH),
		};
		_settingsPanel.Append(_hideObviousToggle);

		_haveOnlyToggle = new UITextButton(
			label:    HaveLabel,
			onLeft:   ToggleHaveOnly,
			tooltip:  "Show only recipes you can craft from current inventory",
			width:    setBtnW,
			height:   SearchH)
		{
			Left = StyleDimension.FromPixels(SetPad),
			Top  = StyleDimension.FromPixels(setRow0 + setRowH * 2),
		};
		_settingsPanel.Append(_haveOnlyToggle);

		// Per-mod hide toggles - scrollable column, rebuilt on every Open since
		// the item universe may still be populating at BuildLayout time. Applies
		// in every mode (recipes by owning mod, items + loot by item mod).
		const int ScrollW = 20;
		int modListTop    = setRow0 + setRowH * 3;
		int modListH      = System.Math.Max(setRowH * 2, (int)h - modListTop - SetPad);
		_modBtnW          = setBtnW - ScrollW - 2;
		_modList = new UIList
		{
			Left        = StyleDimension.FromPixels(SetPad),
			Top         = StyleDimension.FromPixels(modListTop),
			Width       = StyleDimension.FromPixels(_modBtnW),
			Height      = StyleDimension.FromPixels(modListH),
			ListPadding = 4f,
			// Keep ModOrder()'s order; UIList's default CompareTo sort would scramble it.
			ManualSortMethod = _ => { },
		};
		_settingsPanel.Append(_modList);
		_modScroll = new UIScrollbar
		{
			Left   = StyleDimension.FromPixels(SetPad + _modBtnW + 2),
			Top    = StyleDimension.FromPixels(modListTop),
			Width  = StyleDimension.FromPixels(ScrollW),
			Height = StyleDimension.FromPixels(modListH),
		};
		// Bind for wheel clamp; UpdateModScrollVisibility appends only on overflow.
		_modList.SetScrollbar(_modScroll);
		RebuildModToggles();

		// Equippable-mode hide toggles - same region as the mod list, swapped
		// in/out by ApplySettingsForMode.
		_equipList = new UIList
		{
			Left        = StyleDimension.FromPixels(SetPad),
			Top         = StyleDimension.FromPixels(modListTop),
			Width       = StyleDimension.FromPixels(_modBtnW),
			Height      = StyleDimension.FromPixels(modListH),
			ListPadding = 4f,
			ManualSortMethod = _ => { },   // keep _equipCats order
		};
		_equipScroll = new UIScrollbar
		{
			Left   = StyleDimension.FromPixels(SetPad + _modBtnW + 2),
			Top    = StyleDimension.FromPixels(modListTop),
			Width  = StyleDimension.FromPixels(ScrollW),
			Height = StyleDimension.FromPixels(modListH),
		};
		_equipList.SetScrollbar(_equipScroll);
		BuildEquipToggles();
		ApplySettingsForMode();

		// Filter chip - hint when no filter, button when active. UpdateChip
		// swaps them in/out of the same row position.
		_chipHint = new UIText(HintFor(_mode), 0.78f, large: false)
		{
			Left   = StyleDimension.FromPixels(HeaderPad),
			Top    = StyleDimension.FromPixels(HeaderPad + SearchH + 5),
			Width  = StyleDimension.FromPixels(w - HeaderPad * 2),
			Height = StyleDimension.FromPixels(HintH),
			TextColor = new Color(150, 160, 190),
		};
		_chipButton = new UITextButton(
			label:    () => _chipLabel,
			onLeft:   () => { if (_filter != BrowseFilter.None) ClearFilter(); },
			tooltip:  "Click to clear the active filter",
			width:    (int)(w - HeaderPad * 2),
			height:   ChipH)
		{
			Left = StyleDimension.FromPixels(HeaderPad),
			Top  = StyleDimension.FromPixels(HeaderPad + SearchH + 5),
		};
		_chipShown = null;

		void ToggleHideObvious()
		{
			_hideObvious = !_hideObvious;
			Refilter(_search?.Text ?? "");
		}

		void ToggleHaveOnly()
		{
			_haveOnly = !_haveOnly;
			Refilter(_search?.Text ?? "");
		}

		int listTop = HeaderPad + SearchH + 5 + HintH + 5;
		_list = new UIRecipeList(() => _filtered, emptyHint: "No recipes match this search")
		{
			Left = StyleDimension.FromPixels(4),
			Top  = StyleDimension.FromPixels(listTop),
			Width  = StyleDimension.FromPixels(w - 8),
			Height = StyleDimension.FromPixels(h - listTop - 4),
		};
		_grid = new UIItemGrid(() => _filteredItems)
		{
			Left = StyleDimension.FromPixels(4),
			Top  = StyleDimension.FromPixels(listTop),
			Width  = StyleDimension.FromPixels(w - 8),
			Height = StyleDimension.FromPixels(h - listTop - 4),
		};
		_equipGrid = new UIItemGrid(() => _filteredEquippable, emptyHint: "No equippable items match this search")
		{
			Left = StyleDimension.FromPixels(4),
			Top  = StyleDimension.FromPixels(listTop),
			Width  = StyleDimension.FromPixels(w - 8),
			Height = StyleDimension.FromPixels(h - listTop - 4),
		};
		_loot = new UILootList(() => _filteredLoot, emptyHint: "No loot matches this search")
		{
			Left = StyleDimension.FromPixels(4),
			Top  = StyleDimension.FromPixels(listTop),
			Width  = StyleDimension.FromPixels(w - 8),
			Height = StyleDimension.FromPixels(h - listTop - 4),
		};
		ApplyMode();
		UpdateChip();

		// Restore preserved search across resolution-rebuild; triggers one
		// Refilter via the search bar's onChanged.
		if (preservedQuery.Length > 0) _search?.SetText(preservedQuery);

		_builtScreenW = Main.screenWidth;
		_builtScreenH = Main.screenHeight;
		_builtUiScale = Main.UIScale;
	}

	private int   _builtScreenW = -1;
	private int   _builtScreenH = -1;
	private float _builtUiScale = -1f;

	private bool ScreenChanged()
		=> Main.screenWidth  != _builtScreenW
		|| Main.screenHeight != _builtScreenH
		|| System.Math.Abs(Main.UIScale - _builtUiScale) > 0.001f;

	private string ModeLabel() => _mode switch
	{
		BrowseMode.Recipes    => "Mode: Recipes",
		BrowseMode.Items      => "Mode: Items",
		BrowseMode.Loot       => "Mode: Loot",
		BrowseMode.Equippable => "Mode: Equippable",
		_                     => "Mode",
	};

	private void ToggleMode()
	{
		// Recipes -> Items -> Loot -> Equippable -> Recipes. Search text preserved
		// across switches; ApplyMode re-runs Refilter for the new mode.
		SetMode(_mode switch
		{
			BrowseMode.Recipes => BrowseMode.Items,
			BrowseMode.Items   => BrowseMode.Loot,
			BrowseMode.Loot    => BrowseMode.Equippable,
			_                  => BrowseMode.Recipes,
		});
	}

	// Swap the active content widget (list / grid / loot). Item/tag filter is
	// preserved across modes (Items ignores it; Loot honours it).
	private void ApplyMode()
	{
		if (_panel is null || _list is null || _grid is null || _equipGrid is null || _loot is null) return;

		if (_list.Parent == _panel)      _panel.RemoveChild(_list);
		if (_grid.Parent == _panel)      _panel.RemoveChild(_grid);
		if (_equipGrid.Parent == _panel) _panel.RemoveChild(_equipGrid);
		if (_loot.Parent == _panel)      _panel.RemoveChild(_loot);

		switch (_mode)
		{
			case BrowseMode.Recipes:    _panel.Append(_list); break;
			case BrowseMode.Items:      _panel.Append(_grid); break;
			case BrowseMode.Loot:       _panel.Append(_loot); break;
			case BrowseMode.Equippable: _panel.Append(_equipGrid); break;
		}

		ApplySettingsForMode();
		UpdateChip();
		Refilter(_search?.Text ?? "");
	}

	// Swaps the lower toggle list between the mod-hide list and the
	// equip-category list; top toggles stay put across modes.
	private void ApplySettingsForMode()
	{
		if (_settingsPanel is null || _modList is null || _equipList is null) return;
		bool equip = _mode == BrowseMode.Equippable;

		void Detach(UIElement? e) { if (e is not null && e.Parent == _settingsPanel) _settingsPanel.RemoveChild(e); }

		if (equip)
		{
			Detach(_modList);
			Detach(_modScroll);
			if (_equipList.Parent != _settingsPanel) _settingsPanel.Append(_equipList);
			UpdateEquipScrollVisibility();
		}
		else
		{
			Detach(_equipList);
			Detach(_equipScroll);
			if (_modList.Parent != _settingsPanel) _settingsPanel.Append(_modList);
			UpdateModScrollVisibility();
		}
	}

	// Called by GlobalRecipeBrowserSystem.Open() - clears any item filter,
	// pulls every recipe fresh, and restores `_lastQuery` for close->re-open.
	public void RebuildFromScratch()
	{
		_filter = BrowseFilter.None;
		_filterItem = 0;
		_filterFluid = null;
		_filterFluidLabel = null;
		_filterTagLabel = null;
		_filterTagItems = null;
		_search?.SetText(_lastQuery);
		_hideObvious = _lastHideObvious;
		SetMode(_lastMode);
		// SetMode defers to next Update; flush inline here since RebuildFromScratch
		// runs from Open() (outside DrawChildren) and callers expect a rendered panel.
		if (_modeSwapPending) { _modeSwapPending = false; ApplyMode(); }
		// Browser is opened post-load; drop any provisional mod list cached at Load.
		if (!_modOrderReady) { _modOrderReady = true; _modOrder = null; }
		RebuildModToggles();
		RecomputeAll();
	}

	// Scopes the browser to a single item. Output = "how to obtain X",
	// Input = "X used as ingredient". Resets the text search.
	public void ApplyItemFilter(int itemType, BrowseFilter filter)
	{
		if (itemType <= 0 || filter == BrowseFilter.None) { ClearFilter(); return; }
		SetMode(BrowseMode.Recipes);
		_filter = filter;
		_filterItem = itemType;
		_filterFluid = null;
		_filterFluidLabel = null;
		_filterTagLabel = null;
		_filterTagItems = null;
		_search?.SetText("");
		RecomputeAll();
	}

	// Fluid variant; driven by R/U over a fluid cell.
	public void ApplyFluidFilter(string fluidId, string label, BrowseFilter filter)
	{
		if (string.IsNullOrEmpty(fluidId) || filter == BrowseFilter.None) { ClearFilter(); return; }
		SetMode(BrowseMode.Recipes);
		_filter = filter;
		_filterItem = 0;
		_filterFluid = fluidId;
		_filterFluidLabel = label;
		_filterTagLabel = null;
		_filterTagItems = null;
		_search?.SetText("");
		RecomputeAll();
	}

	// Tag variant - matches any item in the tag's resolved set. Without this,
	// a click on a TagIngredient cell would resolve to the first member only.
	public void ApplyTagFilter(string tagLabel, HashSet<int> items, BrowseFilter filter)
	{
		if (items.Count == 0 || filter == BrowseFilter.None) { ClearFilter(); return; }
		SetMode(BrowseMode.Recipes);
		_filter = filter;
		_filterItem = 0;
		_filterFluid = null;
		_filterFluidLabel = null;
		_filterTagLabel = tagLabel;
		_filterTagItems = items;
		_search?.SetText("");
		RecomputeAll();
	}

	// Preserves the search text so clearing the chip leaves the query intact.
	private void ClearFilter()
	{
		_filter = BrowseFilter.None;
		_filterItem = 0;
		_filterFluid = null;
		_filterFluidLabel = null;
		_filterTagLabel = null;
		_filterTagItems = null;
		RecomputeAll();
	}

	// Rebuilds `_all` from RecipeRegistry, scoped by the active item filter,
	// then re-applies the current text search on top.
	private void RecomputeAll()
	{
		_all.Clear();
		foreach (var kv in RecipeRegistry.ByStation)
			foreach (var r in kv.Value)
			{
				if (_filter == BrowseFilter.None)
				{
					_all.Add(r);
					continue;
				}
				bool match;
				if (_filterFluid is not null)
				{
					var set = _filter == BrowseFilter.Output
						? Widgets.RecipeRowRenderer.OutputFluidIdsInRecipe(r)
						: Widgets.RecipeRowRenderer.InputFluidIdsInRecipe(r);
					match = set.Contains(_filterFluid);
				}
				else if (_filterTagItems is not null)
				{
					var set = _filter == BrowseFilter.Output
						? Widgets.RecipeRowRenderer.OutputItemTypesInRecipe(r)
						: Widgets.RecipeRowRenderer.InputItemTypesInRecipe(r);
					match = set.Overlaps(_filterTagItems);
				}
				else
				{
					var set = _filter == BrowseFilter.Output
						? Widgets.RecipeRowRenderer.OutputItemTypesInRecipe(r)
						: Widgets.RecipeRowRenderer.InputItemTypesInRecipe(r);
					match = set.Contains(_filterItem);
				}
				if (match) _all.Add(r);
			}
		UpdateChip();
		Refilter(_search?.Text ?? "");
	}

	// Narrows `_all` -> `_filtered` by the JEI-style text search tokens,
	// then optionally by the "have ingredients" checkbox.
	private void Refilter(string text)
	{
		if (_mode == BrowseMode.Items)      { RefilterItems(text);      return; }
		if (_mode == BrowseMode.Loot)       { RefilterLoot(text);       return; }
		if (_mode == BrowseMode.Equippable) { RefilterEquippable(text); return; }

		var tokens = RecipeSearch.Tokenize(text);
		bool needText = tokens.Length > 0;
		bool needModFilter = _hiddenMods.Count > 0;
		bool needFilter = needText || _haveOnly || _hideObvious || needModFilter;

		Dictionary<int, int>? inv  = _haveOnly ? BuildInventoryCounts() : null;
		Dictionary<string, int>? fluids = _haveOnly ? BuildFluidCounts() : null;

		_filtered.Clear();
		if (needFilter)
		{
			foreach (var r in _all)
			{
				if (_hideObvious && IsObviousRecipe(r)) continue;
				if (needModFilter && _hiddenMods.Contains(RecipeModName(r))) continue;
				if (needText && !RecipeSearch.Matches(r, tokens)) continue;
				if (inv is not null && !RecipeCraftableNow(r, inv, fluids!)) continue;
				_filtered.Add(r);
			}
		}
		else
		{
			_filtered.AddRange(_all);
		}

		// Two-key stable sort - see SortByRank. Skipped under Have-ingredients
		// (every row is 0-missing, only outputs-match would drive).
		if (_filtered.Count > 1)
		{
			var sortInv    = inv    ?? BuildInventoryCounts();
			var sortFluids = fluids ?? BuildFluidCounts();
			bool useCraftableKey = !_haveOnly;
			SortByRank(_filtered, tokens, useCraftableKey, sortInv, sortFluids);
		}
	}

	// Packed integer rank - lower is earlier. Bit [16] = not-outputs-match
	// (recipes producing the search term first); bits [15:0] = count of
	// unsatisfied inputs (4/5-green ranks above 1/5-green above 0/5-green).
	// Either key collapses to 0 when not applicable.
	private const int OutputsMissBit = 1 << 16;
	private const int MissingMask    = 0xFFFF;

	private static long[]     _sortKeys  = System.Array.Empty<long>();
	private static GTRecipe[] _sortItems = System.Array.Empty<GTRecipe>();

	private static void SortByRank(List<GTRecipe> list, string[] tokens,
		bool useCraftableKey,
		Dictionary<int, int> inv,
		Dictionary<string, int> fluids)
	{
		bool needText = tokens.Length > 0;
		int n = list.Count;
		if (n <= 1) return;

		if (_sortKeys.Length < n)
		{
			_sortKeys  = new long[n];
			_sortItems = new GTRecipe[n];
		}
		var keys  = _sortKeys;
		var items = _sortItems;
		for (int i = 0; i < n; i++)
		{
			var r = list[i];
			items[i] = r;
			int rank = 0;
			if (needText && !RecipeSearch.MatchesOutputs(r, tokens)) rank |= OutputsMissBit;
			if (useCraftableKey)
			{
				int missing = CountMissingInputs(r, inv, fluids);
				if (missing > MissingMask) missing = MissingMask;
				rank |= missing;
			}
			keys[i] = ((long)rank << 32) | (uint)i;
		}
		System.Array.Sort(keys, items, 0, n);
		list.Clear();
		for (int i = 0; i < n; i++) list.Add(items[i]);
	}

	// Counts unsatisfied inputs - every ingredient cell that would render red
	// or yellow. Mirrors the cell-tint logic so the sort matches visible state.
	private static int CountMissingInputs(GTRecipe recipe,
		IReadOnlyDictionary<int, int> inv,
		IReadOnlyDictionary<string, int> fluids)
	{
		int missing = 0;
		if (recipe.Inputs.TryGetValue(ItemRecipeCapability.CAP, out var items))
		{
			foreach (var content in items)
			{
				int needed = CountFor(content);
				if (needed <= 0) continue;
				if (GetItemAvailability(content, inv) != AvailabilityState.Full) missing++;
			}
		}
		if (recipe.Inputs.TryGetValue(FluidRecipeCapability.CAP, out var liquids))
		{
			foreach (var content in liquids)
			{
				if (GetFluidAvailability(content, fluids) != AvailabilityState.Full) missing++;
			}
		}
		return missing;
	}

	private string HaveLabel() => _haveOnly ? "[x] Have ingredients" : "[ ] Have ingredients";
	private string HideObviousLabel() => _hideObvious ? "[x] Hide obvious" : "[ ] Hide obvious";

	// True for noise recipes (recycling categories, wiremill bundled-wire
	// variants, packer wire/cable/dust-size conversions, shapeless wire-gt
	// doubling/splitting). Cable variants stay - insulation has real cost.
	private static bool IsObviousRecipe(GTRecipe r)
	{
		var cat = r.CategoryId;
		if (cat is not null && cat.EndsWith("_recycling", System.StringComparison.Ordinal))
			return true;

		string id = r.Id;
		if (id.Length == 0) return false;

		// wiremill bundled-wire variants - ingot -> double/quadruple/octal/hex.
		if (id.StartsWith("wiremill/mill_", System.StringComparison.Ordinal))
		{
			if (id.EndsWith("_wire_2", System.StringComparison.Ordinal) ||
			    id.EndsWith("_wire_4", System.StringComparison.Ordinal) ||
			    id.EndsWith("_wire_8", System.StringComparison.Ordinal) ||
			    id.EndsWith("_wire_16", System.StringComparison.Ordinal))
				return true;
		}

		// packer wire-tier conversions (single <-> double <-> quadruple <-> octal <-> hex).
		if (id.StartsWith("packer/pack_", System.StringComparison.Ordinal) &&
		    id.Contains("_wires_", System.StringComparison.Ordinal))
			return true;

		// packer cable-strip - cable -> wire + rubber_plate (pure unpack).
		if (id.StartsWith("packer/strip_", System.StringComparison.Ordinal) &&
		    id.Contains("_cable_gt_", System.StringComparison.Ordinal))
			return true;

		// shapeless wire-tier conversions (workbench equivalent of the packer recipes).
		if (id.Contains("_wire_wire_gt_", System.StringComparison.Ordinal))
			return true;

		// Dust-size conversions: tiny <-> small <-> regular (packer + shapeless).
		// Also fluid-pipe size packing (4 small -> quadruple, 9 -> nonuple).
		if (id.StartsWith("packer/package_", System.StringComparison.Ordinal) ||
		    id.StartsWith("packer/unpackage_", System.StringComparison.Ordinal))
		{
			if (id.EndsWith("_small_dust", System.StringComparison.Ordinal) ||
			    id.EndsWith("_tiny_dust", System.StringComparison.Ordinal))
				return true;
			if (id.EndsWith("_quadruple_pipe", System.StringComparison.Ordinal) ||
			    id.EndsWith("_nonuple_pipe", System.StringComparison.Ordinal))
				return true;
		}
		if (id.StartsWith("shaped/small_dust_", System.StringComparison.Ordinal) ||
		    id.StartsWith("shaped/tiny_dust_", System.StringComparison.Ordinal))
			return true;

		return false;
	}

	// Ordered list of mod internal names: Vanilla, GregTech, then others alpha
	// by display name. Derived from the full item universe (not just recipes)
	// so the toggles filter Items + Loot too. GregTech added explicitly for
	// fluid-only recipes that have no resolvable item output.
	private static List<string> ModOrder()
	{
		if (_modOrder is not null) return _modOrder;
		var set = new HashSet<string>();
		foreach (var kv in ContentSamples.ItemsByType)
			if (kv.Key > 0 && kv.Value is not null)
				set.Add(ItemModName(kv.Key));
		set.Add(GregTechMod);
		var list = new List<string>(set);
		list.Sort((a, b) =>
		{
			int ra = ModRank(a), rb = ModRank(b);
			if (ra != rb) return ra.CompareTo(rb);
			return string.Compare(ModDisplayName(a), ModDisplayName(b),
				System.StringComparison.OrdinalIgnoreCase);
		});
		// Only cache once recipes actually exist - otherwise the first call
		// (BuildLayout at ModSystem.Load, before recipes load) would pin an
		// empty list forever and the toggles would never appear.
		if (list.Count > 0) _modOrder = list;
		return list;
	}

	private static int ModRank(string mod) => mod == VanillaMod ? 0 : mod == GregTechMod ? 1 : 2;

	// (Re)builds the per-mod "Hide <mod>" toggle buttons into the scrollable
	// list. Idempotent - clears the previous set first. Called from BuildLayout
	// (layout build / resolution change) and RebuildFromScratch (every Open)
	// since the item universe may be incomplete at the first BuildLayout (state
	// Activate()'d at Load). UIList owns each button's position; we only set
	// width/height - no Left/Top.
	private void RebuildModToggles()
	{
		if (_modList is null) return;
		_modList.Clear();
		foreach (var modName in ModOrder())
		{
			string captured = modName;
			var modBtn = new UITextButton(
				label:   () => (_hiddenMods.Contains(captured) ? "[x] Hide " : "[ ] Hide ") + ModDisplayName(captured),
				onLeft:  () => ToggleHideMod(captured),
				tooltip: $"Hide {ModDisplayName(captured)} content - recipes, items and loot",
				width:   _modBtnW,
				height:  SearchH);
			_modList.Add(modBtn);
		}
		UpdateModScrollVisibility();
	}

	// Show the scrollbar only on overflow. Decided per-(re)build, not per-frame.
	private void UpdateModScrollVisibility()
	{
		if (_modList is null || _modScroll is null || _settingsPanel is null) return;
		// Don't leave a stray scrollbar behind while Equippable mode shows its
		// own column - RebuildModToggles can run via RebuildFromScratch.
		if (_mode == BrowseMode.Equippable)
		{
			if (_modScroll.Parent == _settingsPanel) _settingsPanel.RemoveChild(_modScroll);
			return;
		}
		int count = _modList.Count;
		const float ListPadding = 4f;
		float contentH = count * SearchH + (count > 1 ? count * ListPadding : 0f);
		bool need = contentH > _modList.Height.Pixels + 0.5f;
		bool attached = _modScroll.Parent == _settingsPanel;
		if (need && !attached) _settingsPanel.Append(_modScroll);
		else if (!need && attached) _settingsPanel.RemoveChild(_modScroll);
	}

	private void ToggleHideMod(string mod)
	{
		if (!_hiddenMods.Remove(mod)) _hiddenMods.Add(mod);
		Refilter(_search?.Text ?? "");
	}

	private static string ModDisplayName(string internalName) => internalName switch
	{
		VanillaMod  => "Vanilla",
		GregTechMod => "GregTech",
		// tModLoader's built-in items live under internal name "ModLoader" and
		// TryGetMod doesn't resolve it; map by hand.
		"ModLoader" => "tModLoader",
		_           => ModLoader.TryGetMod(internalName, out var m) ? m.DisplayName : internalName,
	};

	// Owning mod = primary output's mod, preferring modded over vanilla; falls
	// back to inputs, then GregTech for fluid-only recipes. Cached per recipe.
	private static string RecipeModName(GTRecipe r)
	{
		if (_recipeModCache.TryGetValue(r, out var cached)) return cached;
		string mod = ClassifyRecipeMod(r);
		_recipeModCache[r] = mod;
		return mod;
	}

	private static string ClassifyRecipeMod(GTRecipe r)
	{
		if (PreferModded(Widgets.RecipeRowRenderer.OutputItemTypesInRecipe(r), out string outMod))
			return outMod;
		if (PreferModded(Widgets.RecipeRowRenderer.InputItemTypesInRecipe(r), out string inMod))
			return inMod;
		return GregTechMod;   // fluid<->fluid - no resolvable items
	}

	// First non-vanilla mod in the set; else Vanilla if any vanilla item present;
	// else false.
	private static bool PreferModded(HashSet<int> types, out string mod)
	{
		bool sawVanilla = false;
		foreach (int t in types)
		{
			string m = ItemModName(t);
			if (m != VanillaMod) { mod = m; return true; }
			sawVanilla = true;
		}
		mod = VanillaMod;
		return sawVanilla;
	}

	private static string ItemModName(int type)
	{
		if (type <= 0 || type < ItemID.Count) return VanillaMod;
		if (ContentSamples.ItemsByType.TryGetValue(type, out var item) && item.ModItem is not null)
			return item.ModItem.Mod.Name;
		return VanillaMod;
	}

	// Loot-mode filter. Tokenises like the recipe search. Rows whose TARGET
	// item name matches every token sort first (same "outputs first" semantics
	// the recipe sort uses).
	private void RefilterLoot(string text)
	{
		var all = LootRegistry.All;
		string[] tokens = RecipeSearch.Tokenize(text);
		bool needText = tokens.Length > 0;

		// "How to obtain X" scopes by TargetItem (or tag-filter set). Input filter
		// has no loot interpretation (sources are NPCs/shops/shimmer, not items)
		// so it narrows to nothing.
		bool scopeByItem = _filter == BrowseFilter.Output && _filterItem > 0;
		bool scopeByTag  = _filter == BrowseFilter.Output && _filterTagItems is not null;
		bool scopeEmpty  = _filter == BrowseFilter.Input || _filterFluid is not null;
		bool needMod     = _hiddenMods.Count > 0;

		if (scopeEmpty) { _filteredLoot = new List<LootRegistry.LootEntry>(); return; }

		if (!needText && !scopeByItem && !scopeByTag && !needMod)
		{
			_filteredLoot = new List<LootRegistry.LootEntry>(all);
			return;
		}

		_filteredLoot = new List<LootRegistry.LootEntry>(all.Count);
		foreach (var e in all)
		{
			if (needMod && _hiddenMods.Contains(ItemModName(e.TargetItem))) continue;
			if (scopeByItem && e.TargetItem != _filterItem) continue;
			if (scopeByTag  && !_filterTagItems!.Contains(e.TargetItem)) continue;
			if (needText && !LootRegistry.Matches(e, tokens)) continue;
			_filteredLoot.Add(e);
		}

		if (_filteredLoot.Count <= 1) return;
		var ranks = new int[_filteredLoot.Count];
		for (int i = 0; i < _filteredLoot.Count; i++)
			ranks[i] = LootRegistry.MatchesTarget(_filteredLoot[i], tokens) ? 0 : 1;
		for (int i = 1; i < _filteredLoot.Count; i++)
		{
			var e = _filteredLoot[i];
			int rank = ranks[i];
			int j = i - 1;
			while (j >= 0 && ranks[j] > rank)
			{
				_filteredLoot[j + 1] = _filteredLoot[j];
				ranks[j + 1] = ranks[j];
				j--;
			}
			_filteredLoot[j + 1] = e;
			ranks[j + 1] = rank;
		}
	}

	private void RefilterItems(string text)
	{
		var allItems = EnsureItemUniverse();
		string needle = (text ?? string.Empty).Trim().ToLowerInvariant();
		bool needText = needle.Length > 0;
		bool needMod  = _hiddenMods.Count > 0;
		// Reuse the list instance (closure re-reads the field); Clear keeps capacity.
		_filteredItems.Clear();
		if (!needText && !needMod)
		{
			_filteredItems.AddRange(allItems);
			return;
		}
		foreach (int type in allItems)
		{
			if (needMod && _hiddenMods.Contains(ItemModName(type))) continue;
			if (needText)
			{
				string name = ItemNameLower(type);
				if (name.Length == 0 || !name.Contains(needle)) continue;
			}
			_filteredItems.Add(type);
		}
	}

	private static readonly Dictionary<int, string> _itemNameLowerCache = new();

	private List<int> EnsureItemUniverse()
	{
		if (_allItems is not null) return _allItems;
		_allItems = new List<int>(ContentSamples.ItemsByType.Count);
		foreach (var kv in ContentSamples.ItemsByType)
			if (kv.Key > 0 && kv.Value != null) _allItems.Add(kv.Key);
		_allItems.Sort();
		return _allItems;
	}

	private List<int> EnsureEquippableUniverse()
	{
		if (_allEquippable is not null) return _allEquippable;
		_allEquippable = new List<int>();
		foreach (var kv in ContentSamples.ItemsByType)
			if (kv.Key > 0 && kv.Value is not null && EquipCatOf(kv.Key) != EquipCat.None)
				_allEquippable.Add(kv.Key);
		_allEquippable.Sort();
		return _allEquippable;
	}

	public void Warm()
	{
		EnsureItemUniverse();
		EnsureEquippableUniverse();
		WarmItemNames();
	}

	private static void WarmItemNames()
	{
		foreach (var kv in ContentSamples.ItemsByType)
			if (kv.Key > 0 && kv.Value is not null)
				ItemNameLower(kv.Key);
	}

	private static string ItemNameLower(int type)
	{
		if (_itemNameLowerCache.TryGetValue(type, out var s)) return s;
		string? name = ContentSamples.ItemsByType.TryGetValue(type, out var it) && it is not null
			? it.Name : null;
		s = (name ?? string.Empty).ToLowerInvariant();
		_itemNameLowerCache[type] = s;
		return s;
	}

	// Classifies an item into equipment categories. Bounds-guards each lookup
	// since modded items can carry shoot/buffType/mountType past vanilla array lengths.
	private static EquipCat ClassifyEquip(Item it)
	{
		var c = EquipCat.None;
		if (it.headSlot >= 0) c |= EquipCat.Helmet;
		if (it.bodySlot >= 0) c |= EquipCat.Shirt;
		if (it.legSlot  >= 0) c |= EquipCat.Pants;
		if (it.dye > 0)       c |= EquipCat.Dye;
		if (it.accessory)     c |= EquipCat.Trinket;
		if (it.mountType != -1)
			c |= (it.mountType < MountID.Sets.Cart.Length && MountID.Sets.Cart[it.mountType])
				? EquipCat.Minecart : EquipCat.Mount;
		if (it.shoot > ProjectileID.None && it.shoot < Main.projHook.Length && Main.projHook[it.shoot])
			c |= EquipCat.Hook;
		if (it.buffType > 0)
		{
			if (it.buffType < Main.vanityPet.Length && Main.vanityPet[it.buffType]) c |= EquipCat.Pet;
			if (it.buffType < Main.lightPet.Length  && Main.lightPet[it.buffType])  c |= EquipCat.LightPet;
		}
		return c;
	}

	private static EquipCat EquipCatOf(int type)
	{
		if (_equipCatCache.TryGetValue(type, out var c)) return c;
		c = ContentSamples.ItemsByType.TryGetValue(type, out var it) && it is not null
			? ClassifyEquip(it) : EquipCat.None;
		_equipCatCache[type] = c;
		return c;
	}

	private void RefilterEquippable(string text)
	{
		var allEquip = EnsureEquippableUniverse();
		string needle = (text ?? string.Empty).Trim().ToLowerInvariant();
		bool needText = needle.Length > 0;
		bool needMod  = _hiddenMods.Count > 0;
		var hidden    = _hiddenEquip;
		_filteredEquippable.Clear();
		foreach (int type in allEquip)
		{
			// Hide only when every category the item belongs to is hidden, so
			// an item in both a hidden + un-hidden category stays.
			var cat = EquipCatOf(type);
			if (hidden != EquipCat.None && (cat & ~hidden) == EquipCat.None) continue;
			if (needMod && _hiddenMods.Contains(ItemModName(type))) continue;
			if (needText)
			{
				string name = ItemNameLower(type);
				if (name.Length == 0 || !name.Contains(needle)) continue;
			}
			_filteredEquippable.Add(type);
		}
	}

	private void BuildEquipToggles()
	{
		if (_equipList is null) return;
		_equipList.Clear();
		foreach (var (cat, name) in _equipCats)
		{
			var captured = cat;
			string captName = name;
			var btn = new UITextButton(
				label:   () => ((_hiddenEquip & captured) != 0 ? "[x] Hide " : "[ ] Hide ") + captName,
				onLeft:  () => ToggleHideEquip(captured),
				tooltip: $"Hide {captName} in Equippable mode",
				width:   _modBtnW,
				height:  SearchH);
			_equipList.Add(btn);
		}
	}

	private void ToggleHideEquip(EquipCat cat)
	{
		_hiddenEquip ^= cat;
		Refilter(_search?.Text ?? "");
	}

	// Mirror of UpdateModScrollVisibility for the equip toggle column.
	private void UpdateEquipScrollVisibility()
	{
		if (_equipList is null || _equipScroll is null || _settingsPanel is null) return;
		if (_mode != BrowseMode.Equippable)
		{
			if (_equipScroll.Parent == _settingsPanel) _settingsPanel.RemoveChild(_equipScroll);
			return;
		}
		int count = _equipList.Count;
		const float ListPadding = 4f;
		float contentH = count * SearchH + (count > 1 ? count * ListPadding : 0f);
		bool need = contentH > _equipList.Height.Pixels + 0.5f;
		bool attached = _equipScroll.Parent == _settingsPanel;
		if (need && !attached) _settingsPanel.Append(_equipScroll);
		else if (!need && attached) _settingsPanel.RemoveChild(_equipScroll);
	}

	// One-tick cache for the inventory + fluid walker - runs at most once per
	// game tick across all callers (refilter, per-cell renderer, have-only filter).
	private static uint   _snapTick    = uint.MaxValue;
	private static Dictionary<int, int>?    _snapInv;
	private static Dictionary<string, int>? _snapFluids;
	private static void EnsureSnapshot()
	{
		if (_snapTick == Main.GameUpdateCount && _snapInv is not null && _snapFluids is not null) return;
		_snapInv    = BuildInventoryCountsImpl();
		_snapFluids = BuildFluidCountsImpl();
		_snapTick   = Main.GameUpdateCount;
	}
	internal static Dictionary<int, int> InventoryCountsSnapshot()
	{ EnsureSnapshot(); return _snapInv!; }
	internal static Dictionary<string, int> FluidCountsSnapshot()
	{ EnsureSnapshot(); return _snapFluids!; }

	// ItemID -> total stack count across vanilla's crafting pool: inventory,
	// open container, Void Bag (when equipped), every nearby chest within 600 px.
	// Mirrors `Recipe.CollectItemsToCraftWithFrom` + `CollectItemsFromChests`;
	// adds cursor + trash slot (player-held).
	internal static Dictionary<int, int> BuildInventoryCounts() => InventoryCountsSnapshot();
	private  static Dictionary<int, int> BuildInventoryCountsImpl()
	{
		var counts = new Dictionary<int, int>();
		var player = Main.LocalPlayer;
		void Add(Item? it)
		{
			if (it is null || it.IsAir) return;
			counts[it.type] = (counts.TryGetValue(it.type, out int n) ? n : 0) + it.stack;
		}
		void AddArray(Item[]? arr)
		{
			if (arr is null) return;
			foreach (var it in arr) Add(it);
		}

		AddArray(player.inventory);
		Add(Main.mouseItem);
		Add(player.trashItem);

		// Vanilla parity with Recipe.CollectItemsFromChests.
		var seen = new HashSet<Item[]>(ReferenceEqualityComparer.Instance);
		WalkCraftingChests(player, seen, AddArray);

		return counts;
	}

	// Vanilla `Recipe.CollectItemsFromChests` parity, inlined because the
	// underlying Player APIs (GetCurrentContainer, NearbyChests) aren't surfaced
	// by tModLoader's publicized API. `seen` dedupes by item-array reference.
	private static void WalkCraftingChests(Player player, HashSet<Item[]> seen, System.Action<Item[]?> addArray)
	{
		void AddChest(Chest? c)
		{
			if (c is null || c.item is null) return;
			if (!seen.Add(c.item)) return;
			addArray(c.item);
		}

		// Open container - world chest at chest>=0, portable banks at negatives.
		if (player.chest != -1)
		{
			Chest? open = player.chest switch
			{
				-2 => player.bank,
				-3 => player.bank2,
				-4 => player.bank3,
				-5 => player.bank4,
				_  => (player.chest >= 0 && player.chest < Main.chest.Length) ? Main.chest[player.chest] : null,
			};
			AddChest(open);
		}

		// Void Bag - skipped when its own interface is the open chest (dedup).
		if (player.useVoidBag() && player.chest != -5) AddChest(player.bank4);

		// Nearby chests at vanilla's 600 px range. CraftFromNearbyChests +
		// IsLockedOrInUse aren't exposed to a mod project; we treat the setting
		// as enabled and accept the minor false-positive on locked chests (UI hint).
		const float Range = 600f;
		var center = player.Center;
		for (int i = 0; i < Main.chest.Length; i++)
		{
			var c = Main.chest[i];
			if (c is null) continue;
			var pos = new Microsoft.Xna.Framework.Vector2(c.x * 16 + 16, c.y * 16 + 16);
			if (Microsoft.Xna.Framework.Vector2.Distance(pos, center) <= Range) AddChest(c);
		}
	}

	// Every item AND fluid input satisfied. EU/circuit inputs aren't checked
	// (the filter is "produceable from current items + carried fluid containers").
	// Fluid-only recipes (e.g. distillery wood_vinegar -> water) must check fluids
	// independently or they'd false-greenlight with no item inputs.
	private static bool RecipeCraftableNow(GTRecipe recipe, Dictionary<int, int> inv,
		Dictionary<string, int> fluidsHeld)
	{
		bool hasAnyInput = false;
		if (recipe.Inputs.TryGetValue(ItemRecipeCapability.CAP, out var items))
		{
			foreach (var content in items)
			{
				int needed = CountFor(content);
				if (needed <= 0) continue;
				hasAnyInput = true;
				if (!HasAny(content, needed, inv)) return false;
			}
		}
		if (recipe.Inputs.TryGetValue(FluidRecipeCapability.CAP, out var liquids))
		{
			foreach (var content in liquids)
			{
				hasAnyInput = true;
				if (!HasFluid(content, fluidsHeld)) return false;
			}
		}
		// A recipe with zero item AND zero fluid inputs (e.g. fluid_drilling_rig
		// / large_miner biome-keyed synthetic recipes producing oil from nothing)
		// is world-IO production, not player-craftable - exclude from the filter.
		return hasAnyInput;
	}

	// Walks every chest pool BuildInventoryCounts considers through the
	// abstract `IFluidHandlerItem` interface - any item that exposes fluid
	// contents (cells, buckets, anything else that implements it later) is
	// summed uniformly by fluid id, no per-type if-chain. mB-precise:
	// ten 100 mB cells satisfy a 1000 mB ingredient. Stack count multiplies
	// (a stack of 8 filled cells contributes 8x their per-cell contents).
	internal static Dictionary<string, int> BuildFluidCounts() => FluidCountsSnapshot();
	private  static Dictionary<string, int> BuildFluidCountsImpl()
	{
		var fluids = new Dictionary<string, int>();
		var player = Main.LocalPlayer;
		void AddStack(FluidStack stack, int itemStack)
		{
			if (stack.IsEmpty || stack.Type is null) return;
			fluids[stack.Type.Id] =
				(fluids.TryGetValue(stack.Type.Id, out int a) ? a : 0)
				+ stack.Amount * itemStack;
		}
		void Add(Item? it)
		{
			if (it is null || it.IsAir) return;
			// Mirrors CoverFilterAction.HeldFluid: vanilla bucket -> GT bucket -> handler.
			var vanilla = VanillaFluidBridge.StackFor(it.type);
			if (!vanilla.IsEmpty) { AddStack(vanilla, it.stack); return; }
			if (it.ModItem is FluidBucketItem bucket && bucket.Fluid is { } gf)
			{
				AddStack(new FluidStack(gf, VanillaFluidBridge.BucketAmount), it.stack);
				return;
			}
			if (it.ModItem is not IFluidHandlerItem handler) return;
			for (int tank = 0; tank < handler.TankCount; tank++)
				AddStack(handler.GetTank(tank), it.stack);
		}
		void AddArray(Item[]? arr)
		{
			if (arr is null) return;
			foreach (var it in arr) Add(it);
		}

		AddArray(player.inventory);
		Add(Main.mouseItem);
		Add(player.trashItem);

		WalkCraftingChests(player, new HashSet<Item[]>(ReferenceEqualityComparer.Instance), AddArray);

		return fluids;
	}

	// Any one matching fluid (exact/tag/attribute) in sufficient quantity counts.
	private static bool HasFluid(Api.Recipe.Content.Content content, Dictionary<string, int> fluids)
	{
		var ing = (Ingredient)content.Payload;
		FluidIngredient? fi = Inner(ing) switch
		{
			FluidIngredient direct        => direct,
			FluidContainerIngredient fc   => fc.Fluid,
			_                             => null,
		};
		if (fi is null) return true;       // not a fluid ingredient - ignore here
		int needed = fi.Amount;
		if (needed <= 0) return true;

		if (fi.ExactType is { } exact)
			return fluids.TryGetValue(exact.Id, out int a) && a >= needed;

		foreach (var f in fi.GetFluids())
			if (fluids.TryGetValue(f.Id, out int a) && a >= needed) return true;
		return false;
	}

	private static int CountFor(Api.Recipe.Content.Content content)
	{
		// Don't unwrap via Inner() - amount lives on the OUTERMOST wrapper,
		// unwrapping would silently collapse every count to 1.
		var ing = (Ingredient)content.Payload;
		return ing switch
		{
			SizedIngredient sized       => sized.Amount,
			IntProviderIngredient ipi   => ipi.RollSampledCount(),
			_                           => 1,
		};
	}

	// ItemStack/NBT check one type; TagIngredient sums across resolved members
	// (vanilla RecipeGroup semantics). Unresolved ingredients (ItemType == 0)
	// are UNSATISFIABLE - treating them as satisfied silently greenlights every
	// recipe whose ingredients fell off the resolver (the nether_wart bug).
	private static bool HasAny(Api.Recipe.Content.Content content, int needed,
		Dictionary<int, int> inv)
	{
		var ing = Inner((Ingredient)content.Payload);
		switch (ing)
		{
			case ItemStackIngredient isi:
				if (isi.ItemType <= 0) return false;
				return inv.TryGetValue(isi.ItemType, out int a) && a >= needed;
			case NBTPredicateIngredient nbt:
				if (nbt.ItemType <= 0) return false;
				return inv.TryGetValue(nbt.ItemType, out int b) && b >= needed;
			case TagIngredient tag:
			{
				var members = tag.GetItems();
				if (members.Count == 0) return false;          // tag resolved to no items
				int have = 0;
				foreach (var it in members)
				{
					if (inv.TryGetValue(it.type, out int n)) have += n;
					if (have >= needed) return true;
				}
				return false;
			}
			case IntCircuitIngredient:
				// Programmed circuit is a machine-GUI setting, not an inventory item.
				return true;
			default:
				// Unknown ingredient in the ITEM bucket - conservatively unsatisfiable.
				return false;
		}
	}

	private static Ingredient Inner(Ingredient ing) => ing switch
	{
		SizedIngredient sized      => Inner(sized.Inner),
		IntProviderIngredient ipi  => Inner(ipi.Inner),
		_                          => ing,
	};

	// Per-ingredient availability for cell tinting - quantitative analogue of
	// HasAny/HasFluid so the renderer can distinguish full / partial / none.
	internal enum AvailabilityState { None, Partial, Full }

	internal static AvailabilityState GetItemAvailability(
		Api.Recipe.Content.Content content,
		IReadOnlyDictionary<int, int> inv)
	{
		int needed = CountFor(content);
		if (needed <= 0) return AvailabilityState.None;       // not a counted item input
		var ing = Inner((Ingredient)content.Payload);
		int have = 0;
		switch (ing)
		{
			case ItemStackIngredient isi:
				if (isi.ItemType <= 0) return AvailabilityState.None;
				inv.TryGetValue(isi.ItemType, out have);
				break;
			case NBTPredicateIngredient nbt:
				if (nbt.ItemType <= 0) return AvailabilityState.None;
				inv.TryGetValue(nbt.ItemType, out have);
				break;
			case TagIngredient tag:
			{
				var members = tag.GetItems();
				if (members.Count == 0) return AvailabilityState.None;
				// Vanilla RecipeGroup semantics: sum across all members.
				foreach (var it in members)
					if (inv.TryGetValue(it.type, out int n)) have += n;
				break;
			}
			case IntCircuitIngredient:
				return AvailabilityState.None;   // machine setting, not inventory
			default:
				return AvailabilityState.None;
		}
		if (have >= needed) return AvailabilityState.Full;
		if (have > 0)       return AvailabilityState.Partial;
		return AvailabilityState.None;
	}

	internal static AvailabilityState GetFluidAvailability(
		Api.Recipe.Content.Content content,
		IReadOnlyDictionary<string, int> fluidsHeld)
	{
		var ing = (Ingredient)content.Payload;
		FluidIngredient? fi = Inner(ing) switch
		{
			FluidIngredient direct      => direct,
			FluidContainerIngredient fc => fc.Fluid,
			_                           => null,
		};
		if (fi is null) return AvailabilityState.None;
		int needed = fi.Amount;
		if (needed <= 0) return AvailabilityState.None;

		// Best-match across the ingredient's fluid set.
		int have = 0;
		if (fi.ExactType is { } exact)
		{
			fluidsHeld.TryGetValue(exact.Id, out have);
		}
		else
		{
			foreach (var f in fi.GetFluids())
				if (fluidsHeld.TryGetValue(f.Id, out int a) && a > have) have = a;
		}
		if (have >= needed) return AvailabilityState.Full;
		if (have > 0)       return AvailabilityState.Partial;
		return AvailabilityState.None;
	}

	private void UpdateChip()
	{
		if (_panel is null || _chipHint is null || _chipButton is null) return;

		_chipHint.SetText(HintFor(_mode));

		// Items/Equippable are flat cheat grids - no filter chip, always hint.
		bool filterable = _mode == BrowseMode.Recipes || _mode == BrowseMode.Loot;

		UIElement desired;
		if (!filterable || _filter == BrowseFilter.None)
		{
			desired = _chipHint;
		}
		else
		{
			string verb = _filter == BrowseFilter.Output ? "How to obtain" : "Used as ingredient";
			string name;
			if (_filterFluid is not null)
			{
				name = _filterFluidLabel ?? _filterFluid;
			}
			else if (_filterTagLabel is not null && _filterTagItems is not null)
			{
				name = $"#{_filterTagLabel}  ({_filterTagItems.Count} items)";
			}
			else
			{
				var probe = new Item();
				probe.SetDefaults(_filterItem);
				name = string.IsNullOrEmpty(probe.Name) ? $"item #{_filterItem}" : probe.Name;
			}
			_chipLabel = $"{verb}:  {name}      - click to clear";
			desired = _chipButton;
		}

		// Don't mutate panel children here - callers may sit inside DrawChildren
		// (UIRecipeList.DrawSelf -> ApplyItemFilter -> RecomputeAll -> UpdateChip);
		// Update() performs the actual swap next frame.
		_chipPending = desired;
	}

	private void ApplyPendingChipSwap()
	{
		if (_panel is null || _chipPending is null) return;
		if (ReferenceEquals(_chipPending, _chipShown)) return;
		if (_chipShown is not null) _panel.RemoveChild(_chipShown);
		_panel.Append(_chipPending);
		_chipShown = _chipPending;
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		// Rebuild on resolution / UI-scale change. BuildLayout preserves search,
		// mode and filter via instance fields.
		if (ScreenChanged()) BuildLayout();

		// Apply deferred mode + chip swaps queued during DrawChildren.
		if (_modeSwapPending)
		{
			_modeSwapPending = false;
			ApplyMode();
		}
		ApplyPendingChipSwap();

		// Modal mouse capture lives in GlobalRecipeBrowserSystem.PostUpdateInput
		// (here in UpdateUI it'd be one frame stale w.r.t. Player.ItemCheck).

		// Periodic refilter while have-only is active.
		if (_haveOnly && ++_haveOnlyTick >= 30)
		{
			_haveOnlyTick = 0;
			Refilter(_search?.Text ?? "");
		}
	}
}
