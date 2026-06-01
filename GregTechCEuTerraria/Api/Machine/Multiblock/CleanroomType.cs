#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Machine.Multiblock;

// LOCKED - verbatim port of com.gregtechceu.gtceu.api.machine.multiblock.CleanroomType.
//
// A named cleanroom kind. Two are predefined (regular `cleanroom` and
// `sterile_cleanroom`); new types can register by constructing a new instance
// with a unique name. Each carries a translation key for its display label.
//
// Used by `CleanroomCondition` (already ported, currently storing the bare
// string id - that string is this type's `Name`; we can migrate
// `CleanroomCondition` to hold the typed instance once the multiblock
// scaffolding is further along).
//
// Adaptations:
//   - Mojang `Codec<CleanroomType>` dropped - we serialise as the bare string
//     id (the existing `CleanroomCondition` already does); `GetByName` is the
//     deserialise side.
public class CleanroomType
{
	private static readonly Dictionary<string, CleanroomType> _byName = new();

	public static readonly CleanroomType CLEANROOM         = new("cleanroom",         "gtceu.recipe.cleanroom.display_name");
	public static readonly CleanroomType STERILE_CLEANROOM = new("sterile_cleanroom", "gtceu.recipe.cleanroom_sterile.display_name");

	public string Name { get; }
	public string TranslationKey { get; }

	public CleanroomType(string name, string translationKey)
	{
		if (_byName.ContainsKey(name))
			throw new System.ArgumentException($"CleanroomType with name {name} is already registered!");
		Name = name;
		TranslationKey = translationKey;
		_byName[name] = this;
	}

	public static CleanroomType? GetByName(string? name) =>
		name is not null && _byName.TryGetValue(name, out var t) ? t : null;

	public static CleanroomType GetByNameOrDefault(string? name) => GetByName(name) ?? CLEANROOM;

	public static IReadOnlyCollection<CleanroomType> GetAllTypes() => _byName.Values;
}
