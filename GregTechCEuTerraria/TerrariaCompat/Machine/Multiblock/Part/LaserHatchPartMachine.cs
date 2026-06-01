#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Machine.Trait;
using GregTechCEuTerraria.Common.Energy;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Multiblock.Part;

// Port of LaserHatchPartMachine. Tier + amperage laser energy hatch -
// EnergyHatchPartMachine shape with a NotifiableLaserContainer. Capacity
// V[tier] * 64 * amperage (both emitter/receiver, vs EnergyHatch's 16x/64x).
// Buffer rejects on wrong side internally.
public class LaserHatchPartMachine : TieredIOPartMachine, ILaserContainer
{
	protected override string Label => "Laser Hatch";

	public NotifiableLaserContainer? Buffer { get; protected set; }
	public int Amperage { get; protected set; }

	long IEnergyContainer.AcceptEnergyFromNetwork(IODirection side, long voltage, long amperage)
		=> Buffer?.AcceptEnergyFromNetwork(side, voltage, amperage) ?? 0L;

	bool IEnergyContainer.InputsEnergy(IODirection side)
		=> Buffer?.InputsEnergy(side) ?? false;

	bool IEnergyContainer.OutputsEnergy(IODirection side)
		=> Buffer?.OutputsEnergy(side) ?? false;

	long IEnergyContainer.ChangeEnergy(long differenceAmount)
		=> Buffer?.ChangeEnergy(differenceAmount) ?? 0L;

	long IEnergyContainer.EnergyStored   => Buffer?.EnergyStored   ?? 0L;
	long IEnergyContainer.EnergyCapacity => Buffer?.EnergyCapacity ?? 0L;
	long IEnergyContainer.InputVoltage   => Buffer?.InputVoltage   ?? 0L;
	long IEnergyContainer.InputAmperage  => Buffer?.InputAmperage  ?? 0L;

	public LaserHatchPartMachine() : base() { }

	protected override void OnDefinitionBound()
	{
		base.OnDefinitionBound();
		var def = Definition;
		if (def?.PartIo == null || def.PartAmperage == 0) return;
		Configure(def.PartIo.Value, (int)((MetaMachine)this).Tier, def.PartAmperage);
	}

	// Verbatim port of NotifiableLaserContainer.serverTick, replicated at the
	// part level because the trait's TYPE is allowMultipleInstances and the
	// base NEC's TYPE-keyed walk would miss laser hatches.
	protected override void OnTick()
	{
		base.OnTick();
		if (!IsServer) return;
		if (Io != IO.OUT) return;
		var buf = Buffer;
		if (buf == null) return;
		if (buf.EnergyStored < buf.OutputVoltage || buf.OutputVoltage <= 0 || buf.OutputAmperage <= 0) return;

		long outputVoltage = buf.OutputVoltage;
		long outputAmperes = System.Math.Min(buf.EnergyStored / outputVoltage, buf.OutputAmperage);
		if (outputAmperes == 0) return;

		long amperesUsed = 0;
		// PerimeterCells: whole-footprint walk; 4-cardinal from top-left would
		// self-match a 2x2 hatch's interior cells.
		foreach (var (side, nx, ny) in PerimeterCells(this))
		{
			if (!buf.OutputsEnergy(side)) continue;
			var oppositeSide = side.Opposite();

			// (a) adjacent laser pipe -> LaserNetHandler walks the net;
			// (b) direct adjacent ILaserContainer -> push without hop.
			ILaserContainer? dest;
			if (TerrariaCompat.Pipelike.Laser.LaserPipeLayerSystem.Pipes.Has(nx, ny))
			{
				var net = TerrariaCompat.Pipelike.Laser.LaserPipeNetSystem.Level.GetNetFromPos((nx, ny));
				dest = net is null ? null
				    : new TerrariaCompat.Pipelike.Laser.LaserNetHandler(net, (nx, ny), oppositeSide);
			}
			else
			{
				dest = TerrariaCompat.Capabilities.WorldCapability.Get<ILaserContainer>(nx, ny);
			}
			if (dest == null) continue;
			if (!dest.InputsEnergy(oppositeSide)) continue;

			amperesUsed += dest.AcceptEnergyFromNetwork(oppositeSide, outputVoltage, outputAmperes - amperesUsed);
			if (amperesUsed >= outputAmperes) break;
		}

		if (amperesUsed > 0)
			buf.SetEnergyStored(buf.EnergyStored - amperesUsed * outputVoltage);
	}

	public void Configure(IO io, int tier, int amperage)
	{
		Tier     = tier;
		Io       = io;
		Amperage = amperage;
		EnsureBuffer();
	}

	private void EnsureBuffer()
	{
		if (Buffer != null) return;
		long voltage  = VoltageTiers.V(Tier);
		long capacity = voltage * 64L * Amperage;
		Buffer = Io == IO.OUT
			? NotifiableLaserContainer.EmitterContainer (capacity, voltage, Amperage)
			: NotifiableLaserContainer.ReceiverContainer(capacity, voltage, Amperage);
		// No side gate - EnergyHatchPartMachine convention (sideless wires).
		Traits.Attach(Buffer);
		Traits.RegisterPersistent("Buffer", Buffer);
	}

	public bool CanShared() => false;

	// Diagnostic tooltip - controller binding + buffer fill + per-side
	// pipe/endpoint resolution.
	public override void AppendTooltip(System.Collections.Generic.List<string> lines)
	{
		base.AppendTooltip(lines);
		var buf = Buffer;
		if (buf == null)
		{
			lines.Add("[c/FFAA44:Buffer not initialised]");
			return;
		}

		string role = Io == IO.OUT ? "[c/55AAFF:Source]" : "[c/FFAA55:Target]";
		string tierName = Common.Energy.VoltageTiers.ShortName(((MetaMachine)this).Tier);
		lines.Add($"{role} * {Amperage:N0}A * {tierName} ({Common.Energy.VoltageTiers.V(Tier):N0} EU/t)");
		lines.Add($"Buffer: {buf.EnergyStored:N0} / {buf.EnergyCapacity:N0} EU");

		var controllers = GetControllers();
		if (controllers.Count == 0)
			lines.Add("[c/FFAA44:Unbound (no multi formed nearby)]");
		else
			lines.Add($"[c/55FF55:Bound to {controllers.Count} controller{(controllers.Count == 1 ? "" : "s")}]");

		// Same 4-side walk OnTick does (push targets for Source / inputs for Target).
		AppendAdjacencyLines(lines);
	}

	private void AppendAdjacencyLines(System.Collections.Generic.List<string> lines)
	{
		int hits = 0;
		foreach (var (side, nx, ny) in PerimeterCells(this))
		{
			if (TerrariaCompat.Pipelike.Laser.LaserPipeLayerSystem.Pipes.Has(nx, ny))
			{
				var net = TerrariaCompat.Pipelike.Laser.LaserPipeNetSystem.Level.GetNetFromPos((nx, ny));
				if (net is null)
				{
					lines.Add($"  {SideName(side)}: [c/FFAA44:pipe (no net)]");
				}
				else
				{
					// Walk from the pipe-side that faces back at this hatch.
					var route = net.GetNetData((nx, ny), side.Opposite());
					if (route is null)
						lines.Add($"  {SideName(side)}: [c/FFAA44:pipe -> no endpoint]");
					else
					{
						var dest = route.GetHandler();
						if (dest is null)
							lines.Add($"  {SideName(side)}: [c/FFAA44:pipe -> endpoint resolved, but handler null]");
						else
							lines.Add($"  {SideName(side)}: [c/55FF55:pipe -> endpoint] ({dest.EnergyStored:N0}/{dest.EnergyCapacity:N0} EU)");
					}
				}
				hits++;
				continue;
			}

			var direct = TerrariaCompat.Capabilities.WorldCapability.Get<ILaserContainer>(nx, ny);
			if (direct != null)
			{
				lines.Add($"  {SideName(side)}: [c/55FF55:direct laser hatch] ({direct.EnergyStored:N0}/{direct.EnergyCapacity:N0} EU)");
				hits++;
			}
		}
		if (hits == 0)
			lines.Add("[c/FFAA44:No laser pipe / hatch on any side]");
	}

	private static string SideName(IODirection side) => side switch
	{
		IODirection.Up    => "Up   ",
		IODirection.Down  => "Down ",
		IODirection.Left  => "Left ",
		IODirection.Right => "Right",
		_                  => "?    ",
	};

	// Raw-cell variant of MachineCellResolver.PerimeterNeighbors - yields
	// (side, x, y) since laser pipes aren't MetaMachines. Dedupes neighbor cells.
	private static System.Collections.Generic.IEnumerable<(IODirection side, int x, int y)>
		PerimeterCells(MetaMachine machine)
	{
		var own  = new System.Collections.Generic.HashSet<(int, int)>(machine.Cells());
		var seen = new System.Collections.Generic.HashSet<(int, int)>();
		foreach (var (cx, cy) in machine.Cells())
		foreach (var (side, dx, dy) in MachineSides.Cardinal4)
		{
			int nx = cx + dx, ny = cy + dy;
			if (own.Contains((nx, ny))) continue;
			if (!seen.Add((nx, ny))) continue;
			yield return (side, nx, ny);
		}
	}

	public override void SaveData(TagCompound tag)
	{
		base.SaveData(tag);
		tag["amperage"] = Amperage;
	}

	public override void LoadData(TagCompound tag)
	{
		base.LoadData(tag);
		Amperage = tag.GetInt("amperage");
		EnsureBuffer();
		Traits.Load(tag);   // re-load after late registration; ItemBus pattern.
	}
}
