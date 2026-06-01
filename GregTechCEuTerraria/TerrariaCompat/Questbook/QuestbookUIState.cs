#nullable enable
using System;
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// Centred panel: chapter list | quest node graph | detail pane (below graph).
// Item quests track live inventory progress; manual quests get a Mark-Complete
// button.
public sealed class QuestbookUIState : UIState
{
	private UITerrariaPanel _panel = null!;
	private UIList _chapterList = null!;
	private QuestGraph _graph = null!;
	private UIList _detailList = null!;

	private int _chapterIndex = -1;
	private string? _selectedQuest;

	public string? SelectedQuestId => _selectedQuest;

	public override void OnInitialize()
	{
		float uiScale = Main.UIScale <= 0 ? 1f : Main.UIScale;
		float w = Main.screenWidth / uiScale * 0.95f;
		float h = Main.screenHeight / uiScale * 0.95f;

		_panel = new UITerrariaPanel
		{
			HAlign = 0.5f,
			VAlign = 0.5f,
			Width = StyleDimension.FromPixels(w),
			Height = StyleDimension.FromPixels(h),
		};
		Append(_panel);

		const int pad = 8;
		const int titleH = 28;
		const int barW = 20;
		const int chapterW = 232;

		var title = new UIText("GregTech Quests", 1.05f)
		{
			Left = StyleDimension.FromPixels(pad),
			Top = StyleDimension.FromPixels(pad),
		};
		_panel.Append(title);

		var close = new UIText("X", 1.1f)
		{
			HAlign = 1f,
			Left = StyleDimension.FromPixels(-pad),
			Top = StyleDimension.FromPixels(pad),
		};
		close.OnLeftClick += (_, _) => QuestbookUISystem.Close();
		_panel.Append(close);

		int contentTop = pad + titleH + 6;
		int contentH = (int)h - contentTop - pad;

		_chapterList = MakeList(pad, contentTop, chapterW, contentH, out var chapterBar);
		_chapterList.ManualSortMethod = items => items.Sort(
			(a, b) => ((a as ChapterRow)?.Index ?? 0) - ((b as ChapterRow)?.Index ?? 0));
		_panel.Append(_chapterList);
		_panel.Append(chapterBar);

		int rightLeft = pad + chapterW + barW + 12;
		int rightW = (int)w - rightLeft - pad;
		int graphH = (int)(contentH * 0.62f);

		_graph = new QuestGraph(this)
		{
			Left = StyleDimension.FromPixels(rightLeft),
			Top = StyleDimension.FromPixels(contentTop),
			Width = StyleDimension.FromPixels(rightW),
			Height = StyleDimension.FromPixels(graphH),
		};
		_panel.Append(_graph);

		int detailTop = contentTop + graphH + 8;
		_detailList = MakeList(rightLeft, detailTop, rightW, contentH - graphH - 8, out var detailBar);
		_panel.Append(_detailList);
		_panel.Append(detailBar);

		BuildChapters();
		if (QuestbookSystem.Data.Chapters.Count > 0)
			SelectChapter(0);
	}

	private static UIList MakeList(int left, int top, int width, int height, out UIScrollbar bar)
	{
		const int barW = 20;
		var list = new UIList
		{
			Left = StyleDimension.FromPixels(left),
			Top = StyleDimension.FromPixels(top),
			Width = StyleDimension.FromPixels(width - barW - 4),
			Height = StyleDimension.FromPixels(height),
			ListPadding = 2f,
		};
		bar = new UIScrollbar
		{
			Left = StyleDimension.FromPixels(left + width - barW),
			Top = StyleDimension.FromPixels(top),
			Width = StyleDimension.FromPixels(barW),
			Height = StyleDimension.FromPixels(height),
		};
		list.SetScrollbar(bar);
		return list;
	}

	private void BuildChapters()
	{
		_chapterList.Clear();
		for (int i = 0; i < QuestbookSystem.Data.Chapters.Count; i++)
			_chapterList.Add(new ChapterRow(this, i));
	}

	public void SelectChapter(int index)
	{
		_chapterIndex = index;
		_selectedQuest = null;
		_detailList.Clear();

		if (index < 0 || index >= QuestbookSystem.Data.Chapters.Count)
			return;

		_graph.LoadChapter(QuestbookSystem.Data.Chapters[index]);
	}

	internal void SelectQuest(QuestData quest)
	{
		_selectedQuest = quest.Id;
		_detailList.Clear();

		_detailList.Add(new WrappedText(quest.Title, 0.95f, Color.White));

		if (!string.IsNullOrEmpty(quest.Subtitle))
			_detailList.Add(new WrappedText(quest.Subtitle, 0.72f, new Color(190, 190, 210)));

		if (!string.IsNullOrEmpty(quest.Desc))
			_detailList.Add(new WrappedText(quest.Desc, 0.78f, new Color(215, 215, 215)));

		if (QuestbookSystem.Resolved.TryGetValue(quest.Id, out ResolvedQuest? resolved))
		{
			foreach (ResolvedTask task in resolved.Tasks)
				_detailList.Add(new TaskLine(task));

			if (!resolved.AutoCheck)
				_detailList.Add(new CompleteButton(quest.Id));
		}
	}

	// Mouse-interface capture is done once by QuestbookUISystem.UpdateUI
	// across the whole screen while the modal is open.

	private sealed class ChapterRow : UIElement
	{
		private readonly QuestbookUIState _owner;
		private readonly int _index;

		// Read by ManualSortMethod - UIList's default sort doesn't preserve
		// insertion order on every Add, so we pin to data order explicitly.
		internal int Index => _index;

		public ChapterRow(QuestbookUIState owner, int index)
		{
			_owner = owner;
			_index = index;
			Width = StyleDimension.Fill;
			Height = StyleDimension.FromPixels(26);
			OnLeftClick += (_, _) => _owner.SelectChapter(_index);
		}

		protected override void DrawSelf(SpriteBatch sb)
		{
			ChapterData chapter = QuestbookSystem.Data.Chapters[_index];
			CalculatedStyle d = GetDimensions();

			bool selected = _owner._chapterIndex == _index;
			if (selected)
				sb.Draw(TextureAssets.MagicPixel.Value, d.ToRectangle(), new Color(120, 130, 200) * 0.35f);
			else if (IsMouseHovering)
				sb.Draw(TextureAssets.MagicPixel.Value, d.ToRectangle(), Color.White * 0.10f);

			int done = 0;
			foreach (NodeData n in chapter.Nodes)
				if (QuestbookProgress.IsComplete(n.Quest))
					done++;
			int total = chapter.Nodes.Count;

			string name = chapter.Title;
			var color = done >= total && total > 0
				? new Color(120, 230, 120)
				: Color.White;
			Terraria.Utils.DrawBorderString(sb, name, new Vector2(d.X + 6, d.Y + 5), color, 0.78f);
			Terraria.Utils.DrawBorderString(sb, $"{done}/{total}",
				new Vector2(d.X + d.Width - 52, d.Y + 5), new Color(180, 180, 195), 0.72f);
		}
	}

	private sealed class WrappedText : UIElement
	{
		private readonly string _text;
		private readonly float _scale;
		private readonly Color _color;

		public WrappedText(string text, float scale, Color color)
		{
			_text = text;
			_scale = scale;
			_color = color;
			Width = StyleDimension.Fill;
			Height = StyleDimension.FromPixels(20);
		}

		public override void Recalculate()
		{
			base.Recalculate();
			float width = GetInnerDimensions().Width;
			if (width <= 0)
				return;
			// Measure real multi-line height - line spacing varies by font/scale,
			// a per-line constant under-counts and overlaps the next UIList row.
			string wrapped = WrapFor(width);
			float h = FontAssets.MouseText.Value.MeasureString(wrapped).Y * _scale;
			Height = StyleDimension.FromPixels(h + 8f);
		}

		private string WrapFor(float width)
			=> FontAssets.MouseText.Value.CreateWrappedText(_text, width / _scale);

		protected override void DrawSelf(SpriteBatch sb)
		{
			CalculatedStyle d = GetDimensions();
			Terraria.Utils.DrawBorderString(sb, WrapFor(d.Width),
				new Vector2(d.X + 4, d.Y + 2), _color, _scale);
		}
	}

	private sealed class TaskLine : UIElement
	{
		private readonly ResolvedTask _task;

		public TaskLine(ResolvedTask task)
		{
			_task = task;
			Width = StyleDimension.Fill;
			Height = StyleDimension.FromPixels(26);
		}

		protected override void DrawSelf(SpriteBatch sb)
		{
			CalculatedStyle d = GetDimensions();

			if (!_task.IsItem)
			{
				Terraria.Utils.DrawBorderString(sb, "- Manual checkmark",
					new Vector2(d.X + 6, d.Y + 5), new Color(190, 190, 205), 0.78f);
				return;
			}

			int iconType = _task.AcceptTypes.Length > 0 ? _task.AcceptTypes[0] : 0;
			if (iconType > 0)
			{
				var box = new Rectangle((int)d.X + 4, (int)d.Y + 3, 20, 20);
				QuestbookIcon.Draw(sb, iconType, box.Center.ToVector2(), 20f);
			}

			// FTB label ("Any Logs", "Iron-bearing Ores") -> first resolved item
			// -> "(unresolved)".
			string name = !string.IsNullOrEmpty(_task.Label)
				? _task.Label
				: (iconType > 0 ? Lang.GetItemNameValue(iconType) : "(unresolved)");

			int have = 0;
			foreach (int t in _task.AcceptTypes)
				have += QuestbookSystem.CountInInventory(Main.LocalPlayer, t);
			bool resolved = _task.AcceptTypes.Length > 0;
			bool ok = resolved && have >= _task.Count;
			string progress = resolved ? $"  ({have}/{_task.Count})" : "";

			Terraria.Utils.DrawBorderString(sb, $"{_task.Count}x {name}{progress}",
				new Vector2(d.X + 30, d.Y + 5), ok ? new Color(120, 230, 120) : Color.White, 0.78f);
		}
	}

	private sealed class CompleteButton : UIElement
	{
		private readonly string _questId;

		public CompleteButton(string questId)
		{
			_questId = questId;
			Width = StyleDimension.Fill;
			Height = StyleDimension.FromPixels(30);
			OnLeftClick += (_, _) =>
			{
				if (!QuestbookProgress.IsComplete(_questId))
					QuestbookProgress.MarkManual(_questId);
			};
		}

		protected override void DrawSelf(SpriteBatch sb)
		{
			CalculatedStyle d = GetDimensions();
			bool complete = QuestbookProgress.IsComplete(_questId);
			var box = new Rectangle((int)d.X + 4, (int)d.Y + 4, (int)d.Width - 8, 22);

			Color fill = complete
				? new Color(40, 90, 40)
				: (IsMouseHovering ? new Color(80, 90, 150) : new Color(55, 60, 95));
			sb.Draw(TextureAssets.MagicPixel.Value, box, fill);

			string label = complete ? "Completed" : "Mark Complete";
			Vector2 size = FontAssets.MouseText.Value.MeasureString(label) * 0.82f;
			Terraria.Utils.DrawBorderString(sb, label,
				new Vector2(box.Center.X - size.X * 0.5f, box.Center.Y - size.Y * 0.5f),
				complete ? new Color(140, 230, 140) : Color.White, 0.82f);
		}
	}
}
