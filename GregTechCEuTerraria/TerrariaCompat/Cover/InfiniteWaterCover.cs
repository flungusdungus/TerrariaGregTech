#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Fluids;
using GregTechCEuTerraria.Api.Machine;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.InfiniteWaterCover. Pumps free water every 20 ticks.
// Adaptations: FluidUtil.getFluidHandler -> CoverHolder cast; getOffsetTimer
// stagger dropped (phase doesn't matter for 20-tick top-up).
public sealed class InfiniteWaterCover : CoverBehavior
{
	private const int FillAmount = 16_000;   // 16 x BUCKET_VOLUME (verbatim)

	private TickableSubscription? _subscription;
	private static FluidType? _water;

	public InfiniteWaterCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override bool CanAttach() =>
		base.CanAttach() && CoverHolder is IFluidHandler { TankCount: > 0 };

	public override void OnLoad()
	{
		base.OnLoad();
		_subscription = CoverHolder.SubscribeServerTick(Update);
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		_subscription?.Unsubscribe();
		_subscription = null;
	}

	private void Update()
	{
		if (Main.GameUpdateCount % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(20) != 0) return;
		if (CoverHolder is not IFluidHandler handler) return;
		_water ??= FluidRegistry.TryGet("water", out var water) ? water : null;
		if (_water is null) return;
		handler.Fill(new FluidStack(_water, FillAmount), simulate: false);
	}
}
