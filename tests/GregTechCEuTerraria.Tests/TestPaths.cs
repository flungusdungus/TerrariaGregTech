using System;
using System.IO;

namespace GregTechCEuTerraria.Tests;

internal static class TestPaths
{
	// Walks up from the test assembly location until it finds the repo root
	// (the dir containing GregTechCEuTerraria/). Lets tests load real bundled
	// JSON without hardcoding absolute paths.
	private static readonly Lazy<string> _repoRoot = new(() =>
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "GregTechCEuTerraria")) &&
			    Directory.Exists(Path.Combine(dir.FullName, "tools")))
				return dir.FullName;
			dir = dir.Parent;
		}
		throw new InvalidOperationException("could not locate repo root from " + AppContext.BaseDirectory);
	});

	public static string RepoRoot => _repoRoot.Value;
	public static string DataDir => Path.Combine(RepoRoot, "GregTechCEuTerraria", "Data");
	public static string MaterialsDir => Path.Combine(DataDir, "Materials");
	public static string VeinsDir => Path.Combine(DataDir, "Veins");
}
