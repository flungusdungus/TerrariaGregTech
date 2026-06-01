#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.TerrariaCompat.Pipelike.LongDistance;

// Replacement for upstream LongDistanceNetwork's endpoint list + activeInput/
// activeOutput index machinery. Upstream stores the chosen pair ON the network
// instance; our nets are derived-from-cells (rebuilt each topology change) and
// our endpoints are machines (not net nodes), so we keep a flat endpoint set and
// resolve the active pair deterministically on demand.
//
// "Only the first two endpoints are used" (upstream) maps to: per net + type,
// the active input = the IO.IN endpoint with the lowest position key, the active
// output = the lowest-key IO.OUT endpoint; they link iff the min-length is met.
// Both endpoints compute the same pair, so the wormhole is symmetric.
public static class LongDistanceEndpointRegistry
{
	private static readonly HashSet<ILDEndpoint> _endpoints = new();

	public static void Register(ILDEndpoint e)
	{
		if (_endpoints.Add(e)) InvalidateAll();
	}

	public static void Unregister(ILDEndpoint e)
	{
		if (_endpoints.Remove(e)) InvalidateAll();
	}

	// Drop every endpoint's cached link so the next GetLink re-resolves. Called
	// on any registry change or net topology change.
	public static void InvalidateAll()
	{
		foreach (var e in _endpoints) e.InvalidateLink();
	}

	// Port of LongDistanceNetwork.getOtherEndpoint - the wormhole partner of
	// `self`, or null when no valid pairing exists in its net.
	public static ILDEndpoint? ResolveLink(ILDEndpoint self)
	{
		if (self.IoType is not (IO.IN or IO.OUT)) return null;
		var net = self.AttachedNet;
		if (net is null) return null;

		var (input, output) = ActivePair(net, self.PipeType);
		if (input is null || output is null) return null;
		if (!SatisfiesMinLength(input, output)) return null;

		if (ReferenceEquals(self, input)) return output;
		if (ReferenceEquals(self, output)) return input;
		return null;
	}

	private static (ILDEndpoint? input, ILDEndpoint? output) ActivePair(
		LongDistancePipeNet net, LongDistancePipeType type)
	{
		ILDEndpoint? input = null, output = null;
		foreach (var e in _endpoints)
		{
			if (e.IsRemoved || e.PipeType != type) continue;
			if (!ReferenceEquals(e.AttachedNet, net)) continue;
			if (e.IoType == IO.IN  && (input  is null || Key(e) < Key(input)))  input  = e;
			if (e.IoType == IO.OUT && (output is null || Key(e) < Key(output))) output = e;
		}
		return (input, output);
	}

	// Port of LongDistancePipeType.satisfiesMinLength - distance in tiles (not
	// pipe count) between the two endpoints must be >= the per-type minimum.
	private static bool SatisfiesMinLength(ILDEndpoint a, ILDEndpoint b)
	{
		if (ReferenceEquals(a, b)) return false;
		int min = a.PipeType.MinLength();
		long dx = a.EndpointPos.x - b.EndpointPos.x;
		long dy = a.EndpointPos.y - b.EndpointPos.y;
		return dx * dx + dy * dy >= (long)min * min;
	}

	private static long Key(ILDEndpoint e) =>
		((long)e.EndpointPos.x << 32) | (uint)e.EndpointPos.y;
}
