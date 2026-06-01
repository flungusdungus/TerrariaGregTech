#nullable enable
using System;
using Terraria.ModLoader.IO;

namespace GregTechCEuTerraria.TerrariaCompat.Cover.Ender;

// Port of virtualregistry.VirtualEntry - a named ender channel's shared
// payload. ColorStr (8-hex) is the channel-identity key. Parsed int `color`
// dropped (consumed only by upstream's ColorBlockWidget).
public abstract class VirtualEntry
{
	public const string DefaultColor = "FFFFFFFF";

	public string ColorStr { get; private set; } = DefaultColor;
	public string Description { get; set; } = "";

	public abstract EnderEntryType Type { get; }

	public void SetColor(string color) => ColorStr = color.ToUpperInvariant();

	// Verbatim canRemove - GC'd when last cover detaches and (per subtype) the
	// buffer is empty.
	public virtual bool CanRemove() => Description.Length == 0;

	public virtual void Save(TagCompound tag)
	{
		tag["color"] = ColorStr;
		if (Description.Length > 0) tag["desc"] = Description;
	}

	public virtual void Load(TagCompound tag)
	{
		if (tag.ContainsKey("color")) ColorStr = tag.GetString("color");
		if (tag.ContainsKey("desc")) Description = tag.GetString("desc");
	}
}
