#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GregTechCEuTerraria.Config;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.Common;

// Boss-fight diagnostics. Maintains a single "current fight" snapshot whenever
// any NPC implementing IDebuggableBoss is alive. Surfaces seven datasets:
//
//   1. Damage TAKEN, bucketed by source projectile (histogram + top-N)
//   2. Damage DEALT to all live bosses (running total -> DPS)
//   3. Rolling list of last N hits taken (live ticker)
//   4. Full ordered timeline (attack-state transitions + hits + phase + death)
//   5. State-duration histogram (each attack: pick count + total time)
//   6. Per-second damage time series (taken + dealt, one entry per game-sec)
//   7. Peak concurrent projectile counts by type + total spawns by type
//   8. Player position polyline (sampled every PositionSamplePeriod ticks)
//
// Tracker polls each PostUpdateNPCs to detect attack picks + sample position;
// projectile counters fed by BossFightSpawnGlobal's OnSpawn hook + a per-tick
// scan. On fight end, writes the whole snapshot to disk and resets.
//
// All data is local-client only. Gated by GTConfig.DebugMobs so cost is zero
// when off.
public class BossFightTracker : ModSystem
{
	// ---- public state (read by DebugOverlaySystem panel) -------------------

	public static bool FightActive => _fightStartTick >= 0;
	public static int FightDurationTicks => FightActive ? (int)(Main.GameUpdateCount - _fightStartTick) : 0;
	public static float FightDurationSec => FightDurationTicks / 60f;
	public static long DamageDealtToBosses { get; private set; }
	public static float DpsToBosses => FightDurationSec > 0.1f ? DamageDealtToBosses / FightDurationSec : 0f;
	public static long DamageTakenTotal { get; private set; }
	public static float DpsTaken => FightDurationSec > 0.1f ? DamageTakenTotal / FightDurationSec : 0f;
	public static IReadOnlyDictionary<string, (int Hits, int Damage)> DamageTaken => _damageTaken;
	public static IReadOnlyList<HitEvent> RecentHits => _recentHits;

	public struct HitEvent { public int Tick; public string Source; public int Damage; }
	private struct TimelineEvent { public int Tick; public string Kind; public string Detail; }
	private struct StateRow { public int PickCount; public int TotalTicks; }
	private struct ProjStat { public int Spawned; public int PeakConcurrent; }

	// ---- tunables ---------------------------------------------------------

	private const int RecentHitsCap = 8;
	private const int PositionSamplePeriod = 10;     // ticks between player.Center samples (~6 Hz)
	private const int PositionTraceCap = 1200;       // ~3 min @ 6 Hz

	// ---- internals ---------------------------------------------------------

	private static int _fightStartTick = -1;
	private static int _previousBossState = int.MinValue;
	private static int _previousBossPhase = 0;
	private static int _previousBossWhoAmI = -1;
	private static string _previousBossDisplayName = "";
	private static int _previousStateStartTick = 0;
	private static string _previousStateLabel = "?";

	private static readonly Dictionary<string, (int Hits, int Damage)> _damageTaken = new();
	private static readonly List<HitEvent> _recentHits = new();
	private static readonly List<TimelineEvent> _timeline = new();
	private static readonly Dictionary<string, StateRow> _stateDurations = new();
	private static readonly Dictionary<string, ProjStat> _projStats = new();
	private static readonly List<(float taken, float dealt)> _dpsTimeSeries = new();
	private static readonly List<Vector2> _positionTrace = new();

	// Per-second accumulators (flushed into the time series each game-second).
	private static long _secAccumTaken;
	private static long _secAccumDealt;
	private static int _lastSecondTick;

	public override void PostUpdateNPCs()
	{
		if (!GTConfig.Instance.DebugMobs)
		{
			if (FightActive) Reset();
			return;
		}

		int boss = FindLeadDebuggableBoss();
		if (boss >= 0)
		{
			if (!FightActive) StartFight(boss);
			DetectStateChange(boss);
			DetectPhaseChange(boss);
			ScanLiveProjectiles();
			SamplePlayerPosition();
			AccumulatePerSecond();
		}
		else if (FightActive)
		{
			// Close out the current state's duration before exporting.
			AccumulateCurrentStateDuration();
			bool playerWon = !Main.LocalPlayer.dead;
			EndFight(playerWon ? "victory" : "death");
		}
	}

	private static int FindLeadDebuggableBoss()
	{
		// Prefer the previously-tracked boss if still alive.
		if (_previousBossWhoAmI >= 0 && _previousBossWhoAmI < Main.maxNPCs)
		{
			var prev = Main.npc[_previousBossWhoAmI];
			if (prev.active && prev.ModNPC is IDebuggableBoss) return _previousBossWhoAmI;
		}
		for (int i = 0; i < Main.maxNPCs; i++)
			if (Main.npc[i].active && Main.npc[i].ModNPC is IDebuggableBoss)
				return i;
		return -1;
	}

	private static void StartFight(int boss)
	{
		_fightStartTick = (int)Main.GameUpdateCount;
		_previousBossWhoAmI = boss;
		_previousBossState = int.MinValue;
		_previousBossPhase = 0;
		_previousBossDisplayName = Main.npc[boss].FullName;
		_previousStateStartTick = 0;
		_previousStateLabel = "?";
		DamageDealtToBosses = 0;
		DamageTakenTotal = 0;
		_damageTaken.Clear();
		_recentHits.Clear();
		_timeline.Clear();
		_stateDurations.Clear();
		_projStats.Clear();
		_dpsTimeSeries.Clear();
		_positionTrace.Clear();
		_secAccumTaken = 0;
		_secAccumDealt = 0;
		_lastSecondTick = 0;
		_timeline.Add(new TimelineEvent { Tick = 0, Kind = "start", Detail = _previousBossDisplayName });
	}

	// (1) state-duration histogram + timeline entry
	private static void DetectStateChange(int boss)
	{
		NPC npc = Main.npc[boss];
		int cur = (int)npc.ai[0];
		if (cur == _previousBossState) return;

		AccumulateCurrentStateDuration();

		_previousBossState = cur;
		string label = (npc.ModNPC as IDebuggableBoss)?.CurrentAttackLabel() ?? $"State{cur}";
		_previousStateLabel = label;
		_previousStateStartTick = FightDurationTicks;

		_timeline.Add(new TimelineEvent { Tick = FightDurationTicks, Kind = "attack", Detail = label });

		if (_stateDurations.TryGetValue(label, out var sr))
			_stateDurations[label] = new StateRow { PickCount = sr.PickCount + 1, TotalTicks = sr.TotalTicks };
		else
			_stateDurations[label] = new StateRow { PickCount = 1, TotalTicks = 0 };
	}

	private static void AccumulateCurrentStateDuration()
	{
		if (_previousStateLabel == "?" || _previousBossState == int.MinValue) return;
		int dur = FightDurationTicks - _previousStateStartTick;
		if (dur <= 0) return;
		if (_stateDurations.TryGetValue(_previousStateLabel, out var sr))
			_stateDurations[_previousStateLabel] = new StateRow { PickCount = sr.PickCount, TotalTicks = sr.TotalTicks + dur };
	}

	// (5) phase transition annotation
	private static void DetectPhaseChange(int boss)
	{
		NPC npc = Main.npc[boss];
		int curPhase = (npc.ModNPC as IDebuggableBoss)?.CurrentPhase() ?? 0;
		if (curPhase == _previousBossPhase) return;
		float playerHpPct = Main.LocalPlayer.statLifeMax2 > 0
			? 100f * Main.LocalPlayer.statLife / Main.LocalPlayer.statLifeMax2
			: 0f;
		_timeline.Add(new TimelineEvent
		{
			Tick = FightDurationTicks,
			Kind = $"phase{curPhase}",
			Detail = $"enter, player at {playerHpPct:0.0}% HP",
		});
		_previousBossPhase = curPhase;
	}

	// (4) peak concurrent projectile counts
	private static void ScanLiveProjectiles()
	{
		var perType = new Dictionary<string, int>();
		for (int i = 0; i < Main.maxProjectiles; i++)
		{
			Projectile p = Main.projectile[i];
			if (!p.active || !p.hostile || p.friendly) continue;
			string name = p.ModProjectile?.Name ?? $"vanilla:{p.type}";
			perType.TryGetValue(name, out int n);
			perType[name] = n + 1;
		}
		foreach (var kv in perType)
		{
			if (_projStats.TryGetValue(kv.Key, out var st))
			{
				if (kv.Value > st.PeakConcurrent)
					_projStats[kv.Key] = new ProjStat { Spawned = st.Spawned, PeakConcurrent = kv.Value };
			}
			else
			{
				_projStats[kv.Key] = new ProjStat { Spawned = 0, PeakConcurrent = kv.Value };
			}
		}
	}

	// (8) player position polyline
	private static void SamplePlayerPosition()
	{
		if (FightDurationTicks % PositionSamplePeriod != 0) return;
		if (_positionTrace.Count >= PositionTraceCap) return;
		_positionTrace.Add(Main.LocalPlayer.Center);
	}

	// (2) per-second damage time series - flush accumulators every 60 ticks
	private static void AccumulatePerSecond()
	{
		int sec = FightDurationTicks / 60;
		if (sec > _lastSecondTick)
		{
			_dpsTimeSeries.Add(((float)_secAccumTaken, (float)_secAccumDealt));
			_secAccumTaken = 0;
			_secAccumDealt = 0;
			_lastSecondTick = sec;
		}
	}

	// (6) spawn counter, called by BossFightSpawnGlobal.OnSpawn
	internal static void RecordSpawn(string name)
	{
		if (!FightActive) return;
		if (_projStats.TryGetValue(name, out var st))
			_projStats[name] = new ProjStat { Spawned = st.Spawned + 1, PeakConcurrent = st.PeakConcurrent };
		else
			_projStats[name] = new ProjStat { Spawned = 1, PeakConcurrent = 1 };
	}

	private static void EndFight(string outcome)
	{
		// (3) killing-blow attribution - on death, append the most recent hit's
		// source as an explicit "[death]" timeline entry.
		if (outcome == "death" && _recentHits.Count > 0)
		{
			var lastHit = _recentHits[_recentHits.Count - 1];
			_timeline.Add(new TimelineEvent
			{
				Tick = FightDurationTicks,
				Kind = "death",
				Detail = $"{lastHit.Source} dealt {lastHit.Damage} (most recent hit, {(FightDurationTicks - lastHit.Tick) / 60f:0.0}s before death)",
			});
		}
		_timeline.Add(new TimelineEvent { Tick = FightDurationTicks, Kind = "end", Detail = outcome });
		TryExportLog(outcome);
		Reset();
	}

	private static void Reset()
	{
		_fightStartTick = -1;
		_previousBossState = int.MinValue;
		_previousBossWhoAmI = -1;
		_previousBossPhase = 0;
		_previousStateStartTick = 0;
		_previousStateLabel = "?";
	}

	// ---- recorders (called by GlobalPlayer / GlobalNPC) --------------------

	public static void RecordPlayerHit(string source, int damage)
	{
		if (!FightActive || damage <= 0) return;
		DamageTakenTotal += damage;
		_secAccumTaken += damage;
		if (_damageTaken.TryGetValue(source, out var v))
			_damageTaken[source] = (v.Hits + 1, v.Damage + damage);
		else
			_damageTaken[source] = (1, damage);

		_recentHits.Add(new HitEvent { Tick = FightDurationTicks, Source = source, Damage = damage });
		while (_recentHits.Count > RecentHitsCap) _recentHits.RemoveAt(0);

		_timeline.Add(new TimelineEvent
		{
			Tick = FightDurationTicks,
			Kind = "hit-taken",
			Detail = $"{source} -> {damage}",
		});
	}

	public static void RecordBossHit(int damage)
	{
		if (!FightActive || damage <= 0) return;
		DamageDealtToBosses += damage;
		_secAccumDealt += damage;
	}

	// ---- accessors for the panel -----------------------------------------

	public static IEnumerable<(string Source, int Hits, int Damage)> TopDamageSources(int n = 5)
	{
		var list = new List<(string, int, int)>(_damageTaken.Count);
		foreach (var kv in _damageTaken)
			list.Add((kv.Key, kv.Value.Hits, kv.Value.Damage));
		list.Sort((a, b) => b.Item3.CompareTo(a.Item3));
		int take = Math.Min(n, list.Count);
		for (int i = 0; i < take; i++) yield return list[i];
	}

	// ---- export ----------------------------------------------------------

	private static void TryExportLog(string outcome)
	{
		if (Main.dedServ) return;

		try
		{
			string dir = Path.Combine(Main.SavePath, "GregTechCEuTerraria", "BossFights");
			Directory.CreateDirectory(dir);
			string safeName = string.Concat(_previousBossDisplayName.Split(Path.GetInvalidFileNameChars()));
			if (string.IsNullOrWhiteSpace(safeName)) safeName = "Boss";
			string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			string path = Path.Combine(dir, $"{stamp}_{safeName}_{outcome}.txt");

			var sb = new StringBuilder();
			sb.AppendLine($"Boss: {_previousBossDisplayName}");
			sb.AppendLine($"Outcome: {outcome}");
			sb.AppendLine($"Duration: {FightDurationSec:0.0}s ({FightDurationTicks} ticks)");
			sb.AppendLine($"Damage dealt to boss: {DamageDealtToBosses}  (avg DPS {DpsToBosses:0.0})");
			sb.AppendLine($"Damage taken: {DamageTakenTotal}  (avg DPS-taken {DpsTaken:0.0})");
			sb.AppendLine();

			// (1) State-duration histogram
			sb.AppendLine("=== State durations (pick count + total time + share) ===");
			var rows = new List<(string Label, int Picks, int Ticks)>();
			foreach (var kv in _stateDurations) rows.Add((kv.Key, kv.Value.PickCount, kv.Value.TotalTicks));
			rows.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
			foreach (var (label, picks, ticks) in rows)
			{
				float secs = ticks / 60f;
				float share = FightDurationTicks > 0 ? 100f * ticks / FightDurationTicks : 0f;
				float avgSec = picks > 0 ? secs / picks : 0f;
				sb.AppendLine($"  {label,-20} {picks,3} pick x {avgSec,5:0.0}s avg = {secs,6:0.0}s ({share,4:0.0}%)");
			}
			sb.AppendLine();

			// Damage histogram (carry forward from previous version).
			sb.AppendLine("=== Damage taken by source ===");
			foreach (var (src, hits, dmg) in TopDamageSources(30))
				sb.AppendLine($"  {src,-32}  {hits,4}x = {dmg,6} dmg  ({(float)dmg / hits,5:0.0} dmg/hit)");
			sb.AppendLine();

			// (7) Spawn counts + peak concurrent + damage-per-spawn cross-ref
			sb.AppendLine("=== Projectile spawn + density + efficiency ===");
			var statsList = new List<(string Name, ProjStat St)>();
			foreach (var kv in _projStats) statsList.Add((kv.Key, kv.Value));
			statsList.Sort((a, b) => b.St.Spawned.CompareTo(a.St.Spawned));
			foreach (var (name, st) in statsList)
			{
				int dmgFromThis = _damageTaken.TryGetValue(name, out var d) ? d.Damage : 0;
				float dmgPerSpawn = st.Spawned > 0 ? (float)dmgFromThis / st.Spawned : 0f;
				sb.AppendLine($"  {name,-32}  spawned {st.Spawned,4}  peak-live {st.PeakConcurrent,3}  dmg/spawn {dmgPerSpawn,5:0.00}");
			}
			sb.AppendLine();

			// (2) Per-second damage time series
			sb.AppendLine("=== DPS time series (sec : taken : dealt) ===");
			for (int s = 0; s < _dpsTimeSeries.Count; s++)
			{
				var (taken, dealt) = _dpsTimeSeries[s];
				sb.AppendLine($"  {s,4}s : {taken,5:0} dmg taken  {dealt,6:0} dmg dealt");
			}
			sb.AppendLine();

			// (4) Full ordered timeline
			sb.AppendLine("=== Timeline ===");
			foreach (var ev in _timeline)
				sb.AppendLine($"  {ev.Tick / 60f,6:0.0}s  [{ev.Kind,-9}]  {ev.Detail}");
			sb.AppendLine();

			// (8) Player position trace (CSV)
			sb.AppendLine("=== Player position trace (sec, x, y) ===");
			for (int i = 0; i < _positionTrace.Count; i++)
			{
				float t = i * PositionSamplePeriod / 60f;
				var pos = _positionTrace[i];
				sb.AppendLine($"  {t,6:0.0}, {pos.X,7:0}, {pos.Y,7:0}");
			}

			File.WriteAllText(path, sb.ToString());
			OwnMod.Logger.Info($"Boss fight log written: {path}");
			// Same convention as the profiler's "Dump JSON" button - violet to
			// match the boss-fight panel border, full path so it's copy-pastable.
			Main.NewText($"[GregTech] Boss fight log saved to {path}", 220, 140, 220);
		}
		catch (Exception ex)
		{
			OwnMod.Logger.Warn($"Failed to write boss fight log: {ex.Message}");
			Main.NewText($"[GregTech] Boss fight log FAILED: {ex.Message}", 255, 80, 80);
		}
	}

	private static Mod OwnMod => ModContent.GetInstance<GregTechCEuTerraria>();
}
