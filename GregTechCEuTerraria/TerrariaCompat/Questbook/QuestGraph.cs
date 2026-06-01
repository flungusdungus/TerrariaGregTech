#nullable enable
using System;
using System.Collections.Generic;
using GregTechCEuTerraria.TerrariaCompat.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// Chapter nodes drawn at their FTB x/y, joined by dep lines. A node may come
// from this chapter's quests OR an FTB quest_link (quest defined elsewhere,
// placed here). Quest content is global (QuestbookSystem.QuestsById).
public sealed class QuestGraph : UIElement
{
	private const float Grid = 62f;          // px per FTB grid unit at zoom 1
	private const float NodeWorld = 38f;     // node size at zoom 1

	private readonly QuestbookUIState _owner;

	private List<NodeData> _nodes = [];
	// Dep lines draw only when both endpoints land in THIS chapter.
	private readonly Dictionary<string, NodeData> _byQuestId = [];

	private Vector2 _pan;                    // world point shown at canvas centre
	private float _zoom = 1f;
	private bool _needsFit;

	private bool _dragging;
	private bool _dragMoved;
	private Vector2 _dragLast;

	public QuestGraph(QuestbookUIState owner) => _owner = owner;

	internal void LoadChapter(ChapterData chapter)
	{
		_nodes = chapter.Nodes;
		_byQuestId.Clear();
		foreach (NodeData n in _nodes)
			_byQuestId[n.Quest] = n;
		_needsFit = true;
	}

	private float NodeSize => NodeWorld * _zoom;

	private Vector2 World(NodeData n) => new Vector2(n.X, n.Y) * Grid;

	private Vector2 ToScreen(Vector2 world)
	{
		CalculatedStyle d = GetDimensions();
		var centre = new Vector2(d.X + d.Width * 0.5f, d.Y + d.Height * 0.5f);
		return centre + (world - _pan) * _zoom;
	}

	private void AutoFit()
	{
		CalculatedStyle d = GetDimensions();
		if (_nodes.Count == 0 || d.Width <= 0)
		{
			_pan = Vector2.Zero;
			_zoom = 1f;
			return;
		}

		Vector2 min = World(_nodes[0]);
		Vector2 max = min;
		foreach (NodeData n in _nodes)
		{
			Vector2 w = World(n);
			min = Vector2.Min(min, w);
			max = Vector2.Max(max, w);
		}

		_pan = (min + max) * 0.5f;
		float spanX = max.X - min.X + NodeWorld * 2f;
		float spanY = max.Y - min.Y + NodeWorld * 2f;
		float fit = Math.Min((d.Width - 24f) / spanX, (d.Height - 24f) / spanY);
		_zoom = MathHelper.Clamp(fit, 0.25f, 1.5f);
	}

	private QuestData? HitTest(Vector2 mouse)
	{
		float half = NodeSize * 0.5f;
		foreach (NodeData n in _nodes)
		{
			Vector2 c = ToScreen(World(n));
			if (mouse.X >= c.X - half && mouse.X <= c.X + half
				&& mouse.Y >= c.Y - half && mouse.Y <= c.Y + half
				&& QuestbookSystem.QuestsById.TryGetValue(n.Quest, out QuestData? q))
				return q;
		}
		return null;
	}

	public override void ScrollWheel(UIScrollWheelEvent evt)
	{
		base.ScrollWheel(evt);
		if (!ContainsPoint(Main.MouseScreen))
			return;
		float factor = evt.ScrollWheelValue > 0 ? 1.15f : 1f / 1.15f;
		_zoom = MathHelper.Clamp(_zoom * factor, 0.2f, 2.5f);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

		if (_needsFit && GetDimensions().Width > 0)
		{
			AutoFit();
			_needsFit = false;
		}

		if (!ContainsPoint(Main.MouseScreen))
		{
			_dragging = false;
			return;
		}

		Main.LocalPlayer.mouseInterface = true;
		Vector2 mouse = Main.MouseScreen;

		if (Main.mouseLeft)
		{
			if (!_dragging)
			{
				_dragging = true;
				_dragMoved = false;
				_dragLast = mouse;
			}
			else
			{
				Vector2 delta = mouse - _dragLast;
				if (delta.LengthSquared() > 16f)
					_dragMoved = true;
				if (_zoom > 0f)
					_pan -= delta / _zoom;
				_dragLast = mouse;
			}
		}
		else
		{
			if (_dragging && !_dragMoved)
			{
				QuestData? hit = HitTest(mouse);
				if (hit != null)
					_owner.SelectQuest(hit);
			}
			_dragging = false;
		}
	}

	protected override void DrawSelf(SpriteBatch sb)
	{
		CalculatedStyle d = GetDimensions();
		sb.Draw(TextureAssets.MagicPixel.Value, d.ToRectangle(), new Color(20, 22, 34));

		if (_nodes.Count == 0)
			return;

		ScissorDraw.Draw(sb, GetClippingRectangle(sb), () => DrawGraph(sb));
	}

	private void DrawGraph(SpriteBatch sb)
	{
		Rectangle bounds = GetDimensions().ToRectangle();

		// ScissorDraw clips partial segments so a line stays visible while
		// panning even with one endpoint off-canvas.
		foreach (NodeData n in _nodes)
		{
			if (!QuestbookSystem.QuestsById.TryGetValue(n.Quest, out QuestData? q))
				continue;
			Vector2 to = ToScreen(World(n));

			foreach (string dep in q.Deps)
				if (_byQuestId.TryGetValue(dep, out NodeData? src))
				{
					Vector2 from = ToScreen(World(src));
					Color color = QuestbookProgress.IsComplete(dep)
						? new Color(120, 200, 120)
						: new Color(90, 95, 120);
					DrawLine(sb, from, to, color, 2f);
					DrawArrowhead(sb, from, to, color);
				}
		}

		// Nodes - bounds-culled.
		QuestData? hovered = HitTest(Main.MouseScreen);
		foreach (NodeData n in _nodes)
		{
			if (!QuestbookSystem.QuestsById.TryGetValue(n.Quest, out QuestData? q))
				continue;
			Vector2 c = ToScreen(World(n));
			int size = (int)NodeSize;
			var rect = new Rectangle((int)(c.X - size * 0.5f), (int)(c.Y - size * 0.5f), size, size);
			if (!bounds.Intersects(rect))
				continue;
			DrawNode(sb, q, rect, q == hovered);
		}

		if (hovered != null)
			Main.instance.MouseText(hovered.Title);
	}

	private void DrawNode(SpriteBatch sb, QuestData q, Rectangle rect, bool hovered)
	{
		Texture2D px = TextureAssets.MagicPixel.Value;
		bool complete = QuestbookProgress.IsComplete(q.Id);
		bool selected = _owner.SelectedQuestId == q.Id;

		sb.Draw(px, rect, new Color(46, 50, 76));

		if (QuestbookSystem.Resolved.TryGetValue(q.Id, out ResolvedQuest? resolved)
			&& resolved.IconType > 0)
		{
			QuestbookIcon.Draw(sb, resolved.IconType, rect.Center.ToVector2(), rect.Width - 8f);
		}

		Color border = selected ? Color.Yellow
			: complete ? new Color(120, 230, 120)
			: hovered ? Color.White
			: new Color(95, 100, 130);
		DrawBorder(sb, rect, border, complete || selected ? 3 : 2);
	}

	private void DrawArrowhead(SpriteBatch sb, Vector2 from, Vector2 to, Color color)
	{
		Vector2 dir = to - from;
		if (dir.LengthSquared() < 1f)
			return;
		dir.Normalize();

		Vector2 tip = to - dir * (NodeSize * 0.5f + 3f);
		const float barb = 9f;
		DrawLine(sb, tip, tip + (-dir).RotatedBy(0.5) * barb, color, 2f);
		DrawLine(sb, tip, tip + (-dir).RotatedBy(-0.5) * barb, color, 2f);
	}

	private static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, Color color, float thickness)
	{
		Vector2 diff = b - a;
		float length = diff.Length();
		if (length < 0.5f)
			return;
		// Explicit 1x1 source rect so the rendered size = length x thickness
		// (MagicPixel is NOT 1x1, so default sourceRect would mis-scale).
		sb.Draw(TextureAssets.MagicPixel.Value, a, new Rectangle(0, 0, 1, 1), color, diff.ToRotation(),
			new Vector2(0f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
	}

	private static void DrawBorder(SpriteBatch sb, Rectangle r, Color color, int thickness)
	{
		Texture2D px = TextureAssets.MagicPixel.Value;
		sb.Draw(px, new Rectangle(r.X, r.Y, r.Width, thickness), color);
		sb.Draw(px, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), color);
		sb.Draw(px, new Rectangle(r.X, r.Y, thickness, r.Height), color);
		sb.Draw(px, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), color);
	}
}
