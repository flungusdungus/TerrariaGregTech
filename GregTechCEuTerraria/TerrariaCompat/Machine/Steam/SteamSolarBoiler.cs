#nullable enable
using GregTechCEuTerraria.Api.Recipe;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.Machine.Steam;

// 1:1 port of SteamSolarBoiler. No fuel - heats when sunlit (still needs
// water). Upstream's RecipeLogic-status-driven temperature toggle collapses
// to overriding IsHeating() with the sunlight check; RecipeLogic stays dormant.
// canSeeSunClearly: daytime + not raining + no solid block in the column
// directly above. Bounded + cached ~1x/sec (no cheap Terraria sky cache).
public class SteamSolarBoiler : SteamBoilerMachine
{
	public SteamSolarBoiler() : base() { }

	protected override string Label => Definition?.Label ?? "Solar Boiler";
	public override GTRecipeType GetRecipeType() => Definition?.RecipeType!;

	// No STEAM_BOILER recipes - recipe browser skipped entirely.
	public override bool ShowsInRecipeBrowser(GTRecipe recipe) => false;

	// Upstream ConfigHolder solarBoilerBaseOutput.
	protected override long GetBaseSteamOutput() => IsHighPressure ? 360 : 120;

	protected override int GetCooldownInterval() => IsHighPressure ? 50 : 45;
	protected override int GetCoolDownRate()     => 3;

	protected override bool IsHeating() => IsSunlit;

	// RecipeLogic stays idle here, so drive IsActive off temperature.
	public override bool IsActive => CurrentTemperature > 0;

	private bool _sunCache;
	private uint _sunCacheTick;
	private bool _sunCacheValid;

	private const int SkyScanCap = 256;

	public bool IsSunlit
	{
		get
		{
			uint now = (uint)Main.GameUpdateCount;
			if (!_sunCacheValid || now - _sunCacheTick >= 20)
			{
				_sunCacheTick  = now;
				_sunCacheValid = true;
				_sunCache      = ComputeSunlit();
			}
			return _sunCache;
		}
	}

	private bool ComputeSunlit()
	{
		if (!Main.dayTime || Main.raining) return false;
		var (w, _) = Size;
		for (int dx = 0; dx < w; dx++)
		{
			int x = Position.X + dx;
			if (x < 0 || x >= Main.maxTilesX) continue;
			int yStop = System.Math.Max(0, Position.Y - SkyScanCap);
			for (int y = Position.Y - 1; y >= yStop; y--)
			{
				var tile = Main.tile[x, y];
				if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
					return false;
			}
		}
		return true;
	}
}
