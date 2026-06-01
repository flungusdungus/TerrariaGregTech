#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Electric.Research;

// Port of NetworkSwitchMachine. Computation router - aggregates CWU/t from
// reception hatches, serves to transmitter hatches, allows HPCA chaining
// (CanBridge=true). Extends DataBankMachine; EU upkeep per hatch = VA[IV].
public class NetworkSwitchMachine : DataBankMachine, IOpticalComputationProvider
{
	// Upstream VA[IV] = 7680 (V x 15/16), NOT V[IV] = 8192.
	public static readonly long NS_EUT_PER_HATCH = VoltageTiers.VA((int)VoltageTier.IV);

	private MultipleComputationHandler? _computationHandler;

	// Handler is server-only (gathered in OnStructureForm); MP clients read this.
	private int _syncMaxComputation;

	public NetworkSwitchMachine() : base() { }

	private void EnsureHandler()
	{
		if (_computationHandler != null) return;
		_computationHandler = new MultipleComputationHandler(this);
		Traits.Attach(_computationHandler);
		Traits.RegisterPersistent("NSComputation", _computationHandler);
	}

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		EnsureHandler();
	}

	protected override long CalculateEnergyUsage()
	{
		int receivers = 0, transmitters = 0;
		foreach (var part in GetParts())
		{
			if (part is OpticalComputationHatchMachine ch)
			{
				if (ch.Transmitter) transmitters++; else receivers++;
			}
		}
		return NS_EUT_PER_HATCH * (receivers + transmitters);
	}

	public override void OnStructureFormed()
	{
		base.OnStructureFormed();
		EnsureHandler();
		var receivers    = new List<IOpticalComputationHatch>();
		var transmitters = new List<IOpticalComputationHatch>();
		foreach (var part in GetParts())
		{
			if (part is OpticalComputationHatchMachine ch)
			{
				if (ch.Transmitter) transmitters.Add(ch);
				else                receivers.Add(ch);
			}
		}
		_computationHandler!.OnStructureForm(receivers, transmitters);
	}

	public override void OnStructureInvalid()
	{
		base.OnStructureInvalid();
		_computationHandler?.Reset();
	}

	public override long GetEnergyUsage() => IsFormed ? (_computationHandler?.EUt ?? 0) : 0;

	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer || !IsFormed) return;
		_syncMaxComputation = _computationHandler?.GetMaxCWUtForDisplay() ?? 0;
	}

	// Combined max CWU/t across bridging providers; client reads synced mirror.
	public int MaxComputationForDisplay =>
		(_computationHandler?.ProviderCount ?? 0) > 0
			? (_computationHandler?.GetMaxCWUtForDisplay() ?? 0)
			: _syncMaxComputation;

	public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.SaveData(tag);
		tag["ns_maxComp"] = _syncMaxComputation;
	}

	public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
	{
		base.LoadData(tag);
		if (tag.ContainsKey("ns_maxComp")) _syncMaxComputation = tag.GetInt("ns_maxComp");
	}

	// === IOpticalComputationProvider (delegate to handler) =================

	public int RequestCWUt(int cwut, bool simulate, ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		return IsActive && !GetRecipeLogic().IsWaiting()
			? (_computationHandler?.RequestCWUt(cwut, simulate, seen) ?? 0)
			: 0;
	}

	public int RequestCWUt(int cwut, bool simulate) => RequestCWUt(cwut, simulate, NewSeen());

	public int GetMaxCWUt(ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		if (!IsFormed) return 0;
		// Client: handler ungathered -> read synced mirror.
		if ((_computationHandler?.ProviderCount ?? 0) == 0) return _syncMaxComputation;
		return _computationHandler?.GetMaxCWUt(seen) ?? 0;
	}

	public int GetMaxCWUt() => GetMaxCWUt(NewSeen());

	// Allows chaining Network Switches.
	public bool CanBridge(ICollection<IOpticalComputationProvider> seen)
	{
		seen.Add(this);
		return true;
	}

	public bool CanBridge() => CanBridge(NewSeen());

	private static ICollection<IOpticalComputationProvider> NewSeen() => new HashSet<IOpticalComputationProvider>();

	// Verbatim port of MultipleComputationHandler.
	public sealed class MultipleComputationHandler : NotifiableComputationContainer
	{
		private readonly NetworkSwitchMachine _owner;
		private readonly HashSet<IOpticalComputationHatch> _providers    = new();
		private readonly HashSet<IOpticalComputationHatch> _transmitters = new();
		public int EUt { get; private set; }

		// >0 only server-side (gathered in OnStructureForm).
		public int ProviderCount => _providers.Count;

		private bool _tickSaturated;
		private long _timerCWUt = -1;

		public MultipleComputationHandler(NetworkSwitchMachine owner) : base(IO.IN, false)
		{
			_owner = owner;
		}

		public void OnStructureForm(ICollection<IOpticalComputationHatch> providers,
			ICollection<IOpticalComputationHatch> transmitters)
		{
			Reset();
			foreach (var p in providers)    _providers.Add(p);
			foreach (var t in transmitters) _transmitters.Add(t);
			EUt = (providers.Count + transmitters.Count) * (int)NS_EUT_PER_HATCH;
		}

		public void Reset()
		{
			_providers.Clear();
			_transmitters.Clear();
			EUt = 0;
		}

		public override int RequestCWUt(int cwut, bool simulate, ICollection<IOpticalComputationProvider> seen)
		{
			if (seen.Contains(this)) return 0;
			seen.Add(this);
			if (cwut == 0) return 0;

			// Saturation cache per tick.
			long timer = _owner.GetOffsetTimer();
			if (_timerCWUt == timer)
			{
				if (_tickSaturated) return 0;
			}
			else
			{
				_timerCWUt = timer;
				_tickSaturated = false;
			}

			var bridgeSeen = new List<IOpticalComputationProvider>(seen);
			int allocatedCWUt = 0;
			foreach (var provider in _providers)
			{
				if (!provider.CanBridge(bridgeSeen)) continue;
				int allocated = provider.RequestCWUt(cwut, simulate, seen);
				allocatedCWUt += allocated;
				cwut -= allocated;
				if (cwut == 0) break;
			}

			if (!simulate && allocatedCWUt == 0)
				_tickSaturated = true;

			return allocatedCWUt;
		}

		public override int GetMaxCWUt(ICollection<IOpticalComputationProvider> seen)
		{
			if (seen.Contains(this)) return 0;
			seen.Add(this);
			var bridgeSeen = new List<IOpticalComputationProvider>(seen);
			int maximumCWUt = 0;
			foreach (var provider in _providers)
			{
				if (!provider.CanBridge(bridgeSeen)) continue;
				maximumCWUt += provider.GetMaxCWUt(seen);
			}
			return maximumCWUt;
		}

		public int GetMaxCWUtForDisplay()
		{
			var seen = new List<IOpticalComputationProvider> { this };
			var bridgeSeen = new List<IOpticalComputationProvider>(seen);
			int maximumCWUt = 0;
			foreach (var provider in _providers)
			{
				if (!provider.CanBridge(bridgeSeen)) continue;
				maximumCWUt += provider.GetMaxCWUt(seen);
			}
			return maximumCWUt;
		}

		public override bool CanBridge(ICollection<IOpticalComputationProvider> seen)
		{
			if (seen.Contains(this)) return false;
			seen.Add(this);
			foreach (var provider in _providers)
				if (provider.CanBridge(seen)) return true;
			return false;
		}
	}
}
