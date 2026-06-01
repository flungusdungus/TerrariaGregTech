#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Cover;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover;

// Port of common.cover.ShutterCover - gate cover; while shut, exposes no
// handler and pipes can't pass through. Toggle via the IControllable field 0
// in the settings popup (upstream's soft-mallet click dropped).
public class ShutterCover : CoverBehavior, IUICover, IControllable
{
	private bool _workingEnabled = true;

	public ShutterCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public bool IsWorkingEnabled() => _workingEnabled;
	public void SetWorkingEnabled(bool isWorkingAllowed) => _workingEnabled = isWorkingAllowed;

	// Shut -> pipes cannot pass through this side.
	public override bool CanPipePassThrough() => !_workingEnabled;

	// Shut -> no handler; open -> passthrough (base returns defaultValue).
	public override IItemHandler? GetItemHandlerCap(IItemHandler? defaultValue) =>
		_workingEnabled ? null : base.GetItemHandlerCap(defaultValue);

	public override IFluidHandler? GetFluidHandlerCap(IFluidHandler? defaultValue) =>
		_workingEnabled ? null : base.GetFluidHandlerCap(defaultValue);

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["shut"] = _workingEnabled;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("shut")) _workingEnabled = tag.GetBool("shut");
	}
}
