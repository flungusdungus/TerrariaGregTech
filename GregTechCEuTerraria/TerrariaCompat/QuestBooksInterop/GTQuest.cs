#nullable enable
using System;
using System.Text;
using GregTechCEuTerraria.TerrariaCompat.Questbook;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using QuestBooks.Quests;
using QuestBooks.Utilities;
using Terraria;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.ModLoader;
using QbUtil = QuestBooks.Utilities;

namespace GregTechCEuTerraria.TerrariaCompat.QuestBooksInterop;

// Runtime-constructed Quest stand-in for one of our 661 questbook entries.
// Autoload=false: tML doesn't register it as a ModType, so QB's QuestManager
// never sees it. Per-id instances are built and cached in GTQuestRegistry
// and handed back through GTQuestDisplay.Quest. That bypass means we don't
// need 661 trivial generated subclasses just to satisfy QB's 1-class-per-id
// registration model.
//
// The instances live without a Mod reference (base.Mod stays null), so we
// override every base virtual that touches Mod to use our injected reference.
[Autoload(false)]
[ExtendsFromMod("QuestBooks")]
public sealed class GTQuest : Quest
{
	private readonly string _id;
	private readonly Mod    _ownerMod;
	// Persisted scroll offset for the info-page content. Survives across info
	// page closes/reopens for the same quest, which matches how players
	// remember "where I was reading" - cheap UX win.
	private float _scrollOffset;

	public GTQuest(string id, Mod ownerMod)
	{
		_id       = id;
		_ownerMod = ownerMod;
	}

	public override string Key                 => _id;
	public override string LocalizationCategory => "GTQuest";
	public override string TextureCategory      => $"{_ownerMod.Name}/Content/Textures";
	public override string? HoverTooltip        => null;
	public override string? LockedTooltip       => null;

	public override void MakeSimpleInfoPage(out string title, out string contents, out Texture2D? texture)
	{
		// Only used as a fallback if DrawCustomInfoPage ever returns false.
		title    = _id;
		contents = "";
		texture  = null;
		if (!QuestbookSystem.QuestsById.TryGetValue(_id, out var data)) return;
		title = string.IsNullOrEmpty(data.Title) ? _id : data.Title;
		contents = BuildContents(data);
	}

	public override bool CheckCompletion()
	{
		var player = Main.LocalPlayer?.GetModPlayer<QuestbookProgress>();
		return player != null && player.Completed.Contains(_id);
	}

	// =========================================================================
	//  Custom info page - scrollable content + Mark Complete button + icon
	//
	//  QB's stock DrawInfoPage truncates long descriptions at the content
	//  rect's fixed 450 px height and has no way to register a "complete this
	//  quest" button. We layer those in by returning true here to suppress
	//  the base path.
	// =========================================================================

	private const float ContentTextScale = 0.5f;
	private const float ContentLineSpacing = 50f;  // matches QB stock value

	public override bool DrawCustomInfoPage(SpriteBatch sb, Vector2 mousePosition, ref Action updateAction)
	{
		if (!QuestbookSystem.QuestsById.TryGetValue(_id, out var data))
			return false;  // fall back to stock; shouldn't ever fire

		string title = string.IsNullOrEmpty(data.Title) ? _id : data.Title;
		string contents = BuildContents(data);

		// Same rectangle layout QB stock uses, except the content area is a
		// bit shorter to make room for the Mark Complete button row.
		Rectangle titleArea  = new( 8,  10, 430,  64);
		Rectangle contentBox = new( 8,  80, 430, 410);
		Rectangle buttonRow  = new( 8, 500, 430,  36);

		DrawTitle(sb, titleArea, title);
		DrawIconTopRight(sb, contentBox);

		// Auto-fits or scrolls based on whether the contents overflow.
		float totalHeight = MeasureContentHeight(contents, contentBox.Width);
		float maxScroll = Math.Max(0f, totalHeight - contentBox.Height);
		_scrollOffset = Math.Clamp(_scrollOffset, 0f, maxScroll);
		DrawScrollableContent(sb, contentBox, contents);
		if (maxScroll > 0f)
			DrawScrollbar(sb, contentBox, _scrollOffset, maxScroll, totalHeight);

		// Mark Complete button only shows when the quest is gated on a manual
		// checkmark task (i.e. NOT auto-checkable from inventory) AND is
		// currently incomplete. Otherwise we'd offer a no-op.
		bool canMark = !IsAutoCheckable(data) && !Completed;
		bool buttonHovered = canMark && buttonRow.Contains(mousePosition.ToPoint());
		if (canMark)
			DrawMarkCompleteButton(sb, buttonRow, buttonHovered);

		// Per-frame logic - scroll wheel + button click - rides on updateAction
		// just like QB's stock snippet hover path. Closure captures the local
		// rectangles so the runtime state matches what we drew.
		var prevUpdate = updateAction;
		var capturedMouse = mousePosition;
		var capturedContent = contentBox;
		var capturedButton = buttonRow;
		var capturedMax = maxScroll;
		string capturedId = _id;
		updateAction = () =>
		{
			prevUpdate?.Invoke();

			// Mouse-wheel scroll while hovering the content area. Main.mouseWheel
			// is the cumulative delta since last frame (vanilla returns +120 per
			// notch up, -120 per notch down).
			if (capturedMax > 0f && capturedContent.Contains(capturedMouse.ToPoint()))
			{
				int wheel = PlayerInput.ScrollWheelDelta;
				if (wheel != 0)
				{
					// 0.5 turns one notch into a half-line of scroll - keeps
					// dense paragraphs from blurring past too fast.
					_scrollOffset = Math.Clamp(_scrollOffset - wheel * 0.5f, 0f, capturedMax);
				}
			}

			if (canMark && buttonHovered && Main.mouseLeft && Main.mouseLeftRelease)
			{
				Main.mouseLeftRelease = false;
				var player = Main.LocalPlayer?.GetModPlayer<QuestbookProgress>();
				player?.Completed.Add(capturedId);
				Terraria.Audio.SoundEngine.PlaySound(Terraria.ID.SoundID.MenuTick);
			}
		};

		return true;
	}

	// === Layout helpers =====================================================

	private static void DrawTitle(SpriteBatch sb, Rectangle titleArea, string title)
	{
		Rectangle underline = titleArea.CookieCutter(new(0f, 0.6f), new(1f, 0.05f));
		sb.DrawRectangle(underline, Color.Gray, fill: true);
		sb.DrawOutlinedStringInRectangle(
			titleArea.CookieCutter(new(0f, 0.25f), Vector2.One),
			FontAssets.DeathText.Value,
			Color.White, Color.Black,
			title,
			stroke: 2.3f, clipBounds: false,
			alignment: QbUtil.TextAlignment.Left);
	}

	private void DrawIconTopRight(SpriteBatch sb, Rectangle contentBox)
	{
		if (!QuestbookSystem.Resolved.TryGetValue(_id, out var resolved) || resolved.IconType <= 0)
			return;
		Main.instance.LoadItem(resolved.IconType);
		var asset = TextureAssets.Item[resolved.IconType];
		if (asset?.Value is not Texture2D tex) return;
		// Sized to ~64 px in the info-page coordinate space. Drawn at the
		// content box's top-right corner with origin at top-right of the
		// texture so it extends LEFT and DOWN into the content area.
		const int boxSize = 64;
		Vector2 anchor = new(contentBox.Right, contentBox.Top);
		Rectangle src = Main.itemAnimations[resolved.IconType] != null
			? Main.itemAnimations[resolved.IconType].GetFrame(tex)
			: tex.Frame();
		float scale = (float)boxSize / Math.Max(src.Width, src.Height);
		sb.Draw(tex, anchor, src, Color.White,
			rotation: 0f,
			origin: new Vector2(src.Width, 0f),
			scale: scale,
			effects: SpriteEffects.None,
			layerDepth: 0f);
	}

	private void DrawScrollableContent(SpriteBatch sb, Rectangle contentBox, string contents)
	{
		// Scissor-clip so scrolled-out lines don't bleed past the bottom edge.
		// SpriteBatch needs to End()/Begin() around scissor changes; we mirror
		// the pattern from QB's BasicQuestLogStyle.DrawIncomplete switcheroo.
		sb.End();
		sb.GetDrawParameters(out var blend, out var sampler, out var depth, out var raster, out var effect, out var matrix);
		var prevScissor = sb.GraphicsDevice.ScissorRectangle;
		var raster2 = new RasterizerState
		{
			CullMode = raster.CullMode,
			DepthBias = raster.DepthBias,
			FillMode = raster.FillMode,
			MultiSampleAntiAlias = raster.MultiSampleAntiAlias,
			SlopeScaleDepthBias = raster.SlopeScaleDepthBias,
			ScissorTestEnable = true,
		};
		sb.Begin(SpriteSortMode.Deferred, blend, sampler, depth, raster2, effect, matrix);

		// ScissorRectangle is in BACKBUFFER coords. The info page is rendered
		// to a render target that's the same logical size, so the rect we want
		// matches contentBox directly when matrix is identity. QB's info-page
		// drawing uses identity matrix; if QB changes that later we'd need to
		// translate. Verified against BasicQuestLogStyle.UpdateInfoPageArea.
		sb.GraphicsDevice.ScissorRectangle = contentBox;

		Vector2 pos = new(contentBox.X, contentBox.Y - _scrollOffset);
		sb.DrawParagraphText(
			FontAssets.DeathText.Value, pos, contents,
			scale: ContentTextScale,
			maxWidth: (int)(contentBox.Width / ContentTextScale),
			verticalSpacing: ContentLineSpacing,
			stroke: 1.8f);

		sb.End();
		sb.GraphicsDevice.ScissorRectangle = prevScissor;
		sb.Begin(SpriteSortMode.Deferred, blend, sampler, depth, raster, effect, matrix);
	}

	private static void DrawScrollbar(SpriteBatch sb, Rectangle contentBox, float scroll, float maxScroll, float totalHeight)
	{
		// 6 px wide track on the right edge of the content box.
		const int width = 6;
		Rectangle track = new(contentBox.Right - width, contentBox.Y, width, contentBox.Height);
		sb.DrawRectangle(track, new Color(0, 0, 0, 80), fill: true);

		float thumbRatio = contentBox.Height / totalHeight;
		int thumbHeight = Math.Max(20, (int)(track.Height * thumbRatio));
		int thumbY = track.Y + (int)((track.Height - thumbHeight) * (scroll / maxScroll));
		Rectangle thumb = new(track.X, thumbY, track.Width, thumbHeight);
		sb.DrawRectangle(thumb, new Color(180, 180, 180), fill: true);
	}

	private static void DrawMarkCompleteButton(SpriteBatch sb, Rectangle buttonRow, bool hovered)
	{
		// Center a 130x30 button in the row.
		const int w = 130, h = 30;
		Rectangle button = new(
			buttonRow.X + (buttonRow.Width - w) / 2,
			buttonRow.Y + (buttonRow.Height - h) / 2,
			w, h);

		Color fill = hovered ? new Color(80, 120, 80) : new Color(50, 80, 50);
		Color border = hovered ? Color.Yellow : Color.Gray;
		sb.DrawRectangle(button, fill, fill: true);
		sb.DrawRectangle(button, border, stroke: 2f);

		// Center the label text. CookieCutter trims to give the font some
		// breathing room above/below.
		sb.DrawOutlinedStringInRectangle(
			button.CookieCutter(new(0f, 0f), new(0.9f, 0.7f)),
			FontAssets.DeathText.Value,
			Color.White, Color.Black,
			"Mark Complete",
			stroke: 1.8f, clipBounds: false,
			alignment: QbUtil.TextAlignment.Middle);
	}

	// === Data helpers =======================================================

	// Quests where every task is "item" + at least one resolves to a concrete
	// Terraria item type auto-check from inventory via QuestbookProgress's
	// poll. Anything else (pure checkmark tasks, unresolvable item ids) needs
	// a manual button - that's what we draw.
	private static bool IsAutoCheckable(QuestData data)
	{
		if (!QuestbookSystem.Resolved.TryGetValue(data.Id, out var r)) return false;
		return r.AutoCheck;
	}

	private float MeasureContentHeight(string contents, int boxWidth)
	{
		// Same wrap routine DrawParagraphText runs internally, so the line
		// count we get back matches what's actually drawn.
		var lines = Terraria.Utils.WordwrapStringSmart(
			contents, Color.White, FontAssets.DeathText.Value,
			maxWidth: (int)(boxWidth / ContentTextScale),
			maxLines: -1);
		return lines.Count * ContentLineSpacing * ContentTextScale;
	}

	private static string BuildContents(QuestData data)
	{
		var sb = new StringBuilder();
		if (data.Tasks.Count > 0)
		{
			sb.AppendLine("Tasks:");
			foreach (var task in data.Tasks)
			{
				if (task.Type == "checkmark")
				{
					sb.AppendLine("  - Mark complete manually");
				}
				else if (task.Type == "item")
				{
					string label = !string.IsNullOrEmpty(task.Label)
						? task.Label
						: ResolveItemDisplay(task);
					sb.Append("  - ").Append(task.Count).Append("x ").AppendLine(label);
				}
				else
				{
					sb.Append("  - ").AppendLine(task.Type);
				}
			}
			sb.AppendLine();
		}

		if (!string.IsNullOrEmpty(data.Desc))
			sb.Append(data.Desc);

		return sb.ToString();
	}

	private static string ResolveItemDisplay(TaskData task)
	{
		if (task.Items.Count > 0)
		{
			var first = task.Items[0];
			int colon = first.IndexOf(':');
			string bare = colon >= 0 ? first.Substring(colon + 1) : first;
			return bare.Replace('_', ' ');
		}
		if (!string.IsNullOrEmpty(task.Tag))
		{
			int colon = task.Tag.IndexOf(':');
			string bare = colon >= 0 ? task.Tag.Substring(colon + 1) : task.Tag;
			return $"any {bare.Replace('_', ' ')}";
		}
		return "item";
	}
}
