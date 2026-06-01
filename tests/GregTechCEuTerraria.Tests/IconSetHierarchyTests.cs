using System.Linq;
using GregTechCEuTerraria.Common.Materials;
using Xunit;

namespace GregTechCEuTerraria.Tests;

public class IconSetHierarchyTests
{
	[Fact]
	public void RubyResolvesEntireChainUpThroughMetallic()
	{
		var chain = IconSetHierarchy.WalkChain("RUBY").ToList();
		// RUBY -> EMERALD -> DIAMOND -> SHINY -> METALLIC. DULL is appended as the
		// ultimate fallback.
		Assert.Equal(new[] { "RUBY", "EMERALD", "DIAMOND", "SHINY", "METALLIC", "DULL" }, chain);
	}

	[Fact]
	public void NullIconSetDefaultsToDull()
	{
		// Upstream MaterialInfo defaults an unset iconSet to DULL.
		var chain = IconSetHierarchy.WalkChain(null).ToList();
		Assert.Equal("DULL", chain[0]);
	}

	[Fact]
	public void UnknownIconSetFallsThroughToDull()
	{
		var chain = IconSetHierarchy.WalkChain("MADE_UP_SET").ToList();
		Assert.Equal(new[] { "MADE_UP_SET", "DULL" }, chain);
	}

	[Fact]
	public void RadioactiveRoutesThroughMetallic()
	{
		var chain = IconSetHierarchy.WalkChain("RADIOACTIVE").ToList();
		Assert.Equal(new[] { "RADIOACTIVE", "METALLIC", "DULL" }, chain);
	}

	[Fact]
	public void NoCyclesEvenIfParentMapHasOne()
	{
		// WalkChain dedupes via HashSet, so even a stray cycle wouldn't hang
		// the loader. Verify by exhausting the chain finishes.
		var chain = IconSetHierarchy.WalkChain("OPAL").ToList();
		Assert.Equal(chain.Count, chain.Distinct().Count());
	}
}
