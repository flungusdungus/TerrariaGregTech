#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.BigProgressBar;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Bosses.SoulDistiller;

// One health bar for the whole Soul Distiller fight, summing every living head -
// the parent before the split, and all four fraction sub-worms after. Assigned
// to NPC.BossBar on every head (SoulDistillerHeadBase.ConfigureCommonDefaults).
public class SoulDistillerBossBar : ModBossBar
{
	// ModTexturedType requires a real asset; the fancy-bar frame is vanilla, so
	// this is only the autoload anchor (unused for the fill).
	public override string Texture => "GregTechCEuTerraria/Content/Textures/block/casings/solid/machine_casing_clean_stainless_steel";

	public override Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame) =>
		SoulDistillerRenderer.BossHeadAsset!; // null falls back to a question-mark icon

	public override bool? ModifyInfo(ref BigProgressBarInfo info, ref float life, ref float lifeMax, ref float shield, ref float shieldMax)
	{
		if (info.npcIndexToAimAt < 0 || info.npcIndexToAimAt >= Main.maxNPCs) return false;
		if (!Main.npc[info.npcIndexToAimAt].active) return false;

		float l = 0f, m = 0f;
		for (int i = 0; i < Main.maxNPCs; i++)
		{
			NPC n = Main.npc[i];
			if (n.active && n.ModNPC is SoulDistillerHeadBase)
			{
				l += Terraria.Utils.Clamp(n.life, 0, n.lifeMax);
				m += n.lifeMax;
			}
		}
		if (m <= 0f) return false;

		life = l;
		lifeMax = m;
		return true;
	}
}
