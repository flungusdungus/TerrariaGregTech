#nullable enable
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Profiler;

// Watches the managed heap and shouts (chat + error log) when it crosses a
// threshold, so a runaway / leak is obvious BEFORE it OOM-crashes the game.
//
// Context: the in-process tModLoader "Build + Reload" cycle leaks ~1 GB per
// reload (old assemblies / textures / statics are never freed), so a long dev
// session can climb to 18 GB and hard-crash the client with no logged
// exception. This guard surfaces that early. It is NOT a leak fix - it's an
// alarm. The real remedy for the reload leak is a full tML restart.
//
// Throttled: warns once per `StepMb` band above the threshold (so 8 -> 10 -> 12
// GB each warn once, not every tick), and re-arms after the heap falls back
// below the threshold (e.g. after a GC or world reload).
public static class MemoryGuard
{
	// First warning level + how much extra heap triggers each subsequent warn.
	public const long ThresholdMb = 8192;   // 8 GB
	public const long StepMb      = 2048;   // re-warn every +2 GB

	private static long _nextWarnMb = ThresholdMb;

	public static void Check(long heapMb)
	{
		// Re-arm once the heap drops back under the threshold (GC / reload freed it).
		if (heapMb < ThresholdMb)
		{
			_nextWarnMb = ThresholdMb;
			return;
		}
		if (heapMb < _nextWarnMb) return;

		string msg = $"[GregTech] Managed heap is high: {heapMb} MB ({heapMb / 1024.0:0.0} GB). " +
			"If this keeps climbing it will OOM-crash the game. During dev sessions this is usually " +
			"tModLoader's reload leak (~1 GB per Build+Reload) - fully restart tML to reclaim it.";

		try { ModContent.GetInstance<GregTechCEuTerraria>()?.Logger?.Error(msg); } catch { }

		// Surface in chat where a player/dev will actually see it.
		if (!Main.dedServ)
		{
			Main.NewText(msg, 255, 80, 80);
		}
		else
		{
			try
			{
				Terraria.Chat.ChatHelper.BroadcastChatMessage(
					Terraria.Localization.NetworkText.FromLiteral(msg),
					new Microsoft.Xna.Framework.Color(255, 80, 80));
			}
			catch { }
		}

		// Advance to the next band strictly above the current heap so we don't
		// re-warn until it climbs another StepMb.
		_nextWarnMb = ((heapMb / StepMb) + 1) * StepMb;
	}
}
