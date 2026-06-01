#nullable enable
namespace GregTechCEuTerraria.Api.Cover.Data;

// Port of com.gregtechceu.gtceu.common.cover.data.TransferMode (robot arm /
// fluid regulator).
public enum TransferMode
{
	TransferAny,
	TransferExact,
	KeepExact,
}

public static class TransferModeExtensions
{
	// Port of TransferMode's per-value maxStackSize (1 / 1024 / 1024) - the
	// cap a robot arm imposes on its filter's configured per-type amount.
	public static int MaxStackSize(this TransferMode mode) => mode switch
	{
		TransferMode.TransferAny => 1,
		_                        => 1024,
	};
}
