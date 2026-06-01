#nullable enable
namespace GregTechCEuTerraria.Api.Cover;

// Marker for a cover that has a settings screen.
//
// Adaptation: upstream's IUICover extends LDLib's IUIHolder and builds a
// ModularUI. We drop that surface - a cover with settings just implements this
// marker; the machine GUI opens its settings screen on right-click of the
// cover slot. The settings screens themselves are a later UI phase.
public interface IUICover
{
}
