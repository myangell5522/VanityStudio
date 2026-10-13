using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace VanityStudio.Core
{
	/// <summary>
	/// Every design lives on disk next to the player/world saves, so it's the same library
	/// no matter which character, world or server you're on.
	/// </summary>
	public static class DesignLibrary
	{
		private const string Extension = ".vsd";
		private static readonly byte[] Magic = { (byte)'V', (byte)'S', (byte)'D', (byte)'1' };

		private static readonly List<Design> designs = new();
		private static bool loaded;

		public static string Root => Path.Combine(Main.SavePath, "VanityStudio");
		public static string ProjectsDir => Path.Combine(Root, "Projects");
		public static string ExportsDir => Path.Combine(Root, "Exports");

		public static IReadOnlyList<Design> All {
			get {
				EnsureLoaded();
				return designs;
			}
		}

		public static IEnumerable<Design> Newest => All.OrderByDescending(d => d.Modified);

		public static void EnsureLoaded() {
			if (loaded)
				return;
			loaded = true;
			designs.Clear();
			try {
				Directory.CreateDirectory(ProjectsDir);
				foreach (string file in Directory.GetFiles(ProjectsDir, "*" + Extension)) {
					Design d = TryLoadFile(file);
					if (d != null && designs.All(x => x.Id != d.Id))
						designs.Add(d);
				}
			}
			catch (Exception e) {
				ModContent.GetInstance<VanityStudio>().Logger.Error("Could not read the design library", e);
			}
		}

		public static void Unload() {
			designs.Clear();
			loaded = false;
		}

		public static Design Find(Guid id) {
			if (id == Guid.Empty)
				return null;
			return All.FirstOrDefault(d => d.Id == id);
		}

		public static void Add(Design d) {
			EnsureLoaded();
			if (!designs.Contains(d))
				designs.Add(d);
			Save(d);
		}

		public static void Save(Design d) {
			try {
				Directory.CreateDirectory(ProjectsDir);
				string path = PathFor(d);
				string temp = path + ".tmp";
				WriteFile(temp, d);
				File.Move(temp, path, true);
			}
			catch (Exception e) {
				ModContent.GetInstance<VanityStudio>().Logger.Error($"Saving design '{d.Name}' failed", e);
			}
		}

		public static void Delete(Design d) {
			designs.Remove(d);
			try {
				string path = PathFor(d);
				if (File.Exists(path))
					File.Delete(path);
			}
			catch (Exception e) {
				ModContent.GetInstance<VanityStudio>().Logger.Warn($"Could not delete {d.Name}: {e.Message}");
			}
		}

		public static void WriteFile(string path, Design d) {
			using var fs = File.Create(path);
			fs.Write(Magic);
			fs.Write(d.ToBytes());
		}

		public static Design TryLoadFile(string path) {
			try {
				byte[] data = File.ReadAllBytes(path);
				if (data.Length < 4 || data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2] || data[3] != Magic[3])
					return null;
				return Design.FromBytes(data[4..]);
			}
			catch (Exception e) {
				ModContent.GetInstance<VanityStudio>().Logger.Warn($"Skipping broken design file {Path.GetFileName(path)}: {e.Message}");
				return null;
			}
		}

		private static string PathFor(Design d) => Path.Combine(ProjectsDir, d.Id.ToString("N") + Extension);

		public static string SafeFileName(string name) {
			if (string.IsNullOrWhiteSpace(name))
				name = "design";
			foreach (char c in Path.GetInvalidFileNameChars())
				name = name.Replace(c, '_');
			return name.Trim();
		}

		public static string UniqueName(string wanted) {
			if (All.All(d => d.Name != wanted))
				return wanted;
			for (int i = 2; ; i++) {
				string n = $"{wanted} ({i})";
				if (All.All(d => d.Name != n))
					return n;
			}
		}
	}
}
