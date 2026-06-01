using GregTechCEuTerraria.TerrariaCompat.Worldgen;
using Xunit;

namespace GregTechCEuTerraria.Tests;

public class LayerDepthMappingTests
{
	// Approximate Terraria small-world dimensions (4200x1200).
	private static readonly WorldDimensions SmallWorld =
		new(SurfaceLow: 200, SurfaceHigh: 280, RockLayer: 320, UnderworldLayer: 1100, MaxY: 1200);

	[Fact]
	public void StoneStartsAtSurfaceLow()
	{
		// STONE starts at SurfaceLow (shallowest surface y - where vanilla tin/copper's
		// FIRST band begins) so GT tier-1 ores can appear at grass level.
		var (yMin, yMax) = LayerDepthMapping.For("STONE", SmallWorld);
		Assert.Equal(SmallWorld.SurfaceLow, yMin);
		Assert.True(yMax > SmallWorld.RockLayer);
		Assert.True(yMax < SmallWorld.UnderworldLayer);
	}

	[Fact]
	public void EndstoneSpawnsInUnderworld()
	{
		var (yMin, yMax) = LayerDepthMapping.For("ENDSTONE", SmallWorld);
		Assert.True(yMin >= SmallWorld.UnderworldLayer, "ENDSTONE band must start at or below underworld");
		Assert.True(yMax < SmallWorld.MaxY, "ENDSTONE band must not run off world bottom");
	}

	[Fact]
	public void NetherrackIsInUnderworld()
	{
		// MC NETHERRACK = Nether -> Terraria underworld (ash zone), NOT the cavern.
		var (nMin, nMax) = LayerDepthMapping.For("NETHERRACK", SmallWorld);
		Assert.Equal(SmallWorld.UnderworldLayer, nMin);
		Assert.True(nMax > SmallWorld.UnderworldLayer);
		Assert.True(nMax < SmallWorld.MaxY);
	}

	[Fact]
	public void DeepslateBetweenStoneAndUnderworld()
	{
		var (_, sMax) = LayerDepthMapping.For("STONE", SmallWorld);
		var (dMin, dMax) = LayerDepthMapping.For("DEEPSLATE", SmallWorld);
		Assert.Equal(sMax, dMin);
		Assert.Equal(SmallWorld.UnderworldLayer, dMax);
		Assert.True(dMin > SmallWorld.SurfaceLow, "DEEPSLATE must sit below upper-cavern start");
	}

	[Fact]
	public void BandsDoNotOverlap()
	{
		var stone     = LayerDepthMapping.For("STONE",      SmallWorld);
		var deepslate = LayerDepthMapping.For("DEEPSLATE",  SmallWorld);
		var nether    = LayerDepthMapping.For("NETHERRACK", SmallWorld);
		var end       = LayerDepthMapping.For("ENDSTONE",   SmallWorld);

		Assert.Equal(stone.yMax, deepslate.yMin);
		Assert.Equal(deepslate.yMax, nether.yMin);
		Assert.Equal(nether.yMax, end.yMin);
	}

	[Fact]
	public void UnknownLayerFallsBackToFullCavern()
	{
		var (yMin, yMax) = LayerDepthMapping.For("MADE_UP", SmallWorld);
		Assert.Equal(SmallWorld.SurfaceLow, yMin);
		Assert.Equal(SmallWorld.UnderworldLayer, yMax);
	}

	[Fact]
	public void DeepDeepslateVeinSitsBelowShallowDeepslateVein()
	{
		// diamond_vein (MC y -55..-30, near bedrock) must land DEEPER in Terraria
		// than lapis_vein (MC y -60..10, spans more of deepslate's reach).
		var deep = LayerDepthMapping.ForVein("DEEPSLATE", -55, -30, SmallWorld);
		var lapis = LayerDepthMapping.ForVein("DEEPSLATE", -60, 10, SmallWorld);
		Assert.True(deep.yMin >= lapis.yMin, "diamond should not start shallower than lapis");
		Assert.True(deep.yMax <= lapis.yMax, "diamond should not reach as shallow as lapis");
	}

	[Fact]
	public void NetherrackVeinWindowsAreOrdered()
	{
		// certus_quartz (MC y 80..120, top of nether) -> shallow underworld;
		// beryllium_vein (MC y 5..30, bottom of nether) -> deep underworld.
		var shallow = LayerDepthMapping.ForVein("NETHERRACK", 80, 120, SmallWorld);
		var deep    = LayerDepthMapping.ForVein("NETHERRACK",  5,  30, SmallWorld);
		Assert.True(shallow.yMax <= deep.yMin || shallow.yMin < deep.yMin,
			$"shallow window {shallow} should not be below deep window {deep}");
	}

	[Fact]
	public void EveryBandHasUsableHeightOnSmallWorld()
	{
		foreach (var layer in new[] { "STONE", "DEEPSLATE", "NETHERRACK", "ENDSTONE" })
		{
			var (yMin, yMax) = LayerDepthMapping.For(layer, SmallWorld);
			Assert.True(yMax - yMin >= 20, $"{layer} band collapsed to <20 tiles: {yMin}..{yMax}");
		}
	}
}
