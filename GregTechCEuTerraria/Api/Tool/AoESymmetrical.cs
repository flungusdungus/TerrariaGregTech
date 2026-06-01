#nullable enable
namespace GregTechCEuTerraria.Api.Tool;

// Port of upstream com.gregtechceu.gtceu.api.item.tool.aoe.AoESymmetrical.
//
// A symmetrical area-of-effect mining volume: `column` extra blocks to each
// side, `row` extra blocks up/down, `layer` extra blocks deep. A drill with
// aoe(1,1,0) mines a 3x3 face (1 column + self + 1 column, same for rows).
//
// Upstream's CompoundTag read/write + increase/decrease helpers (the AoE
// config UI) are NOT ported - the in-game AoE-resize widget is deferred.
// Only the immutable definition is needed for the default drill AoE.
public sealed class AoESymmetrical
{
	public readonly int Column, Row, Layer;

	private AoESymmetrical()
	{
		Column = 0;
		Row = 0;
		Layer = 0;
	}

	private AoESymmetrical(int column, int row, int layer)
	{
		Column = column;
		Row = row;
		Layer = layer;
	}

	public static readonly AoESymmetrical ZERO = new();

	public bool IsZero() => ReferenceEquals(this, ZERO);

	public static AoESymmetrical Of(int column, int row, int layer)
	{
		return column == 0 && row == 0 && layer == 0 ? ZERO : new AoESymmetrical(column, row, layer);
	}
}
