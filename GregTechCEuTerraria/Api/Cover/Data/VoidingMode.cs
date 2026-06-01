#nullable enable
namespace GregTechCEuTerraria.Api.Cover.Data;

// Port of com.gregtechceu.gtceu.common.cover.data.VoidingMode.
public enum VoidingMode
{
	VoidAny,
	VoidOverflow,
}

public static class VoidingModeExtensions
{
	// Upstream VoidingMode's `maxStackSize` field - the filter slot cap the
	// advanced voiding covers apply to a SimpleItemFilter.
	public static int MaxStackSize(this VoidingMode mode) => mode switch
	{
		VoidingMode.VoidAny => 1,
		VoidingMode.VoidOverflow => 1024,
		_ => 1,
	};
}
