#nullable enable
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.NPCs.EBFChan;

// Dev tool. Run "/character_export" while controlling a character you designed to
// look like a town NPC (EBF-chan or any future one). It dumps that character's full
// appearance (skin/hair/colors + the 20-slot armor array + 10-slot dye array) as a
// ready-to-paste C# block, so the values can be baked into an appearance class
// (e.g. EBFChanAppearance). Writes to:
//   Documents/My Games/Terraria/tModLoader/GregTechCEuTerraria/character-appearance.txt
// and the client log. Client-side; reads Main.LocalPlayer only.
public class EBFChanExportCommand : ModCommand
{
	public override CommandType Type => CommandType.Chat;
	public override string Command => "character_export";
	public override string Description => "Dump the current character's appearance for baking a town NPC.";

	public override void Action(CommandCaller caller, string input, string[] args)
	{
		Player p = Main.LocalPlayer;
		var sb = new StringBuilder();

		sb.AppendLine("// --- paste into the NPC's appearance class (e.g. EBFChanAppearance) ---");
		sb.AppendLine($"SkinVariant = {p.skinVariant};");
		sb.AppendLine($"Hair = {p.hair};");
		sb.AppendLine($"HairColor       = {C(p.hairColor)};");
		sb.AppendLine($"SkinColor       = {C(p.skinColor)};");
		sb.AppendLine($"EyeColor        = {C(p.eyeColor)};");
		sb.AppendLine($"ShirtColor      = {C(p.shirtColor)};");
		sb.AppendLine($"UnderShirtColor = {C(p.underShirtColor)};");
		sb.AppendLine($"PantsColor      = {C(p.pantsColor)};");
		sb.AppendLine($"ShoeColor       = {C(p.shoeColor)};");

		sb.Append("// Armor (0-2 equip, 3-9 acc, 10-12 vanity, 13-19 acc-vanity): ");
		for (int i = 0; i < p.armor.Length; i++)
		{
			int t = p.armor[i].type;
			sb.Append(t);
			if (t > 0) sb.Append($" /*{i}:{p.armor[i].Name}*/");
			if (i < p.armor.Length - 1) sb.Append(", ");
		}
		sb.AppendLine();
		sb.Append("// Dye (one per equip slot 0-9): ");
		for (int i = 0; i < p.dye.Length; i++)
		{
			sb.Append(p.dye[i].type);
			if (i < p.dye.Length - 1) sb.Append(", ");
		}
		sb.AppendLine();

		// Also emit the array initializers verbatim for direct paste.
		sb.Append("Armor = { ");
		for (int i = 0; i < p.armor.Length; i++)
		{
			sb.Append(p.armor[i].type);
			if (i < p.armor.Length - 1) sb.Append(", ");
		}
		sb.AppendLine(" };");
		sb.Append("Dye   = { ");
		for (int i = 0; i < p.dye.Length; i++)
		{
			sb.Append(p.dye[i].type);
			if (i < p.dye.Length - 1) sb.Append(", ");
		}
		sb.AppendLine(" };");

		string dir = Path.Combine(Main.SavePath, "GregTechCEuTerraria");
		Directory.CreateDirectory(dir);
		string path = Path.Combine(dir, "character-appearance.txt");
		File.WriteAllText(path, sb.ToString());

		Mod.Logger.Info("Character appearance export:\n" + sb);
		caller.Reply("Character appearance written to " + path, Color.Cyan);
	}

	private static string C(Color c) => $"new({c.R}, {c.G}, {c.B})";
}
