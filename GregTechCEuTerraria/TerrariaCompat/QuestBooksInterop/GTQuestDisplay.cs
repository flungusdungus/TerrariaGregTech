#nullable enable
using GregTechCEuTerraria.TerrariaCompat.Questbook;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using QuestBooks.Quests;
using QuestBooks.QuestLog.DefaultElements;
using Terraria;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.QuestBooksInterop;

// Custom QuestDisplay that carries our quest id directly + replaces the
// default per-state draw methods. See header comments on each override for
// the rationale; this stays a thin shim around QB's QuestDisplay.
[ExtendsFromMod("QuestBooks")]
public sealed class GTQuestDisplay : QuestDisplay
{
	[JsonProperty]
	public string GTQuestId { get; set; } = "";

	public override Quest Quest => GTQuestRegistry.Get(GTQuestId);

	// Render-size for item icons on the graph, in canvas (logical) pixels.
	// 24 lands the item inside QB's Medium-badge background (the badge itself
	// is ~32 px) so the player sees "blue badge with item on top".
	private const int IconSize = 24;

	private int IconItemType
	{
		get
		{
			if (string.IsNullOrEmpty(GTQuestId)) return 0;
			if (!QuestbookSystem.Resolved.TryGetValue(GTQuestId, out var r)) return 0;
			return r.IconType;
		}
	}

	// Sized to match the Medium badge background, giving a comfortable click
	// target. QB's stock IsHovered uses _completedTexture.Size() which is fine
	// once that texture loads, but we want a single fixed hit-rect that
	// matches whatever we DRAW regardless of base-texture metadata.
	public override bool IsHovered(Vector2 mousePosition, Vector2 canvasOffset, float zoom, ref string mouseTooltip)
	{
		const int hit = 32;
		var rect = new Rectangle(
			(int)(CanvasPosition.X - hit / 2),
			(int)(CanvasPosition.Y - hit / 2),
			hit, hit);
		bool hovered = rect.Contains(mousePosition.ToPoint());
		bool unlocked = Unlocked() || QuestBooks.Systems.QuestLogDrawer.ActiveStyle.UseDesigner;
		string? tooltip = unlocked ? Quest.HoverTooltip : Quest.LockedTooltip;
		if (hovered && tooltip != null)
			mouseTooltip = tooltip;
		return hovered && unlocked;
	}

	// All three state methods follow the same shape: draw QB's Medium badge
	// (for the blue background look the player expects) tinted per-state,
	// then the item icon on top. Falls back to base behavior when no item
	// is resolved so unresolved kubejs:* quests still render *something*.
	protected override void DrawCompleted(SpriteBatch sb, Vector2 canvasOffset, float zoom, bool hovered, bool selected)
	{
		int item = IconItemType;
		if (item <= 0) { base.DrawCompleted(sb, canvasOffset, zoom, hovered, selected); return; }
		DrawOutlineFor(sb, canvasOffset, zoom, hovered, selected);
		DrawBadge(sb, canvasOffset, zoom, Color.White);
		DrawItemIcon(sb, item, canvasOffset, zoom);
	}

	protected override void DrawIncomplete(SpriteBatch sb, Vector2 canvasOffset, float zoom, bool hovered, bool selected)
	{
		int item = IconItemType;
		if (item <= 0) { base.DrawIncomplete(sb, canvasOffset, zoom, hovered, selected); return; }
		DrawOutlineFor(sb, canvasOffset, zoom, hovered, selected);
		// QB's base DrawIncomplete tints the badge to mid-gray via the grayscale
		// shader; we keep it full color (the player can already tell from the
		// completion checkmark + dim outline that it isn't done yet) so the
		// item icon stays recognisable.
		DrawBadge(sb, canvasOffset, zoom, Color.White);
		DrawItemIcon(sb, item, canvasOffset, zoom);
	}

	protected override void DrawLocked(SpriteBatch sb, Vector2 canvasOffset, float zoom, bool hovered, bool selected)
	{
		int item = IconItemType;
		if (item <= 0) { base.DrawLocked(sb, canvasOffset, zoom, hovered, selected); return; }
		DrawOutlineFor(sb, canvasOffset, zoom, hovered, selected);
		// Locked quests dim both the badge and the icon so the player reads
		// "not yet reachable" without ambiguity.
		DrawBadge(sb, canvasOffset, zoom, new Color(80, 80, 80));
		DrawItemIcon(sb, item, canvasOffset, zoom, new Color(120, 120, 120));
	}

	// Re-uses the parent's _completedTexture pipeline (Medium badge by default)
	// so we never duplicate the asset-loading logic and any future change to the
	// badge ships in one place.
	private void DrawBadge(SpriteBatch sb, Vector2 canvasOffset, float zoom, Color color)
	{
		// `Texture` is QB's public accessor for the private _completedTexturePath
		// field (which we set via JSON to the Medium asset). Load on demand.
		_completedTexture ??= ModContent.Request<Texture2D>(Texture);
		var tex = _completedTexture.Value;
		Vector2 drawPos = (CanvasPosition - canvasOffset) * zoom;
		sb.Draw(tex, drawPos, null, color, 0f, tex.Size() * 0.5f, zoom, SpriteEffects.None, 0f);
	}

	private void DrawOutlineFor(SpriteBatch sb, Vector2 canvasOffset, float zoom, bool hovered, bool selected)
	{
		Color col;
		if (selected)              col = Color.Yellow;
		else if (hovered)          col = Color.LightGray;
		else                       col = new Color(108, 118, 199);
		DrawOutline(sb, canvasOffset, zoom, col);
	}

	// Item icon routed through QuestbookIcon -> vanilla ItemSlot.Draw so frame
	// selection, MaterialItem layer compositing, item animations, and ModItem
	// PreDraw hooks all work the same way they do in our existing questbook
	// UI + the recipe browser. The earlier raw-texture path was drawing
	// "Any Hammer" as the whole vertical hammer strip - this fixes that.
	private void DrawItemIcon(SpriteBatch sb, int itemType, Vector2 canvasOffset, float zoom, Color? tint = null)
	{
		Vector2 center = (CanvasPosition - canvasOffset) * zoom;
		QuestbookIcon.Draw(sb, itemType, center, IconSize * zoom);
	}
}
