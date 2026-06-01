#nullable enable
using GregTechCEuTerraria.Api.Capability.Recipe;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Non-generic UI-read surface for the ender link covers. AbstractEnderLinkCover
// is generic on the virtual-entry type, so the cover settings popup can't name
// it directly - this interface gives the popup a uniform read across the item /
// fluid / redstone links. Mutation goes through CoverBehavior.ApplySetting /
// ApplySettingText.
public interface IEnderLinkCover
{
	// The 8-hex channel string; the channel is identifier + this.
	string ColorStr { get; }
	IO Io { get; }
	bool IsWorkingEnabled();

	// Channel identity - the full registry key (identifier + ColorStr) and the
	// entry kind. Used by the contents view + EnderChannelSyncPacket.
	string ChannelName { get; }
	EnderEntryType EntryType { get; }
}
