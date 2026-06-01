#nullable enable
using System.Collections.Generic;
using GregTechCEuTerraria.Api.Pipenet;
using GregTechCEuTerraria.TerrariaCompat.Pipelike;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.Fluid;
using GregTechCEuTerraria.TerrariaCompat.Pipelike.ItemPipe;
using Terraria;
using Terraria.Localization;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Pipes;

// Single source of truth for held-item behaviour across PipeItem and
// SimpleItem/FluidPipeItem - the controls-hint + cell-info hover tooltip and
// the RMB-held cut. Per-stack `removeCooldown` is owned by the caller so the
// cadence survives across HoldItem ticks of the same instance.
public static class PipeHeldItemBehavior
{
	public static void Tick(
		Player player,
		PipeKind kind,
		string heldKindLabel,
		IGridLayerHandle layer,
		ref int removeCooldown,
		int useTime)
	{
		if (Main.myPlayer != player.whoAmI) return;
		Hover(player, kind, heldKindLabel);
		HandleRmbCut(player, layer, ref removeCooldown, useTime);
	}

	// Per-kind: an item-pipe item reads only the item layer, etc. - matches
	// the PostDrawTiles foreground-overlay gating.
	private static void Hover(Player player, PipeKind kind, string heldKindLabel)
	{
		if (player.mouseInterface || Main.gameMenu) return;
		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (x <= 0 || x >= Main.maxTilesX - 1 || y <= 0 || y >= Main.maxTilesY - 1) return;

		string controlsLine = $"[c/A0E0FF:LMB]: Place {heldKindLabel}   [c/FFA0A0:RMB]: Remove {heldKindLabel}";

		string? line1 = null;
		string? line2 = null;
		if (kind == PipeKind.Laser)
		{
			// Laser pipes have no per-cell payload; surface active/idle only -
			// the held-item view is "what am I about to do", not full diagnostic.
			if (!Pipelike.Laser.LaserPipeLayerSystem.Pipes.Has(x, y))
			{
				UI.WorldHoverTooltip.Set(controlsLine); return;
			}
			line1 = "Laser Pipe";
			bool active = Pipelike.Laser.LaserPipeLayerSystem.IsActive(x, y);
			line2 = active ? "[c/55FF55:Active]" : "[c/AAAAAA:Idle]";
		}
		else if (kind == PipeKind.Fluid)
		{
			var c = FluidPipeLayerSystem.Pipes.CellAt(x, y);
			if (c is null) { UI.WorldHoverTooltip.Set(controlsLine); return; }
			var f = c.Value;
			string sizeWord = PipeSizes.Word(f.Size);
			line1 = f.IsSimple
				? "Simple Fluid Pipe"
				: $"{HumanizeMaterial(f.MaterialId)} {Capitalize(sizeWord)} Fluid Pipe";
			var proofs = new List<string>();
			if (f.GasProof)    proofs.Add("gas");
			if (f.CryoProof)   proofs.Add("cryo");
			if (f.PlasmaProof) proofs.Add("plasma");
			if (f.AcidProof)   proofs.Add("acid");
			string proofLine = proofs.Count == 0 ? "" : $"  *  {string.Join(" / ", proofs)}-proof";
			line2 = $"{f.Throughput:N0} mB/t  *  {f.Channels} channel{(f.Channels == 1 ? "" : "s")}  *  max {f.MaxFluidTemperature}K{proofLine}";
		}
		else
		{
			var c = ItemPipeLayerSystem.Pipes.CellAt(x, y);
			if (c is null) { UI.WorldHoverTooltip.Set(controlsLine); return; }
			var i = c.Value;
			string sizeWord = PipeSizes.Word(i.Size);
			string kindWord = i.Restrictive ? "Restrictive Item Pipe" : "Item Pipe";
			line1 = i.IsSimple
				? "Simple Item Pipe"
				: $"{HumanizeMaterial(i.MaterialId)} {Capitalize(sizeWord)} {kindWord}";
			float rate = i.TransferRate;
			line2 = (rate % 1 != 0f)
				? $"[c/55FFFF:Transfer Rate:] {(int)((rate * 64f) + 0.5f)} items/s"
				: $"[c/55FFFF:Transfer Rate:] {(int)rate} stacks/s";
		}

		UI.WorldHoverTooltip.Set(line2 is null
			? string.Join("\n", controlsLine, line1)
			: string.Join("\n", controlsLine, line1, line2));
	}

	// Routes strictly through the supplied layer handle so a fluid pipe
	// can't cut item pipes and vice versa. Cooldown matches useTime.
	private static void HandleRmbCut(Player player, IGridLayerHandle layer, ref int removeCooldown, int useTime)
	{
		if (!Main.mouseRight) { removeCooldown = 0; return; }
		if (removeCooldown > 0) { removeCooldown--; return; }

		int x = (int)(Main.MouseWorld.X / 16);
		int y = (int)(Main.MouseWorld.Y / 16);
		if (layer.CutAt(x, y, player))
			removeCooldown = useTime;
	}

	private static string HumanizeMaterial(string materialId)
	{
		string key = $"Mods.GregTechCEuTerraria.Materials.{materialId}";
		string text = Language.GetTextValue(key);
		return text == key ? TitleCase(materialId) : text;
	}

	private static string TitleCase(string snake)
	{
		var sb = new System.Text.StringBuilder(snake.Length);
		bool capNext = true;
		foreach (char c in snake)
		{
			if (c == '_') { sb.Append(' '); capNext = true; continue; }
			sb.Append(capNext ? char.ToUpperInvariant(c) : c);
			capNext = false;
		}
		return sb.ToString();
	}

	private static string Capitalize(string s) =>
		string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
