#nullable enable
using GregTechCEuTerraria.TerrariaCompat.UI.Widgets;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// The clickable "MOD IS WIP" Discord-invite banner: a bottom-left-anchored
// UITerrariaPanel (the mod's standard widget chrome) with link text inside.
// Click -> open the invite URL. HAlign=0/VAlign=1 on the panel handles the
// bottom-left anchoring in tML's UI space, so it stays pinned to the
// bottom-left corner at any resolution / UI scale.
public sealed class DiscordInviteState : UIState
{
	private const string Line1 = "MOD IS WIP. Help with playtesting/feedback at";
	private const string Line2 = "https://discord.gg/sqs4G7u6eX pleeease";
	private const string Url   = "https://discord.gg/sqs4G7u6eX";
	private const float  Scale = 1.35f;

	private static readonly Color Idle  = new(230, 70, 70);
	private static readonly Color Hover = new(255, 110, 110);

	private UITerrariaPanel? _panel;
	private UIText? _text1;
	private UIText? _text2;

	public override void OnInitialize()
	{
		var font = FontAssets.MouseText.Value;
		float w1 = font.MeasureString(Line1).X * Scale;
		float w2 = font.MeasureString(Line2).X * Scale;
		float lineH = font.MeasureString(Line1).Y * Scale;

		_panel = new UITerrariaPanel { HAlign = 0f, VAlign = 1f };
		_panel.Left.Set(8f, 0f);
		_panel.Top.Set(0f, -0.12f);
		_panel.Width.Set(System.Math.Max(w1, w2) + 24f, 0f);
		_panel.Height.Set(lineH * 2f + 16f, 0f);
		_panel.OnLeftClick += (_, _) => Terraria.Utils.OpenToURL(Url);

		_text1 = new UIText(Line1, Scale) { HAlign = 0.5f, VAlign = 0.5f, TextColor = Idle };
		_text1.Top.Set(-lineH * 0.5f, 0f);
		_text2 = new UIText(Line2, Scale) { HAlign = 0.5f, VAlign = 0.5f, TextColor = Idle };
		_text2.Top.Set(lineH * 0.5f, 0f);
		_panel.Append(_text1);
		_panel.Append(_text2);
		Append(_panel);
	}

	public override void Update(GameTime gameTime)
	{
		base.Update(gameTime);
		if (_panel != null && _text1 != null && _text2 != null)
		{
			Color c = _panel.IsMouseHovering ? Hover : Idle;
			_text1.TextColor = c;
			_text2.TextColor = c;
		}
	}
}
