#nullable enable
namespace GregTechCEuTerraria.Api.Cover.Data;

// Port of com.gregtechceu.gtceu.common.cover.data.BucketMode - fluid-amount
// display unit. Upstream carries the multiplier (Bucket = 1000 mB,
// MilliBucket = 1 mB); deferred until the cover GUI needs it.
public enum BucketMode
{
	Bucket,
	MilliBucket,
}
