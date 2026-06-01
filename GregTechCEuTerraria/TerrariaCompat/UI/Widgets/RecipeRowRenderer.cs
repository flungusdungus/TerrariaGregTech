#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Rendering;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;
using RecipeContent = GregTechCEuTerraria.Api.Recipe.Content.Content;

namespace GregTechCEuTerraria.TerrariaCompat.UI.Widgets;

// Draws one recipe as `[in1][in2] -> [out1][out2]`. Items via ItemSlot.Draw,
// fluids as tinted square + amount label, circuit as the per-value sprite,
// arrow = empty progress arrow. Pure static, no per-row UIElement instances.
public static class RecipeRowRenderer
{
	public const int RowHeight = 40;
	public const int CellSize  = 36;
	public const int CellPad   = 2;
	private const int ArrowSize = 24;
	private const int LabelHeight = 10;
	// Tag-ingredient sprite cycles through the member set so all members surface.
	private const int TagCyclePeriod = 48;
	private const int CircuitCellWidth = CellSize;

	private static Asset<Texture2D>? _arrow;
	private static readonly Item[] TempSlot = { new() };
	private static Mod? _mod;
	private static Mod? GetMod() => _mod ??= ModLoader.GetMod("GregTechCEuTerraria");
	private const int StationIconSize = 22;
	private const int CraftButtonWidth  = 52;
	private const int CraftButtonHeight = 22;
	// Programmed-circuit sprites (1..33.png = values 0..32). Per upstream's
	// off-by-one: config value N -> file (N+1).png.
	private const int MaxCircuit = 32;
	private static readonly Asset<Texture2D>?[] _circuitByValue = new Asset<Texture2D>?[MaxCircuit + 1];

	// Inventory + fluid snapshots are one-tick cached upstream.
	private static Dictionary<int, int>?    _invSnapshot;
	private static Dictionary<string, int>? _fluidSnapshot;
	private static void EnsureInventorySnapshot()
	{
		_invSnapshot   = GlobalRecipeBrowserState.InventoryCountsSnapshot();
		_fluidSnapshot = GlobalRecipeBrowserState.FluidCountsSnapshot();
	}

	// Cell-backdrop tints behind input slots only (outputs aren't tinted).
	private static readonly Color TintFull    = new(60, 230, 60);
	private static readonly Color TintPartial = new(240, 200, 40);

	public static int MeasureWidth(GTRecipe recipe)
	{
		var inputs  = AllInputs(recipe);
		var outputs = AllOutputs(recipe);
		int circuit = ExtractCircuit(recipe, out _);
		// AllInputs already includes the circuit input - subtract once since
		// the circuit cell uses its own dedicated width.
		int cells = inputs.Count + outputs.Count - (circuit >= 0 ? 1 : 0);
		int w = cells * (CellSize + CellPad) + ArrowSize + 12;
		if (circuit >= 0) w += CircuitCellWidth + CellPad;
		return w;
	}

	// `lightColor` lets callers dim filtered-out rows.
	public static void Draw(SpriteBatch sb, Rectangle bounds, GTRecipe recipe, Color lightColor)
	{
		EnsureInventorySnapshot();
		int x = bounds.X + 4;
		int cy = bounds.Y + 2;

		int circuit = ExtractCircuit(recipe, out var circuitContent);
		if (circuit >= 0)
		{
			DrawCircuitCell(sb, new Rectangle(x, cy, CircuitCellWidth, CellSize), circuit, lightColor);
			x += CircuitCellWidth + CellPad;
		}

		foreach (var c in AllInputs(recipe))
		{
			if (ReferenceEquals(c, circuitContent)) continue;
			DrawIngredient(sb, new Rectangle(x, cy, CellSize, CellSize), c, lightColor, isOutput: false);
			x += CellSize + CellPad;
		}

		DrawArrow(sb, new Rectangle(x + 2, cy + (CellSize - ArrowSize) / 2, ArrowSize, ArrowSize), lightColor);
		x += ArrowSize + 6;

		foreach (var c in AllOutputs(recipe))
		{
			DrawIngredient(sb, new Rectangle(x, cy, CellSize, CellSize), c, lightColor, isOutput: true);
			x += CellSize + CellPad;
		}

		// Trailing metadata: station icon + duration/EU/t + conditions.
		float labelX = x + 6;
		string stationId = recipe.RecipeType.RegistryName ?? "";
		string stationLabel = Humanize(stationId);
		int stationItemType = StationIcon.ItemTypeFor(stationId, GetMod());
		if (stationItemType > 0)
		{
			var iconRect = new Rectangle((int)labelX, (int)cy + 1, StationIconSize, StationIconSize);
			DrawItemIconFit(sb, iconRect, stationItemType, lightColor);
			labelX += StationIconSize + 4;
		}
		else if (stationLabel.Length > 0)
		{
			// Fallback for abstract stations with no block (crafting_shaped, ...).
			Terraria.Utils.DrawBorderString(sb, stationLabel,
				new Vector2(labelX, cy + 1),
				new Color(180, 220, 255), 0.7f);
		}

		// Pass-B native-proxy chip: also-craftable-at vanilla tile. Suppressed
		// when the native tile == the GT station (e.g. workbench-shapeless).
		int nativeTile = recipe.Data.GetInt("nativeTile");
		if (nativeTile > 0)
		{
			int nativeItemType = StationIcon.ItemTypeForTile(nativeTile);
			if (nativeItemType > 0 && nativeItemType != stationItemType)
			{
				var alsoRect = new Rectangle((int)labelX, (int)cy + 1, StationIconSize, StationIconSize);
				DrawItemIconFit(sb, alsoRect, nativeItemType, lightColor);
				labelX += StationIconSize + 4;
			}
		}
		long eut = recipe.InputEUt.Voltage > 0 ? recipe.InputEUt.Voltage : recipe.OutputEUt.Voltage;
		// duration_is_total_cwu (research recipes): Duration is the TOTAL CWU
		// budget, tick CWU input is the per-tick MINIMUM gating the run.
		// Show MIN CWU/t prominently + total in parens (the bare total alone
		// reads like a required rate, which it isn't).
		bool totalCwu = Api.Recipe.RecipeDataUtil.GetBool(recipe.Data, "duration_is_total_cwu");
		if (recipe.Duration > 0 || eut > 0)
		{
			// Duration is MC ticks (FromMcTicks gate preserves 20 Hz cadence).
			string durStr;
			if (totalCwu)
			{
				int minCwu = 0;
				foreach (var c in recipe.GetTickInputContents(Api.Capability.Recipe.CWURecipeCapability.CAP))
					if (c.Payload is int v) minCwu += v;
				durStr = $"{minCwu} CWU/t min ({recipe.Duration:N0} total)";
			}
			else
			{
				durStr = $"{recipe.Duration / 20.0:0.##}s";
			}
			string meta = eut > 0
				? $"{durStr} * {eut} EU/t ({VoltageTiers.ShortName(VoltageTiers.MinTierForVoltage(eut))})"
				: durStr;
			Terraria.Utils.DrawBorderString(sb, meta,
				new Vector2(labelX, cy + 14),
				lightColor, 0.7f);
		}

		string cond = ConditionSummary(recipe);
		if (cond.Length > 0)
		{
			if (cond.Length > 46) cond = cond.Substring(0, 45) + "...";
			Terraria.Utils.DrawBorderString(sb, cond,
				new Vector2(labelX, cy + 27),
				new Color(255, 210, 110) * (lightColor.A / 255f), 0.62f);
		}

		// Quick-craft chip - visible only when a matching vanilla recipe is in
		// Main.availableRecipe[]. Clicks routed in UIRecipeList.DrawSelf.
		if (FindAvailableVanillaCraft(recipe) != null)
			DrawCraftButton(sb, CraftButtonRect(bounds));
	}

	private static void DrawCraftButton(SpriteBatch sb, Rectangle btn)
	{
		var px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, btn, new Color(50, 130, 75));
		// 1-px top+left highlight for the 3D affordance.
		sb.Draw(px, new Rectangle(btn.X, btn.Y, btn.Width, 1), new Color(160, 220, 180));
		sb.Draw(px, new Rectangle(btn.X, btn.Y, 1, btn.Height), new Color(160, 220, 180));
		Terraria.Utils.DrawBorderString(sb, "Craft",
			new Vector2(btn.X + 9, btn.Y + 4), Color.White, 0.85f);
	}

	public static Rectangle CraftButtonRect(Rectangle bounds) =>
		new(bounds.Right - 6 - CraftButtonWidth,
			bounds.Y + (RowHeight - CraftButtonHeight) / 2,
			CraftButtonWidth, CraftButtonHeight);

	// Returns the SPECIFIC vanilla recipe this row was sourced from, iff it's
	// in `availableRecipe[]` (player can craft it now). `GTToVanilla` is the
	// source of truth - written by VanillaCraftingBridge and NativeRecipeProxy.
	// Identity-based: matching by output type alone would light Craft on every
	// recipe producing that item (e.g. Red Brick from clay vs from wall).
	public static Terraria.Recipe? FindAvailableVanillaCraft(GTRecipe recipe)
	{
		if (!VanillaCraftingBridge.GTToVanilla.TryGetValue(recipe, out var rec)) return null;
		if (rec is null || rec.createItem.IsAir) return null;
		int n = Main.numAvailableRecipes;
		for (int i = 0; i < n; i++)
		{
			int idx = Main.availableRecipe[i];
			if (idx < 0 || idx >= Main.recipe.Length) continue;
			if (ReferenceEquals(Main.recipe[idx], rec)) return rec;
		}
		return null;
	}

	// One line per RecipeCondition joined with " * "; reverse conditions prefixed "Not:".
	private static string ConditionSummary(GTRecipe recipe)
	{
		if (recipe.Conditions.Count == 0) return "";
		var sb = new System.Text.StringBuilder();
		foreach (var c in recipe.Conditions)
		{
			if (sb.Length > 0) sb.Append(" * ");
			sb.Append(ConditionText(c));
		}
		return sb.ToString();
	}

	private static string ConditionText(RecipeCondition c)
	{
		string s = c.GetTooltips();
		if (string.IsNullOrEmpty(s)) s = c.GetTypeName();
		return c.IsReverse ? "Not: " + s : s;
	}

	// Item type currently rendered at the cursor, or 0 if not on an ingredient cell.
	public static int HitTest(Rectangle bounds, GTRecipe recipe, Point mouse)
	{
		int x = bounds.X + 4;
		int cy = bounds.Y + 2;

		int circuit = ExtractCircuit(recipe, out var circuitContent);
		if (circuit >= 0) x += CircuitCellWidth + CellPad;

		foreach (var c in AllInputs(recipe))
		{
			if (ReferenceEquals(c, circuitContent)) continue;
			var r = new Rectangle(x, cy, CellSize, CellSize);
			if (r.Contains(mouse)) return ResolveItemType(c);
			x += CellSize + CellPad;
		}
		x += ArrowSize + 6 + 2;
		foreach (var c in AllOutputs(recipe))
		{
			var r = new Rectangle(x, cy, CellSize, CellSize);
			if (r.Contains(mouse)) return ResolveItemType(c);
			x += CellSize + CellPad;
		}
		return 0;
	}

	private static int ResolveItemType(RecipeContent content)
	{
		var ing = (Ingredient)content.Payload;
		return Inner(ing) switch
		{
			ItemStackIngredient isi    => isi.ItemType,
			NBTPredicateIngredient nbt => nbt.ItemType,
			TagIngredient tag          => tag.GetItems().Count > 0 ? tag.GetItems()[0].type : 0,
			_                          => 0,
		};
	}

	private static void DrawIngredient(SpriteBatch sb, Rectangle dest, RecipeContent content, Color lightColor, bool isOutput)
	{
		// Inventory-availability backdrop - drawn FIRST so the slot art lays
		// over a colored fill. Vanilla's slot back is semi-transparent so the
		// tint shows through as a subtle frame color. Inputs only (no "have"
		// concept for outputs).
		if (!isOutput) DrawAvailabilityBackdrop(sb, dest, content, lightColor);

		var ing = (Ingredient)content.Payload;
		if (IsFluid(ing))
		{
			DrawFluidCell(sb, dest, ing, lightColor);
			DrawChanceOverlay(sb, dest, content, lightColor, isOutput);
			return;
		}

		// Tag - cycle sprites + stamp a `*` in the top-left as the set marker.
		if (Inner(ing) is TagIngredient tag && tag.GetItems().Count > 0)
		{
			DrawItemSprite(sb, dest, CycleMember(tag), CountOf(ing), lightColor);
			DrawTagGlyph(sb, dest, lightColor);
			DrawChanceOverlay(sb, dest, content, lightColor, isOutput);
			return;
		}

		int itemType = ResolveItemType(content);
		if (itemType <= 0)
		{
			DrawUnresolved(sb, dest, ing, lightColor);
			return;
		}

		DrawItemSprite(sb, dest, itemType, CountOf(ing), lightColor, Inner(ing));
		DrawChanceOverlay(sb, dest, content, lightColor, isOutput);
	}

	// Stamp research onto a data orb so its imprinted-item preview + tooltip
	// render (instead of a blank orb). No-op for non-NBT ingredients.
	private static void StampResearchIfAny(Item item, Ingredient? ing)
	{
		if (ing is NBTPredicateIngredient nbt)
			Items.ResearchDataGlobalItem.StampFromSnbt(item, nbt.OutputNbt);
	}

	// SetDefaults + per-stack research stamp - so the cheat path spawns what's
	// shown, not a blank default.
	public static Item BuildDisplayItem(Api.Recipe.Content.Content content, int itemType)
	{
		var item = new Item();
		item.SetDefaults(itemType);
		if (content?.Payload is Ingredient ing)
			StampResearchIfAny(item, Inner(ing));
		return item;
	}

	// Green/yellow/none backdrop based on carried items vs required amount.
	// Skipped for non-counted ingredients (circuits, etc.).
	private static void DrawAvailabilityBackdrop(SpriteBatch sb, Rectangle dest, RecipeContent content, Color lightColor)
	{
		if (_invSnapshot is null || _fluidSnapshot is null) return;
		var state = IsFluid((Ingredient)content.Payload)
			? GlobalRecipeBrowserState.GetFluidAvailability(content, _fluidSnapshot)
			: GlobalRecipeBrowserState.GetItemAvailability(content, _invSnapshot);
		if (state == GlobalRecipeBrowserState.AvailabilityState.None) return;
		var tint = state == GlobalRecipeBrowserState.AvailabilityState.Full ? TintFull : TintPartial;
		sb.Draw(TextureAssets.MagicPixel.Value, dest, tint * (0.85f * lightColor.A / 255f));
	}

	// Vanilla ItemSlot.Draw (slot back + sprite + stack) scaled to fill `dest`.
	private static void DrawItemSprite(SpriteBatch sb, Rectangle dest, int itemType, int stack, Color lightColor, Ingredient? researchSrc = null)
	{
		float prev = Main.inventoryScale;
		Main.inventoryScale = dest.Width / 52f;
		try
		{
			TempSlot[0] = new Item();
			TempSlot[0].SetDefaults(itemType);
			TempSlot[0].stack = stack;
			StampResearchIfAny(TempSlot[0], researchSrc);
			ItemSlot.Draw(sb, TempSlot, ItemSlot.Context.CraftingMaterial, 0,
				new Vector2(dest.X, dest.Y), lightColor);
		}
		finally
		{
			Main.inventoryScale = prev;
		}
	}

	private static int CycleMember(TagIngredient tag)
	{
		var items = tag.GetItems();
		int idx = (int)(Main.GameUpdateCount / TagCyclePeriod % (uint)items.Count);
		return items[idx].type;
	}

	private static void DrawTagGlyph(SpriteBatch sb, Rectangle dest, Color lightColor)
	{
		Terraria.Utils.DrawBorderString(sb, "*",
			new Vector2(dest.X + 2, dest.Y - 3),
			new Color(130, 230, 130) * (lightColor.A / 255f), 1.1f);
	}

	// Top-right (clear of vanilla stack-count + fluid amount labels). Upstream
	// Content.Chance / MaxChance (out of 10000): input chance=0 = tool not
	// consumed; otherwise probabilistic consumption (in) / output (out).
	private static void DrawChanceOverlay(SpriteBatch sb, Rectangle dest, RecipeContent content, Color lightColor, bool isOutput)
	{
		int max = content.MaxChance;
		if (max <= 0 || content.Chance >= max) return;
		var font = FontAssets.MouseText.Value;
		float scale = 0.6f;

		string label;
		Color tint;
		if (!isOutput && content.Chance == 0)
		{
			label = "Tool";
			tint  = new Color(255, 200, 90) * (lightColor.A / 255f);
		}
		else
		{
			int pct = (int)(content.Chance * 100L / max);
			label = $"{pct}%";
			tint  = isOutput
				? lightColor * 0.95f
				: new Color(255, 240, 160) * (lightColor.A / 255f);
		}

		var size = font.MeasureString(label) * scale;
		Terraria.Utils.DrawBorderString(sb, label,
			new Vector2(dest.Right - size.X - 2, dest.Y + 2),
			tint, scale);
	}

	// Delegates to BrowserFluidSlot after resolving fluid + amount.
	private static void DrawFluidCell(SpriteBatch sb, Rectangle dest, Ingredient ing, Color lightColor)
	{
		ResolveFluidCell(ing, out var fluid, out int amount, out string? fallback);
		// Inset 2 px so the availability backdrop shows as a frame around the
		// opaque fluid cell (the items path gets this via vanilla's translucent back).
		var inset = new Rectangle(dest.X + 2, dest.Y + 2, dest.Width - 4, dest.Height - 4);
		BrowserFluidSlot.Draw(sb, inset, fluid, amount, fallback, lightColor);
	}

	// Shared by DrawFluidCell + EmitIngTooltip so the cell art and tooltip
	// can never disagree on what the slot is showing.
	private static void ResolveFluidCell(Ingredient ing,
		out FluidType? fluid, out int amount, out string? fallbackLabel)
	{
		fluid = null; amount = 0; fallbackLabel = null;
		if (FluidPart(ing) is { } fi)
		{
			amount = fi.Amount;
			fluid = fi.ExactType ?? (fi.GetFluids().Count > 0 ? fi.GetFluids()[0] : null);
			if (fluid is null) fallbackLabel = fi.TagName ?? fi.Attribute?.Id ?? "?";
		}
		if (Inner(ing) is IntProviderFluidIngredient ipfi) amount = ipfi.RollSampledCount();
	}

	private static void DrawUnresolved(SpriteBatch sb, Rectangle dest, Ingredient ing, Color lightColor)
	{
		var px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, dest, new Color(60, 30, 30) * (lightColor.A / 255f));
		TankFrame.DrawBorder(sb, dest, TankFrame.BorderColor * (lightColor.A / 255f));

		string raw = ShortLabel(ing);
		if (raw.Length > 8) raw = raw.Substring(0, 8);
		Terraria.Utils.DrawBorderString(sb, raw,
			new Vector2(dest.X + 2, dest.Y + 8),
			lightColor * 0.7f, 0.55f);
	}

	private static void DrawCircuitCell(SpriteBatch sb, Rectangle dest, int circuit, Color lightColor)
	{
		var sprite = CircuitSprite(circuit);
		if (sprite is null) { DrawCircuitFallback(sb, dest, circuit, lightColor); return; }
		PointClampDraw.Draw(sb, () =>
			sb.Draw(sprite, dest, null, lightColor, 0f, Vector2.Zero, SpriteEffects.None, 0f));
	}

	private static Texture2D? CircuitSprite(int value)
	{
		if (value < 0 || value > MaxCircuit) return null;
		int fileIndex = value + 1;
		_circuitByValue[value] ??= ModContent.Request<Texture2D>(
			$"GregTechCEuTerraria/Content/Textures/item/programmed_circuit/{fileIndex}");
		return _circuitByValue[value]?.Value;
	}

	private static void DrawCircuitFallback(SpriteBatch sb, Rectangle dest, int circuit, Color lightColor)
	{
		var px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, dest, new Color(15, 50, 55) * (lightColor.A / 255f));
		TankFrame.DrawBorder(sb, dest, TankFrame.BorderColor * (lightColor.A / 255f));
		Terraria.Utils.DrawBorderString(sb, circuit.ToString(),
			new Vector2(dest.X + 4, dest.Y + 4), lightColor, 1.0f);
	}

	private static void DrawArrow(SpriteBatch sb, Rectangle dest, Color lightColor)
	{
		_arrow ??= ModContent.Request<Texture2D>("GregTechCEuTerraria/Content/Textures/gui/progress_bar/progress_bar_arrow");
		if (_arrow?.Value is not { } tex) return;
		sb.Draw(tex, dest, new Rectangle(0, 0, 20, 20), lightColor);
	}

	// Tooltip + hit-test mirroring Draw's geometry exactly.
	public static void EmitTooltipFor(GTRecipe recipe, Rectangle bounds, Point mouse)
	{
		int x = bounds.X + 4;
		int cy = bounds.Y + 2;
		int circuit = ExtractCircuit(recipe, out var circuitContent);
		if (circuit >= 0)
		{
			if (new Rectangle(x, cy, CircuitCellWidth, CellSize).Contains(mouse))
			{
				Main.instance.MouseText($"Programmed Circuit\nValue: {circuit}");
				return;
			}
			x += CircuitCellWidth + CellPad;
		}
		foreach (var c in AllInputs(recipe))
		{
			if (ReferenceEquals(c, circuitContent)) continue;
			if (new Rectangle(x, cy, CellSize, CellSize).Contains(mouse)) { EmitIngTooltip(c, isOutput: false); return; }
			x += CellSize + CellPad;
		}
		x += ArrowSize + 6 + 2;
		foreach (var c in AllOutputs(recipe))
		{
			if (new Rectangle(x, cy, CellSize, CellSize).Contains(mouse)) { EmitIngTooltip(c, isOutput: true); return; }
			x += CellSize + CellPad;
		}

		// Trailing metadata area. Station icon -> station name + conditions;
		// elsewhere -> conditions only.
		string stationId = recipe.RecipeType.RegistryName ?? "";
		int stationItemType = StationIcon.ItemTypeFor(stationId, GetMod());
		if (stationItemType > 0)
		{
			var iconRect = new Rectangle(x + 6, cy + 1, StationIconSize, StationIconSize);
			if (iconRect.Contains(mouse))
			{
				string label = Humanize(stationId);
				var text = new System.Text.StringBuilder(label);
				if (recipe.Conditions.Count > 0)
				{
					text.Append("\nConditions:");
					foreach (var c in recipe.Conditions)
						text.Append("\n  ").Append(ConditionText(c));
				}
				Main.instance.MouseText(text.ToString());
				return;
			}
		}

		if (recipe.Conditions.Count > 0)
		{
			var text = new System.Text.StringBuilder("Conditions:");
			foreach (var c in recipe.Conditions)
				text.Append("\n  ").Append(ConditionText(c));
			Main.instance.MouseText(text.ToString());
		}
	}

	// Hover-only - must NOT push into HoverItemTracker (the browser arms a
	// suppress guard while the cursor is in its panels; clicks bypass it).
	private static void EmitIngTooltip(RecipeContent content, bool isOutput)
	{
		var ing = (Ingredient)content.Payload;
		string chanceLine = "";
		int max = content.MaxChance;
		if (max > 0 && content.Chance < max)
		{
			chanceLine = content.Chance == 0
				? "\nTool - not consumed"
				: $"\nChance: {(int)(content.Chance * 100L / max)}%";
		}

		if (IsFluid(ing))
		{
			ResolveFluidCell(ing, out var fluid, out int amount, out string? fallback);
			// FluidContainerIngredient is satisfied by a filled bucket/cell.
			string containerNote = Inner(ing) is FluidContainerIngredient
				? "\nin a filled container (bucket / cell)" : "";
			BrowserFluidSlot.EmitTooltip(fluid, amount, fallback, containerNote + chanceLine);
			return;
		}

		// Tag ingredient - name the tag, then list every acceptable member.
		if (Inner(ing) is TagIngredient tag && tag.GetItems().Count > 0)
		{
			var members = tag.GetItems();
			var text = new System.Text.StringBuilder();
			text.Append('#').Append(StripNs(tag.TagName));
			text.Append("\n").Append(CountOf(ing)).Append("x - accepts any of:");
			const int maxListed = 14;
			for (int i = 0; i < members.Count && i < maxListed; i++)
				text.Append("\n  ").Append(members[i].Name);
			if (members.Count > maxListed)
				text.Append("\n  +").Append(members.Count - maxListed).Append(" more");
			text.Append(chanceLine);
			Main.instance.MouseText(text.ToString());
			return;
		}

		int itemType = ResolveItemType(content);
		if (itemType > 0)
		{
			int count = CountOf(ing);
			if (chanceLine.Length == 0)
			{
				Main.HoverItem = new Item();
				Main.HoverItem.SetDefaults(itemType);
				Main.HoverItem.stack = count;
				StampResearchIfAny(Main.HoverItem, Inner(ing));
				Main.instance.MouseText("");
			}
			else
			{
				var probe = new Item();
				probe.SetDefaults(itemType);
				StampResearchIfAny(probe, Inner(ing));
				Main.instance.MouseText($"{probe.Name}\n{count}x{chanceLine}");
			}
			return;
		}
		Main.instance.MouseText($"{ShortLabel(ing)}\n(unresolved){chanceLine}");
	}

	// Content (Ingredient + chance) under the cursor, used for click-driven
	// filter selection. Same hit-test geometry as EmitTooltipFor.
	public static RecipeContent? IngredientAt(GTRecipe recipe, Rectangle bounds, Point mouse)
	{
		int x = bounds.X + 4;
		int cy = bounds.Y + 2;
		int circuit = ExtractCircuit(recipe, out var circuitContent);
		if (circuit >= 0) x += CircuitCellWidth + CellPad;
		foreach (var c in AllInputs(recipe))
		{
			if (ReferenceEquals(c, circuitContent)) continue;
			if (new Rectangle(x, cy, CellSize, CellSize).Contains(mouse)) return c;
			x += CellSize + CellPad;
		}
		x += ArrowSize + 6 + 2;
		foreach (var c in AllOutputs(recipe))
		{
			if (new Rectangle(x, cy, CellSize, CellSize).Contains(mouse)) return c;
			x += CellSize + CellPad;
		}
		return null;
	}

	// Item types touched by the recipe (input union output) for HoverItemTracker.
	public static HashSet<int> ItemTypesInRecipe(GTRecipe recipe)
	{
		var set = new HashSet<int>();
		foreach (var c in recipe.GetInputContents(ItemRecipeCapability.CAP))
			AddItemTypes(c, set);
		foreach (var c in recipe.GetOutputContents(ItemRecipeCapability.CAP))
			AddItemTypes(c, set);
		return set;
	}

	// Item types consumed - backs U "used as ingredient".
	public static HashSet<int> InputItemTypesInRecipe(GTRecipe recipe)
	{
		var set = new HashSet<int>();
		foreach (var c in recipe.GetInputContents(ItemRecipeCapability.CAP))
			AddItemTypes(c, set);
		return set;
	}

	// Item types produced - backs R "how to obtain".
	public static HashSet<int> OutputItemTypesInRecipe(GTRecipe recipe)
	{
		var set = new HashSet<int>();
		foreach (var c in recipe.GetOutputContents(ItemRecipeCapability.CAP))
			AddItemTypes(c, set);
		return set;
	}

	// Tag -> every member; FluidContainerIngredient -> every candidate container.
	private static void AddItemTypes(RecipeContent c, HashSet<int> sink)
	{
		if (Inner((Ingredient)c.Payload) is TagIngredient tag)
		{
			foreach (var item in tag.GetItems())
				if (item.type > Terraria.ID.ItemID.None) sink.Add(item.type);
			return;
		}
		if (Inner((Ingredient)c.Payload) is FluidContainerIngredient fc)
		{
			foreach (var item in fc.GetItems())
				if (item.type > Terraria.ID.ItemID.None) sink.Add(item.type);
			return;
		}
		int t = ResolveItemType(c);
		if (t > 0) sink.Add(t);
	}

	// Fluid ids (FluidRegistry bare keys) touched by the recipe. Unresolved
	// refs still contribute (namespace stripped) so "water" matches "minecraft:water".
	public static HashSet<string> FluidIdsInRecipe(GTRecipe recipe)
	{
		var set = new HashSet<string>();
		foreach (var c in recipe.GetInputContents(FluidRecipeCapability.CAP)) AddFluidIds(c, set);
		foreach (var c in recipe.GetOutputContents(FluidRecipeCapability.CAP)) AddFluidIds(c, set);
		return set;
	}

	public static HashSet<string> InputFluidIdsInRecipe(GTRecipe recipe)
	{
		var set = new HashSet<string>();
		foreach (var c in recipe.GetInputContents(FluidRecipeCapability.CAP)) AddFluidIds(c, set);
		return set;
	}

	public static HashSet<string> OutputFluidIdsInRecipe(GTRecipe recipe)
	{
		var set = new HashSet<string>();
		foreach (var c in recipe.GetOutputContents(FluidRecipeCapability.CAP)) AddFluidIds(c, set);
		return set;
	}

	private static void AddFluidIds(RecipeContent c, HashSet<string> sink)
	{
		if (Inner((Ingredient)c.Payload) is not FluidIngredient fi) return;
		if (fi.ExactType is not null) { sink.Add(fi.ExactType.Id); return; }
		foreach (var f in fi.GetFluids()) sink.Add(f.Id);
		if (fi.TagName is not null)
		{
			int colon = fi.TagName.IndexOf(':');
			sink.Add(colon >= 0 ? fi.TagName.Substring(colon + 1) : fi.TagName);
		}
	}

	// Items then fluids - matches legacy display order.
	private static List<RecipeContent> AllInputs(GTRecipe recipe)
	{
		var list = new List<RecipeContent>();
		list.AddRange(recipe.GetInputContents(ItemRecipeCapability.CAP));
		list.AddRange(recipe.GetInputContents(FluidRecipeCapability.CAP));
		return list;
	}

	private static List<RecipeContent> AllOutputs(GTRecipe recipe)
	{
		var list = new List<RecipeContent>();
		list.AddRange(recipe.GetOutputContents(ItemRecipeCapability.CAP));
		list.AddRange(recipe.GetOutputContents(FluidRecipeCapability.CAP));
		return list;
	}

	// First IntCircuitIngredient input (peeled), or (-1, null). The carrier
	// lets the renderer skip the circuit from the normal input loop.
	private static int ExtractCircuit(GTRecipe recipe, out RecipeContent? carrier)
	{
		foreach (var c in recipe.GetInputContents(ItemRecipeCapability.CAP))
		{
			if (Inner((Ingredient)c.Payload) is IntCircuitIngredient ic)
			{
				carrier = c;
				return ic.Configuration;
			}
		}
		carrier = null;
		return -1;
	}

	// Peels SizedIngredient / IntProviderIngredient. Upstream RecipeCapability
	// dispatches on the inner; wrappers carry count/sampling info only.
	private static Ingredient Inner(Ingredient ing) => ing switch
	{
		SizedIngredient sized      => Inner(sized.Inner),
		IntProviderIngredient ipi  => Inner(ipi.Inner),
		_                          => ing,
	};

	private static int CountOf(Ingredient ing) => ing switch
	{
		SizedIngredient sized      => sized.Amount,
		IntProviderIngredient ipi  => ipi.RollSampledCount(),
		_                          => 1,
	};

	private static bool IsFluid(Ingredient ing) =>
		Inner(ing) is FluidIngredient or IntProviderFluidIngredient or FluidContainerIngredient;

	// The FluidIngredient a content displays as. FluidContainerIngredient is
	// an ITEM ingredient but reads best as "this much of this fluid".
	private static FluidIngredient? FluidPart(Ingredient ing) => Inner(ing) switch
	{
		FluidIngredient fi          => fi,
		FluidContainerIngredient fc => fc.Fluid,
		_                           => null,
	};

	private static string ShortLabel(Ingredient ing) => Inner(ing) switch
	{
		ItemStackIngredient isi      => string.IsNullOrEmpty(isi.UpstreamId) ? $"i{isi.ItemType}" : StripNs(isi.UpstreamId),
		TagIngredient tag            => "#" + StripNs(tag.TagName),
		NBTPredicateIngredient nbt   => StripNs(nbt.UpstreamId),
		FluidIngredient fi           => fi.ExactType?.Id ?? fi.TagName ?? fi.Attribute?.Id ?? "?",
		FluidContainerIngredient fc  => fc.Fluid.ExactType?.Id ?? fc.Fluid.TagName ?? "?",
		_                            => ing.GetTypeName(),
	};

	private static string StripNs(string id)
	{
		int colon = id.IndexOf(':');
		return colon >= 0 ? id.Substring(colon + 1) : id;
	}

	// Inline item icon via ItemSlot.Draw (same path UISlot uses) - vanilla owns
	// frame + sprite + glow + scale fit. Main.inventoryScale = target /
	// VanillaNativeSlotPixels; restored in try/finally so it doesn't leak.
	private static readonly Item[] _stationSlotItem = { new() };
	private const float VanillaNativeSlotPixels = 52f;
	private static void DrawItemIconFit(SpriteBatch sb, Rectangle dest, int itemType, Color tint)
	{
		if (itemType <= 0 || itemType >= TextureAssets.Item.Length) return;
		_stationSlotItem[0].SetDefaults(itemType);
		float oldScale = Main.inventoryScale;
		Main.inventoryScale = dest.Width / VanillaNativeSlotPixels;
		try
		{
			ItemSlot.Draw(sb, _stationSlotItem, ItemSlot.Context.CraftingMaterial, 0,
				new Vector2(dest.X, dest.Y));
		}
		finally
		{
			Main.inventoryScale = oldScale;
		}
	}

	// "chemical_reactor" -> "Chemical Reactor".
	private static string Humanize(string snake)
	{
		if (string.IsNullOrEmpty(snake)) return snake;
		var sb = new System.Text.StringBuilder(snake.Length);
		bool capNext = true;
		foreach (char c in snake)
		{
			if (c == '_') { sb.Append(' '); capNext = true; continue; }
			sb.Append(capNext ? char.ToUpperInvariant(c) : c);
			capNext = false;
		}
		return sb.ToString();
	}
}
