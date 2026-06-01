#nullable enable
namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;

// Verbatim port of ItemPipeType enum. Restrictive multipliers (150/100/75/50)
// sort restrictive routes dead-last (last-resort path).
internal static class ItemPipeSizeModifier
{
	public readonly record struct Modifier(float RateMultiplier, float ResistanceMultiplier);

	public static Modifier For(PipeSize size, bool restrictive) => (size, restrictive) switch
	{
		(PipeSize.Small,  false) => new(0.5f, 1.5f),
		(PipeSize.Normal, false) => new(1.0f, 1.0f),
		(PipeSize.Large,  false) => new(2.0f, 0.75f),
		(PipeSize.Huge,   false) => new(4.0f, 0.5f),

		(PipeSize.Small,  true)  => new(0.5f, 150f),
		(PipeSize.Normal, true)  => new(1.0f, 100f),
		(PipeSize.Large,  true)  => new(2.0f, 75f),
		(PipeSize.Huge,   true)  => new(4.0f, 50f),

		// tiny/quadruple/nonuple are fluid-only; safety-belt default.
		_ => new(1.0f, 1.0f),
	};
}
