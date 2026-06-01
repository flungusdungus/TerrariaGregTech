#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Collapse of upstream's EntryTypes<T> registry - three fixed kinds, so an
// enum replaces the ResourceLocation registry + Supplier factory.
public enum EnderEntryType
{
	Item,
	Fluid,
	Redstone,
}
