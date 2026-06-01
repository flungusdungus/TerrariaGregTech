#nullable enable
using System.Collections.Generic;
using System.IO;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.Api.Recipe.Ingredient;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Items;

// Per-stack research carrier for data_stick / data_orb / data_module. Terraria
// has no native per-stack NBT, so the upstream `assembly_line_research`
// CompoundTag rides this GlobalItem (same shape as MachinePortableData).
public sealed class ResearchDataGlobalItem : GlobalItem
{
	public override bool InstancePerEntity => true;

	// Null/empty = blank data item.
	public string? ResearchId;
	public string? ResearchType;   // recipe-type registry name (e.g. "assembly_line")

	private static int[]? _dataItemTypes;

	internal static int[] DataItemTypes()
	{
		if (_dataItemTypes != null) return _dataItemTypes;
		var mod = ModContent.GetInstance<GregTechCEuTerraria>();
		var list = new System.Collections.Generic.List<int>(3);
		foreach (var name in new[] { "data_stick", "data_orb", "data_module" })
			if (mod.TryFind<ModItem>(name, out var mi)) list.Add(mi.Type);
		_dataItemTypes = list.ToArray();
		return _dataItemTypes;
	}

	public static bool IsDataItemType(int type)
	{
		foreach (var t in DataItemTypes()) if (t == type) return true;
		return false;
	}

	public override bool AppliesToEntity(Item item, bool lateInstantiation) => IsDataItemType(item.type);

	// Per-stack research forces non-stackable (overrides RegistryItem's 999).
	// GlobalItem.SetDefaults runs after ModItem.SetDefaults, so this wins.
	public override void SetDefaults(Item item) => item.maxStack = 1;

	public bool HasResearch => !string.IsNullOrEmpty(ResearchId);

	public override void SaveData(Item item, TagCompound tag)
	{
		if (string.IsNullOrEmpty(ResearchId)) return;
		tag["rid"] = ResearchId!;
		tag["rtype"] = ResearchType ?? "";
	}

	public override void LoadData(Item item, TagCompound tag)
	{
		ResearchId   = tag.ContainsKey("rid")   ? tag.GetString("rid")   : null;
		ResearchType = tag.ContainsKey("rtype") ? tag.GetString("rtype") : null;
	}

	public override void NetSend(Item item, BinaryWriter writer)
	{
		bool has = HasResearch;
		writer.Write(has);
		if (has)
		{
			writer.Write(ResearchId!);
			writer.Write(ResearchType ?? "");
		}
	}

	public override void NetReceive(Item item, BinaryReader reader)
	{
		if (reader.ReadBoolean())
		{
			ResearchId   = reader.ReadString();
			ResearchType = reader.ReadString();
		}
		else { ResearchId = null; ResearchType = null; }
	}

	public override bool CanStack(Item destination, Item source) => SameResearch(destination, source);
	public override bool CanStackInWorld(Item destination, Item source) => SameResearch(destination, source);

	private static bool SameResearch(Item a, Item b)
	{
		var ar = a.GetGlobalItem<ResearchDataGlobalItem>();
		var br = b.GetGlobalItem<ResearchDataGlobalItem>();
		return string.Equals(ar.ResearchId ?? "", br.ResearchId ?? "", System.StringComparison.Ordinal);
	}

	public override GlobalItem Clone(Item? from, Item to)
	{
		var clone = (ResearchDataGlobalItem)base.Clone(from, to);
		clone.ResearchId   = ResearchId;
		clone.ResearchType = ResearchType;
		return clone;
	}

	// research_id -> recipe (via the recipe-type's dataStickEntry) -> primary
	// output = the item this orb represents. Cached per research_id.
	private int     _aboutType = -2;   // -2 uncomputed, -1 none, >0 item type
	private string? _aboutForId;

	public int AboutItemType()
	{
		if (_aboutType != -2 && _aboutForId == ResearchId) return _aboutType;
		_aboutForId = ResearchId;
		_aboutType  = ResolveAboutItemType();
		return _aboutType;
	}

	private int ResolveAboutItemType()
	{
		if (!HasResearch) return -1;
		var type = GTRecipeType.Get(StripNs(ResearchType ?? ""));
		if (type is null) return -1;
		foreach (var recipe in ResearchManager.GetRecipesFor(type, ResearchId!))
		{
			foreach (var content in recipe.GetOutputContents(ItemRecipeCapability.CAP))
			{
				int t = PeelItemType(content.Payload as Ingredient);
				if (t > 0) return t;
			}
		}
		return -1;
	}

	private static int PeelItemType(Ingredient? ing) => ing switch
	{
		SizedIngredient s        => PeelItemType(s.Inner),
		NBTPredicateIngredient n => n.ItemType,
		ItemStackIngredient i    => i.ItemType,
		TagIngredient t          => t.GetItems().Count > 0 ? t.GetItems()[0].type : 0,
		_                        => 0,
	};

	private static string StripNs(string id)
	{
		int i = id.IndexOf(':');
		return i >= 0 ? id[(i + 1)..] : id;
	}

	// Stamp from recipe-ingredient SNBT
	// (`{assembly_line_research:{research_id:"...",research_type:"..."}}`)
	// so the recipe browser shows a researched output orb, not a blank.
	public static void StampFromSnbt(Item stack, string? snbt)
	{
		if (stack is null || stack.IsAir || string.IsNullOrEmpty(snbt)) return;
		if (!IsDataItemType(stack.type)) return;
		string rid = ExtractQuoted(snbt!, "research_id");
		if (string.IsNullOrEmpty(rid)) return;
		var blob = stack.GetGlobalItem<ResearchDataGlobalItem>();
		blob.ResearchId   = rid;
		blob.ResearchType = StripNs(ExtractQuoted(snbt!, "research_type"));
	}

	private static string ExtractQuoted(string snbt, string key)
	{
		int k = snbt.IndexOf(key, System.StringComparison.Ordinal);
		if (k < 0) return "";
		int q1 = snbt.IndexOf('"', k);
		if (q1 < 0) return "";
		int q2 = snbt.IndexOf('"', q1 + 1);
		if (q2 < 0) return "";
		return snbt.Substring(q1 + 1, q2 - q1 - 1);
	}

	public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
	{
		if (!HasResearch) return;                  // blank items get no extra line

		int about = AboutItemType();
		string subject = about > 0 ? Lang.GetItemNameValue(about) : ResearchId!;
		tooltips.Add(new TooltipLine(Mod, "GTResearch", $"Research Data: {subject}")
		{ OverrideColor = new Color(85, 255, 255) });
		tooltips.Add(new TooltipLine(Mod, "GTResearchUse",
			"Insert into a Data Access Hatch to unlock its Assembly Line recipe")
		{ OverrideColor = new Color(170, 170, 170) });
	}

	// Preview the researched item small + centred ("this orb contains X").
	public override void PostDrawInInventory(Item item, SpriteBatch sb, Vector2 position,
		Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
		=> DrawPreview(sb, position, scale * 0.5f);

	public override void PostDrawInWorld(Item item, SpriteBatch sb, Color lightColor,
		Color alphaColor, float rotation, float scale, int whoAmI)
		=> DrawPreview(sb, item.Center - Main.screenPosition, scale * 0.5f);

	private void DrawPreview(SpriteBatch sb, Vector2 center, float scale)
	{
		int about = AboutItemType();
		if (about <= 0) return;
		Main.instance.LoadItem(about);
		var tex = TextureAssets.Item[about].Value;
		if (tex is null) return;
		Rectangle src = Main.itemAnimations[about] is { } anim ? anim.GetFrame(tex) : tex.Frame();
		sb.Draw(tex, center, src, Color.White, 0f, src.Size() / 2f, scale, SpriteEffects.None, 0f);
	}
}
