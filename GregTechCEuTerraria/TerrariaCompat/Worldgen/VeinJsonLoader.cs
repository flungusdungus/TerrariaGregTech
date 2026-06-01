#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria.ModLoader;

namespace GregTechCEuTerraria.TerrariaCompat.Worldgen;

public static class VeinJsonLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	public static void Load(Mod mod)
	{
		VeinRegistry.Clear();

		foreach (string path in mod.GetFileNames())
		{
			if (!path.StartsWith("Data/Veins/") || !path.EndsWith(".json"))
				continue;

			using var stream = mod.GetFileStream(path);
			using var reader = new StreamReader(stream);
			string json = reader.ReadToEnd();

			var entries = JsonSerializer.Deserialize<List<VeinDefinition>>(json, JsonOptions);
			if (entries is null) continue;

			foreach (var v in entries)
				VeinRegistry.Register(v);
		}

		mod.Logger.Info($"Loaded {VeinRegistry.Count} vein definitions.");
	}
}
