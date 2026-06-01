#nullable enable
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.Api.Pipenet;

// Verbatim port of com.gregtechceu.gtceu.api.pipenet.Node.
//
//   data            - per-node payload (e.g. ItemPipeProperties)
//   openConnections - bitmask of UN-blocked sides; 1 = open, 0 = blocked.
//                     Bit positions match CoverSide ordinals (0..3 here vs
//                     0..5 in upstream - 4 cardinal sides only).
//   mark            - connection-group key; nodes only connect if marks
//                     match OR either side has DEFAULT_MARK (= 0).
//   isActive        - runtime flag set by the holder; influences sim only.
public sealed class Node<TData>
{
	public const int DEFAULT_MARK = 0;
	public const int ALL_OPENED   = 0b1111; // 4 cardinal sides (upstream: 0b111111)
	public const int ALL_CLOSED   = 0b0000;

	public TData Data;
	public int OpenConnections;
	public int Mark;
	public bool IsActive;

	public Node(TData data, int openConnections, int mark, bool isActive)
	{
		Data = data;
		OpenConnections = openConnections;
		Mark = mark;
		IsActive = isActive;
	}

	public bool IsBlocked(CoverSide facing) =>
		(OpenConnections & (1 << (int)facing)) == 0;
}
