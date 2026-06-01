#nullable enable
using System.IO;
using GregTechCEuTerraria.Api.Cover;

namespace GregTechCEuTerraria.TerrariaCompat.Net.Actions;

// Change one setting of a cover attached to a machine side - the
// server-authoritative mutation behind the cover settings popup.
//
// The (field, value) pair is cover-defined and interpreted by the target
// cover's CoverBehavior.ApplySetting / ApplySettingText: field 0 is the
// universal working-enabled toggle for any IControllable cover; covers with
// more settings define higher field ids. The value is a long (carries a bool
// 0/1, an enum ordinal, or a raw int/long) - or a string, for text settings
// like the ender link channel name.
public sealed class CoverConfigAction : ICoverAction
{
	public PacketType Type => PacketType.CoverConfig;

	private CoverSide _side;
	private int _field;
	private long _value;
	private string _text = "";
	private bool _isText;

	public CoverConfigAction() { }

	public CoverConfigAction(CoverSide side, int field, long value)
	{
		_side = side;
		_field = field;
		_value = value;
	}

	private CoverConfigAction(CoverSide side, int field, string text)
	{
		_side = side;
		_field = field;
		_text = text ?? "";
		_isText = true;
	}

	// Text-valued setting (e.g. the ender link channel name).
	public static CoverConfigAction OfText(CoverSide side, int field, string text) =>
		new(side, field, text);

	public void Write(BinaryWriter w)
	{
		w.Write((byte)_side);
		w.Write(_field);
		w.Write(_isText);
		if (_isText) w.Write(_text);
		else         w.Write(_value);
	}

	public void Read(BinaryReader r)
	{
		_side = (CoverSide)r.ReadByte();
		_field = r.ReadInt32();
		_isText = r.ReadBoolean();
		if (_isText) _text = r.ReadString();
		else         _value = r.ReadInt64();
	}

	public void Apply(ICoverable target, int byWhoAmI)
	{
		// A stray packet (cover removed between send and apply) is a harmless
		// no-op. ApplySetting / ApplySettingText validate the field id per cover.
		var cover = target.GetCoverAtSide(_side);
		if (cover is null) return;
		if (_isText) cover.ApplySettingText(_field, _text);
		else         cover.ApplySetting(_field, _value);
	}
}
