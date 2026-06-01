#nullable enable
using System;
using System.Collections.Generic;

namespace GregTechCEuTerraria.Api.Fluids.Attribute;

// LOCKED - verbatim port of
// com.gregtechceu.gtceu.api.fluids.attribute.FluidAttribute.
// DO NOT modify behavior; mirror upstream changes only.
//
// Identity-comparable attribute tag attached to a FluidType. Concrete class
// (not an interface) - instances are constructed once and shared by reference.
// Equality is by Id (matches upstream's ResourceLocation-based equals).
//
// Tooltip callbacks mirror upstream's `Consumer<Consumer<Component>>` shape:
// AppendFluidTooltips writes to the fluid's hover text; AppendContainerTooltips
// writes to a container item's hover text when it carries this fluid.
//
// Documented adaptations:
//   - ResourceLocation -> string Id.
//   - Component -> string (Terraria has no Component system; tooltip lines are
//     plain strings rendered as-is).
//   - hashCode caching dropped - System.HashCode handles it.
public sealed class FluidAttribute : IEquatable<FluidAttribute>
{
	public string Id { get; }
	private readonly Action<Action<string>> _fluidTooltip;
	private readonly Action<Action<string>> _containerTooltip;

	public FluidAttribute(
		string id,
		Action<Action<string>> fluidTooltip,
		Action<Action<string>> containerTooltip)
	{
		Id = id;
		_fluidTooltip = fluidTooltip;
		_containerTooltip = containerTooltip;
	}

	public void AppendFluidTooltips(Action<string> tooltip)     => _fluidTooltip(tooltip);
	public void AppendContainerTooltips(Action<string> tooltip) => _containerTooltip(tooltip);

	public bool Equals(FluidAttribute? other) => other is not null && Id == other.Id;
	public override bool Equals(object? obj) => obj is FluidAttribute fa && Equals(fa);
	public override int GetHashCode() => Id.GetHashCode();
	public override string ToString() => $"FluidAttribute{{{Id}}}";
}
