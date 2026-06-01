#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace GregTechCEuTerraria.TerrariaCompat.Items.Tools;

// One resolved render layer of a tool icon: a texture path + the colour it is
// drawn with (white = untinted). Built by ToolItemLoader from ToolModel.Layers
// plus the material's primary/secondary colours.
public readonly record struct ToolLayer(string TexturePath, Color Tint);

// Per-GTToolType layer stack - verbatim from upstream
// assets/gtceu/models/item/tools/<name>.json (the `layerN` texture list).
//
// A tool icon is composed of ordered layers under item/tools/. The tint of
// layer N mirrors upstream IGTTool.tintColor:
//   layer 0  -> untinted (handle / power unit / blade base)
//   layer 1  -> material primary colour
//   layer 2  -> material secondary colour (falls back to primary)
//   layer 3+ -> untinted
// "void" is upstream's transparent placeholder layer - skipped at draw time,
// but kept in the list so later layers keep their original tint index.
internal static class ToolModel
{
	public static readonly IReadOnlyDictionary<string, string[]> Layers = new Dictionary<string, string[]>
	{
		["axe"]            = new[] { "handle", "axe", "axe_overlay" },
		["butchery_knife"] = new[] { "butchery_knife_base", "butchery_knife", "butchery_knife_overlay" },
		["buzzsaw"]        = new[] { "handle_buzzsaw", "buzzsaw_tool", "buzzsaw_tool_overlay" },
		["crowbar"]        = new[] { "handle_crowbar", "crowbar", "crowbar_overlay" },
		["file"]           = new[] { "handle_file", "file", "file_overlay" },
		["hammer"]         = new[] { "handle_hammer", "hammer", "hammer_overlay" },
		["hoe"]            = new[] { "handle", "hoe", "hoe_overlay" },
		["knife"]          = new[] { "knife_base", "knife", "knife_overlay" },
		["mallet"]         = new[] { "handle_hammer", "mallet", "mallet_overlay" },
		["mining_hammer"]  = new[] { "mining_hammer_handle", "mining_hammer_head", "mining_hammer_overlay" },
		["mortar"]         = new[] { "mortar_base", "mortar", "mortar_overlay" },
		["pickaxe"]        = new[] { "handle", "pickaxe", "pickaxe_overlay" },
		["plunger"]        = new[] { "handle_plunger", "plunger", "plunger_overlay" },
		["saw"]            = new[] { "handle_saw", "saw", "saw_overlay" },
		["screwdriver"]    = new[] { "handle_screwdriver", "screwdriver" },
		["scythe"]         = new[] { "handle_scythe", "scythe" },
		["shovel"]         = new[] { "handle_shovel", "shovel", "shovel_overlay" },
		["spade"]          = new[] { "spade_handle", "spade", "spade_overlay" },
		["sword"]          = new[] { "sword_base", "sword", "sword_overlay" },
		["wire_cutter"]    = new[] { "wire_cutter_base", "wire_cutter", "wire_cutter_overlay" },
		["wrench"]         = new[] { "void", "wrench", "wrench_overlay" },

		["lv_drill"]       = new[] { "power_unit_lv", "drill", "void", "drill_body" },
		["mv_drill"]       = new[] { "power_unit_mv", "drill", "void", "drill_body" },
		["hv_drill"]       = new[] { "power_unit_hv", "drill", "void", "drill_body" },
		["ev_drill"]       = new[] { "power_unit_ev", "drill", "void", "drill_body" },
		["iv_drill"]       = new[] { "power_unit_iv", "drill", "void", "drill_body" },

		["lv_chainsaw"]    = new[] { "power_unit_lv", "chainsaw", "void", "chainsaw_body" },
		["hv_chainsaw"]    = new[] { "power_unit_hv", "chainsaw", "void", "chainsaw_body" },
		["iv_chainsaw"]    = new[] { "power_unit_iv", "chainsaw", "void", "chainsaw_body" },

		["lv_wrench"]      = new[] { "power_unit_lv", "wrench_electric", "wrench_electric_overlay" },
		["hv_wrench"]      = new[] { "power_unit_hv", "wrench_electric", "wrench_electric_overlay" },
		["iv_wrench"]      = new[] { "power_unit_iv", "wrench_electric", "wrench_electric_overlay" },

		["lv_wirecutter"]  = new[] { "electric_wirecutter_base", "electric_wirecutter_head" },
		["hv_wirecutter"]  = new[] { "electric_wirecutter_base_hv", "electric_wirecutter_head" },
		["iv_wirecutter"]  = new[] { "electric_wirecutter_base_iv", "electric_wirecutter_head" },

		["lv_screwdriver"] = new[] { "handle_electric_screwdriver", "screwdriver_short", "screwdriver_overlay" },
		["hv_screwdriver"] = new[] { "handle_electric_screwdriver", "screwdriver_short", "screwdriver_overlay" },
		["iv_screwdriver"] = new[] { "handle_electric_screwdriver", "screwdriver_short", "screwdriver_overlay" },
	};

	// "void" placeholder - drawn as nothing.
	public const string Void = "void";
}
