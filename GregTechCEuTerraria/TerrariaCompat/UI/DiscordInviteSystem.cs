#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Config;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace GregTechCEuTerraria.TerrariaCompat.UI;

// Hosts the clickable Discord-invite banner (DiscordInviteState) at the top of
// the screen while in-game. Always shown unless the player turns it off via
// GTClientConfig.ShowDiscordInvite.
public sealed class DiscordInviteSystem : ModSystem
{
	private const string LayerName = "GregTechCEuTerraria: Discord Invite";

	private UserInterface? _ui;
	private DiscordInviteState? _state;

	public override void Load()
	{
		if (Main.dedServ) return;
		_state = new DiscordInviteState();
		_ui = new UserInterface();
		_ui.SetState(_state);
	}

	public override void Unload()
	{
		_ui = null;
		_state = null;
	}

	private static bool ShouldShow() =>
		!Main.gameMenu && !Main.dedServ && (GTClientConfig.Instance?.ShowDiscordInvite ?? false);

	public override void UpdateUI(GameTime gameTime)
	{
		if (_ui != null && ShouldShow())
			_ui.Update(gameTime);
	}

	public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
	{
		// Draw just under the cursor-tooltip layer so mouse text stays on top.
		int idx = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
		if (idx == -1) idx = layers.Count;
		layers.Insert(idx, new LegacyGameInterfaceLayer(LayerName, () =>
		{
			if (_ui?.CurrentState != null && ShouldShow())
				_ui.Draw(Main.spriteBatch, new GameTime());
			return true;
		}, InterfaceScaleType.UI));
	}
}
