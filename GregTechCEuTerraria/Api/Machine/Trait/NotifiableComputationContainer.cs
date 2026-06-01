#nullable enable
using System.Collections.Generic;
using System.Linq;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Feature;
using GregTechCEuTerraria.Api.Machine.Feature.Multiblock;
using GregTechCEuTerraria.Api.Recipe;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Optical;

namespace GregTechCEuTerraria.Api.Machine.Trait;

// Port of com.gregtechceu.gtceu.api.machine.trait.NotifiableComputation
// Container.
//
// The trait that mediates Compute Work Unit (CWU/t) flow for HPCA and
// optical-computation multis. Lives on the computation hatch part - IN
// side means the hatch consumes CWU/t (asks its controller or an adjacent
// optical pipe for them), OUT side means the hatch supplies CWU/t (caches
// the most recent cycle's output for downstream consumers).
//
// Adaptations: the OpticalPipeBlockEntity neighbour scan -> a WorldCapability
// .Perimeter walk (ScanAdjacentForProvider) that resolves either an adjacent
// optical pipe (walked via OpticalPipeNetSystem -> OpticalNetHandler) or a
// direct-adjacent IOpticalComputationProvider. Direction -> IODirection;
// GTCEu.LOGGER diagnostics dropped (silent, return 0/false as upstream does);
// the duration_is_total_cwu progress substitution writes via RecipeLogic
// .SetProgress instead of upstream's direct field; recipe.data.getBoolean ->
// RecipeDataUtil.GetBool. The IN/OUT x transmitter routing matrix, recursive
// provider walk, output-cwu rotation, and `seen` cycle guard are verbatim.
public class NotifiableComputationContainer
	: NotifiableRecipeHandlerTrait<int>, IOpticalComputationHatch, IOpticalComputationReceiver
{
	public static readonly MachineTraitType<NotifiableComputationContainer> TYPE = new(allowMultipleInstances: true);
	public override MachineTraitType TraitType => TYPE;

	public IO   HandlerIO   { get; }
	public bool Transmitter { get; }

	protected long _lastTimeStamp = long.MinValue;
	private   int  _currentOutputCwu;
	private   int  _lastOutputCwu;

	public NotifiableComputationContainer(IO handlerIO, bool transmitter) : base()
	{
		HandlerIO   = handlerIO;
		Transmitter = transmitter;
	}

	public override IO GetHandlerIO() => HandlerIO;

	public bool IsTransmitter() => Transmitter;

	// === IOpticalComputationProvider ========================================

	public virtual int RequestCWUt(int cwut, bool simulate, ICollection<IOpticalComputationProvider> seen)
	{
		long latestTimeStamp = Machine.GetOffsetTimer();
		if (_lastTimeStamp < latestTimeStamp)
		{
			_lastOutputCwu    = _currentOutputCwu;
			_currentOutputCwu = 0;
			_lastTimeStamp    = latestTimeStamp;
		}

		seen.Add(this);
		if (HandlerIO == IO.IN)
		{
			if (Transmitter)
			{
				var provider = FindControllerProvider();
				return provider?.RequestCWUt(cwut, simulate, seen) ?? 0;
			}
			else
			{
				var provider = GetOpticalNetProvider();
				return provider?.RequestCWUt(cwut, simulate, seen) ?? 0;
			}
		}
		else
		{
			// OUT side: serve from the cached output cap, decrement budget.
			_lastOutputCwu = _lastOutputCwu - cwut;
			return System.Math.Min(_lastOutputCwu, cwut);
		}
	}

	public int RequestCWUt(int cwut, bool simulate) =>
		RequestCWUt(cwut, simulate, NewSeen());

	public virtual int GetMaxCWUt(ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		if (HandlerIO == IO.IN)
		{
			if (Transmitter)
			{
				var provider = FindControllerProvider();
				return provider?.GetMaxCWUt(seen) ?? 0;
			}
			else
			{
				var provider = GetOpticalNetProvider();
				return provider?.GetMaxCWUt(seen) ?? 0;
			}
		}
		else
		{
			return _lastOutputCwu;
		}
	}

	public int GetMaxCWUt() => GetMaxCWUt(NewSeen());

	public virtual bool CanBridge(ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		if (HandlerIO == IO.IN)
		{
			if (Transmitter)
			{
				var provider = FindControllerProvider();
				return provider?.CanBridge(seen) ?? false;
			}
			else
			{
				var provider = GetOpticalNetProvider();
				// Upstream: nothing found -> don't report a problem, pass quietly.
				return provider == null || provider.CanBridge(seen);
			}
		}
		return false;
	}

	public bool CanBridge() => CanBridge(NewSeen());

	// Walk the IN+Transmitter side: machine is either the provider itself
	// or a part whose controller (or a controller-attached trait) implements
	// IOpticalComputationProvider.
	//
	// Order note (upstream-EQUIVALENT, not upstream-literal): upstream checks
	// `machine instanceof IOpticalComputationProvider` FIRST, then the IMultiPart
	// controller-walk - which is correct upstream because upstream's
	// OpticalComputationHatchMachine is a PLAIN part (NOT a provider), so the
	// first check always falls through to the controller-walk. Our port made the
	// hatch PART implement IOpticalComputationHatch (so WorldCapability.Get and
	// NetworkSwitch/ResearchStation can resolve it as an endpoint - a pervasive
	// TerrariaCompat deviation). With the literal order, the hatch's own delegate
	// would match `is IOpticalComputationProvider` and return ITSELF, then the
	// container loops back through it and the cycle guard short-circuits to 0
	// (the "Available: 0 CWU/t" bug). Checking the IMultiPart branch first
	// reproduces upstream's controller-walk for EVERY realizable configuration:
	// upstream has no machine that is both a part and a provider, so this yields
	// identical results to upstream while tolerating our part-is-hatch deviation.
	private IOpticalComputationProvider? FindControllerProvider()
	{
		var machine = Machine;
		if (machine is IMultiPart part)
		{
			if (!part.IsFormed()) return null;
			foreach (var controller in part.GetControllers())
			{
				if (controller is IOpticalComputationProvider cp) return cp;
				foreach (var trait in controller.Traits.AllTraits)
				{
					if (trait is IOpticalComputationProvider tp) return tp;
				}
			}
			return null;
		}
		if (machine is IOpticalComputationProvider direct) return direct;
		return null;
	}

	// === Recipe handling ===================================================

	public override List<int>? HandleRecipeInner(IO io, GTRecipe recipe, List<int> left, bool simulate)
	{
		var provider = GetOpticalNetProvider();
		if (provider == null) return left;

		int sum = left.Sum();
		if (io == IO.IN)
		{
			int availableCWUt = RequestCWUt(int.MaxValue, simulate: true);
			if (availableCWUt >= sum)
			{
				bool totalCwuMode = Recipe.RecipeDataUtil.GetBool(recipe.Data, "duration_is_total_cwu");
				if (totalCwuMode)
				{
					int drawn = provider.RequestCWUt(availableCWUt, simulate);
					if (!simulate)
					{
						// Substitute the per-tick progress +1 with the actual
						// CWU drawn - upstream parity. Routes through the
						// machine's RecipeLogic.
						var rlm = Machine as IRecipeLogicMachine;
						if (rlm == null && Machine is IMultiPart mp)
						{
							foreach (var c in mp.GetControllers())
							{
								if (c is IRecipeLogicMachine controllerRlm) { rlm = controllerRlm; break; }
							}
						}
						if (rlm != null)
						{
							var rl = rlm.GetRecipeLogic();
							// Substitute the per-tick +1 progress with the
							// actual CWU drawn - upstream's direct-field
							// mutation routed through our SetProgress.
							rl.SetProgress(rl.GetProgress() - 1 + drawn);
						}
					}
					sum -= drawn;
				}
				else
				{
					sum -= provider.RequestCWUt(sum, simulate);
				}
			}
		}
		else if (io == IO.OUT)
		{
			int canInput = GetMaxCWUt() - _lastOutputCwu;
			if (!simulate)
				_currentOutputCwu = System.Math.Min(canInput, sum);
			sum -= canInput;
		}
		// DEVIATION: upstream's
		// NotifiableComputationContainer.handleRecipeInner returns `List.of()`
		// (EMPTY list) on full consume. But RecipeHandlerList.handleRecipe (locked,
		// verbatim upstream) only treats a NULL return as "capability consumed ->
		// remove from the dispatch map" - and our 3 sibling handlers
		// (NotifiableItemStackHandler / NotifiableFluidTank / NotifiableEnergy
		// Container) all correctly return `left.Count == 0 ? null : left`. Upstream's
		// CWU container returning empty (not null) is a latent upstream inconsistency:
		// the consumed CWU cap is left in the map as `{CWU: emptyList}`, so the
		// dispatcher's `copied.Count > 0` IN-check stays true, the simulate-SUCCESS
		// branch is skipped, and the tail reports `insufficient_in` against the
		// original contents (the "research won't start at 44/32 CWU" bug). Returning
		// null here aligns the CWU container with the null-on-consume protocol every
		// other handler + RecipeHandlerList already use. Same INTENT as upstream,
		// fixes the bug; the one upstream `List.of()` literal is the outlier.
		return sum <= 0 ? null : new List<int> { sum };
	}

	public override IReadOnlyList<object> GetContents() => new object[] { _lastOutputCwu };
	public override double GetTotalContentAmount() => _lastOutputCwu;
	public override RecipeCapability<int> GetCapability() => CWURecipeCapability.CAP;

	// === IOpticalComputationReceiver ========================================

	public virtual IOpticalComputationProvider? GetComputationProvider()
	{
		if (HandlerIO.Supports(IO.OUT)) return this;
		var machine = Machine;
		if (machine is IOpticalComputationReceiver receiver) return receiver.GetComputationProvider();
		if (machine is IOpticalComputationProvider provider) return provider;
		if (machine is IRecipeCapabilityHolder holder)
		{
			var cwuList = holder.GetCapabilitiesFlat(IO.IN, CWURecipeCapability.CAP);
			if (cwuList.Count > 0 && cwuList[0] is IOpticalComputationProvider firstProvider && firstProvider != this)
				return firstProvider;
		}
		// Adjacent-machine perimeter walk - last resort fallback.
		return ScanAdjacentForProvider();
	}

	// Walks the machine's footprint perimeter for any neighbour exposing
	// `IOpticalComputationProvider`. Replaces upstream's per-side BlockEntity
	// capability lookup for `OpticalPipeBlockEntity` - pipes aren't ported,
	// but adjacent machines implementing the interface satisfy the same role.
	private IOpticalComputationProvider? GetOpticalNetProvider() => ScanAdjacentForProvider();

	private IOpticalComputationProvider? ScanAdjacentForProvider()
	{
		if (Machine is not MetaMachine self) return null;
		foreach (var (side, x, y) in WorldCapability.Perimeter(self))
		{
			// (a) adjacent cell is an optical pipe -> walk the net for the unique
			// computation endpoint via an OpticalNetHandler. Mirrors how the
			// laser hatch resolves pipe-or-direct in LaserHatchPartMachine.OnTick.
			if (OpticalPipeLayerSystem.Pipes.Has(x, y))
			{
				var net = OpticalPipeNetSystem.Level.GetNetFromPos((x, y));
				if (net != null)
					return new OpticalNetHandler(net, (x, y), side.Opposite());
				continue;
			}
			// (b) direct-adjacent computation provider (no pipe hop). This is a
			// TerrariaCompat addition - upstream's getOpticalNetProvider resolves
			// ONLY through optical pipes (path a). The convenience is "place the
			// receiver next to a TRANSMITTER hatch" (see tooltip), so it must only
			// accept a SOURCE. A reception hatch is itself an IOpticalComputation
			// Provider (it provides to ITS controller), so without this guard two
			// receivers placed close together resolve to each other and both read 0
			// (a is a sink for b, b is a sink for a). Skip other reception hatches;
			// accept transmitters + non-hatch providers (e.g. a bare HPCA).
			var provider = WorldCapability.Get<IOpticalComputationProvider>(x, y);
			if (provider != null && provider != this)
			{
				if (provider is IOpticalComputationHatch hatch && !hatch.IsTransmitter())
					continue;
				return provider;
			}
		}
		return null;
	}

	// === Helpers ============================================================
	private ICollection<IOpticalComputationProvider> NewSeen()
	{
		var s = new HashSet<IOpticalComputationProvider>();
		s.Add(this);
		return s;
	}
}
