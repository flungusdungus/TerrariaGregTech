#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.TerrariaCompat.Recipes;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Registry;

// Reverse tag index ("what tags does this item/fluid carry?"). Lazily inverts
// RegistryTagLoader on first lookup; member ids resolve via IngredientResolver.
// tags.json includes the material-prefix tags (datagen + live-registry dump),
// so no C#-side reconstruction is needed. Wired through TagSource at Mod.Load.
public static class TagMembership
{
	private static Dictionary<int, HashSet<string>>? _itemTags;
	private static Dictionary<string, HashSet<string>>? _fluidTags;
	private static readonly HashSet<string> Empty = new();

	public static IReadOnlyCollection<string> ItemTagsOf(Item item)
	{
		EnsureItemIndex();
		return item != null && !item.IsAir && _itemTags!.TryGetValue(item.type, out var tags)
			? tags
			: Empty;
	}

	public static IReadOnlyCollection<string> FluidTagsOf(FluidStack fluid)
	{
		EnsureFluidIndex();
		if (fluid.IsEmpty || fluid.Type is null) return Empty;
		return _fluidTags!.TryGetValue(fluid.Type.Id, out var tags) ? tags : Empty;
	}

	public static void Clear()
	{
		_itemTags = null;
		_fluidTags = null;
	}

	private static void EnsureItemIndex()
	{
		if (_itemTags != null) return;
		var idx = new Dictionary<int, HashSet<string>>();

		foreach (var tagId in RegistryTagLoader.AllItemTags)
			foreach (var memberId in RegistryTagLoader.ExpandItems(tagId))
			{
				int type = IngredientResolverImpl.Instance.ResolveItemType(memberId);
				if (type > 0) Add(idx, type, tagId);
			}

		_itemTags = idx;
	}

	private static void EnsureFluidIndex()
	{
		if (_fluidTags != null) return;
		var idx = new Dictionary<string, HashSet<string>>();

		foreach (var tagId in RegistryTagLoader.AllFluidTags)
			foreach (var memberId in RegistryTagLoader.ExpandFluids(tagId))
			{
				var fluid = IngredientResolverImpl.Instance.ResolveFluidType(memberId);
				if (fluid != null) Add(idx, fluid.Id, tagId);
			}

		_fluidTags = idx;
	}

	private static void Add<TKey>(Dictionary<TKey, HashSet<string>> idx, TKey key, string tag)
		where TKey : notnull
	{
		if (!idx.TryGetValue(key, out var set))
		{
			set = new HashSet<string>();
			idx[key] = set;
		}
		set.Add(tag);
	}
}
