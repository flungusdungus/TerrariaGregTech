#nullable enable
using Microsoft.Xna.Framework;
using Terraria;

namespace GregTechCEuTerraria.TerrariaCompat.NPCs.EBFChan;

// Baked appearance for EBF-chan, applied to a throwaway Player "mannequin" that the
// renderer draws through Terraria's normal layered player pipeline (see
// EBFChanRenderer). This is the ONLY part of the feature that depends on the user's
// designed character: design the orange-jacket look in-game, run /ebfchan_export to
// dump the values, then paste them over the stub below.
//
// armor[] is the 20-slot player equip array: 0-2 = head/body/legs (equip),
// 3-9 = accessories, 10-12 = head/body/legs (vanity), 13-19 = accessory vanity.
// The drawn look = vanity-over-equip, exactly as the game resolves it - the export
// captures the live arrays verbatim so the mannequin reproduces what the source
// player looked like. dye[] is the 10-slot dye array (one per equip slot 0-9).
public static class EBFChanAppearance
{
	// ---- Baked from a designed character via /character_export -------------
	// Mummy Shirt (body) + Power Glove (accessory), both dyed; green hair/eyes,
	// orange clothing. Re-run /character_export and re-paste to change her look.
	public static int SkinVariant = 4;
	public static int Hair = 11;

	public static Color HairColor      = new(134, 201, 120);
	public static Color SkinColor      = new(255, 192, 175);
	public static Color EyeColor       = new(134, 201, 120);
	public static Color ShirtColor     = new(223, 115, 57);
	public static Color UnderShirtColor= new(232, 126, 27);
	public static Color PantsColor     = new(59, 53, 40);
	public static Color ShoeColor      = new(134, 201, 120);

	// Armor: 0-2 head/body/legs equip, 3-9 accessories, 10-12 vanity, 13-19 acc-vanity.
	// 871 = Mummy Shirt (body), 897 = Power Glove (accessory).
	public static readonly int[] Armor = { 0, 871, 0, 897, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
	// Dye per equip slot 0-9. 3553 = body dye, 2875 = accessory dye.
	public static readonly int[] Dye = { 0, 3553, 0, 2875, 0, 0, 0, 0, 0, 0 };
	// ------------------------------------------------------------------------

	// Configure a mannequin Player's appearance from the baked data. Item slots
	// are assigned by type id; 0 leaves the slot empty.
	public static void Apply(Player p)
	{
		p.skinVariant = SkinVariant;
		p.hair = Hair;
		p.hairColor = HairColor;
		p.skinColor = SkinColor;
		p.eyeColor = EyeColor;
		p.shirtColor = ShirtColor;
		p.underShirtColor = UnderShirtColor;
		p.pantsColor = PantsColor;
		p.shoeColor = ShoeColor;

		for (int i = 0; i < p.armor.Length && i < Armor.Length; i++)
		{
			if (Armor[i] > 0) p.armor[i].SetDefaults(Armor[i]);
			else p.armor[i].TurnToAir();
		}
		for (int i = 0; i < p.dye.Length && i < Dye.Length; i++)
		{
			if (Dye[i] > 0) p.dye[i].SetDefaults(Dye[i]);
			else p.dye[i].TurnToAir();
		}
	}
}
