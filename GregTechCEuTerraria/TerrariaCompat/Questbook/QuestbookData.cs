#nullable enable
using System.Collections.Generic;

namespace GregTechCEuTerraria.TerrariaCompat.Questbook;

// DTOs mirroring Data/Questbook/questlog.json. Quests are global; chapters
// carry NodeData with per-chapter x/y. Nodes from a chapter's own quests AND
// from FTB `quest_links` (quests defined elsewhere) flatten to the same shape.

internal sealed class QuestLogData
{
	public string Pack { get; set; } = "";
	public List<ChapterData> Chapters { get; set; } = [];
	public List<QuestData> Quests { get; set; } = [];
}

internal sealed class ChapterData
{
	public string Key { get; set; } = "";
	public int Order { get; set; }
	public string Title { get; set; } = "";
	public string Group { get; set; } = "";
	public List<NodeData> Nodes { get; set; } = [];
}

internal sealed class NodeData
{
	public string Quest { get; set; } = "";
	public float X { get; set; }
	public float Y { get; set; }
}

internal sealed class QuestData
{
	public string Id { get; set; } = "";
	public string Icon { get; set; } = "";
	public string Title { get; set; } = "";
	public string Subtitle { get; set; } = "";
	public string Desc { get; set; } = "";
	public List<string> Deps { get; set; } = [];
	public List<TaskData> Tasks { get; set; } = [];
}

internal sealed class TaskData
{
	public string Type { get; set; } = "";        // "item" | "checkmark"
	public List<string> Items { get; set; } = []; // accepted ids; multiple = OR
	public string Tag { get; set; } = "";         // optional item-tag id (FTB itemfilters:tag)
	public string Label { get; set; } = "";       // FTB-supplied task title (e.g. "Any Logs")
	public int Count { get; set; } = 1;
}
