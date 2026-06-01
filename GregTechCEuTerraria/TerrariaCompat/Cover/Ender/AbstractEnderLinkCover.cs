#nullable enable
using GregTechCEuTerraria.Api.Capability;
using GregTechCEuTerraria.Api.Capability.Recipe;
using GregTechCEuTerraria.Api.Cover;
using GregTechCEuTerraria.Api.Cover.Data;
using GregTechCEuTerraria.Api.Machine;
using GregTechCEuTerraria.TerrariaCompat.Capabilities;
using GregTechCEuTerraria.TerrariaCompat.Machine;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of common.cover.ender.AbstractEnderLinkCover - base for the 3 ender
// link covers. Moves resource between host and shared virtual channel every
// 5 ticks.
//
// Adaptations: PUBLIC/PRIVATE permission dropped (PRIVATE needs unported
// MachineOwner - every channel is public). VirtualEntryWidget dropped.
// ConditionalSubscriptionHandler -> plain server-tick subscription.
public abstract class AbstractEnderLinkCover<T> : CoverBehavior, IUICover, IControllable, IEnderLinkCover
	where T : VirtualEntry
{
	protected string _colorStr = VirtualEntry.DefaultColor;
	protected IO _io = IO.OUT;
	protected ManualIOMode _manualIOMode = ManualIOMode.Disabled;
	protected bool _isWorkingEnabled = true;

	private TickableSubscription? _subscription;

	protected AbstractEnderLinkCover(CoverDefinition definition, ICoverable coverHolder, CoverSide attachedSide)
		: base(definition, coverHolder, attachedSide) { }

	public override void OnLoad()
	{
		base.OnLoad();
		_subscription = CoverHolder.SubscribeServerTick(Update);
		if (!CoverHolder.IsRemote) SetVirtualEntry();
	}

	public override void OnRemoved()
	{
		base.OnRemoved();
		Unsubscribe();
		DeleteEntryIfRemovable();
	}

	public override void OnUnload()
	{
		base.OnUnload();
		Unsubscribe();
		DeleteEntryIfRemovable();
	}

	private void Unsubscribe()
	{
		_subscription?.Unsubscribe();
		_subscription = null;
	}

	private void DeleteEntryIfRemovable()
	{
		if (CoverHolder.IsRemote) return;
		VirtualEnderRegistry.Instance.DeleteEntryIf(GetEntryType(), ChannelName, e => e.CanRemove());
	}

	public bool IsWorkingEnabled() => _isWorkingEnabled;
	public void SetWorkingEnabled(bool isWorkingAllowed) => _isWorkingEnabled = isWorkingAllowed;

	public IO Io => _io;
	public string ColorStr => _colorStr;

	public void SetIo(IO io)
	{
		if (io is IO.IN or IO.OUT) _io = io;
	}

	// field 1 (long) = IO; field 0 (text) = channel name; field 0 (long) =
	// working-enabled (base).
	public override void ApplySetting(int field, long value)
	{
		if (field == 1) SetIo((IO)value);
		else base.ApplySetting(field, value);
	}

	public override void ApplySettingText(int field, string text)
	{
		if (field == 0) SetChannelName(text);
		else base.ApplySettingText(field, text);
	}

	protected abstract string Identifier();
	protected abstract T? GetEntry();
	protected abstract void SetEntry(VirtualEntry entry);
	protected abstract EnderEntryType GetEntryType();
	protected abstract void Transfer();

	// Verbatim getChannelName: identifier + colorStr. Covers sharing this
	// string share a VirtualEntry. Public for the contents view + sync.
	public string ChannelName => Identifier() + _colorStr;

	public EnderEntryType EntryType => GetEntryType();

	protected void SetVirtualEntry()
	{
		var entry = VirtualEnderRegistry.Instance.GetOrCreateEntry(GetEntryType(), ChannelName);
		entry.SetColor(_colorStr);
		SetEntry(entry);
	}

	// Verbatim setChannelName: drop old entry if orphaned, switch colorStr,
	// re-resolve onto new channel.
	public void SetChannelName(string name)
	{
		if (CoverHolder.IsRemote) return;
		name = SanitizeColor(name);
		if (name == _colorStr) return;
		VirtualEnderRegistry.Instance.DeleteEntryIf(GetEntryType(), ChannelName, e => e.CanRemove());
		_colorStr = name;
		SetVirtualEntry();
	}

	// Normalize to 8 upper-hex digits - untrusted-client safe.
	private static string SanitizeColor(string s)
	{
		var sb = new System.Text.StringBuilder(8);
		foreach (char c in (s ?? "").ToUpperInvariant())
		{
			if (sb.Length >= 8) break;
			if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')) sb.Append(c);
		}
		while (sb.Length < 8) sb.Append('F');
		return sb.ToString();
	}

	private void Update()
	{
		if (CoverHolder.GetOffsetTimer() % global::GregTechCEuTerraria.Api.TickScale.FromMcTicks(5) != 0) return;
		if (!_isWorkingEnabled || CoverHolder.IsRemote) return;

		// Re-resolve in case the entry was GC'd and recreated.
		var entry = VirtualEnderRegistry.Instance.GetOrCreateEntry(GetEntryType(), ChannelName);
		if (!ReferenceEquals(GetEntry(), entry)) SetEntry(entry);
		Transfer();
	}

	// Verbatim coverHolder.getXHandlerCap(side, false) - io-gated, NOT cover-wrapped.
	protected IItemHandler? GetOwnItemHandler() =>
		CoverHolder is MetaMachine m
			? m.GetItemHandlerCap(WorldCapability.ToIODirection(AttachedSide), useCoverCapability: false)
			: null;

	protected IFluidHandler? GetOwnFluidHandler() =>
		CoverHolder is MetaMachine m
			? m.GetFluidHandlerCap(WorldCapability.ToIODirection(AttachedSide), useCoverCapability: false)
			: null;

	// === Persistence ========================================================

	public override void Save(TagCompound tag)
	{
		base.Save(tag);
		tag["colorStr"] = _colorStr;
		tag["io"] = (int)_io;
		tag["manualIO"] = (int)_manualIOMode;
		tag["enderWorkingEnabled"] = _isWorkingEnabled;
	}

	public override void Load(TagCompound tag)
	{
		base.Load(tag);
		if (tag.ContainsKey("colorStr")) _colorStr = tag.GetString("colorStr");
		if (tag.ContainsKey("io")) _io = (IO)tag.GetInt("io");
		if (tag.ContainsKey("manualIO")) _manualIOMode = (ManualIOMode)tag.GetInt("manualIO");
		if (tag.ContainsKey("enderWorkingEnabled")) _isWorkingEnabled = tag.GetBool("enderWorkingEnabled");
	}
}
