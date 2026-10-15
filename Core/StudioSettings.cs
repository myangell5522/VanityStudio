using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;

namespace VanityStudio.Core
{
	// Tiny key=value file. A ModConfig felt like overkill for three values nobody edits by hand.
	public static class StudioSettings
	{
		public static bool ShowStudioButton = true;
		public static List<Color> RecentColors = new();
		public static bool ShowReference = true;
		public static bool ShowGrid = true;
		public static bool TutorialDone;
		public static int PreviewScale = 3;
		public static bool PreviewNight;

		private static string FilePath => Path.Combine(DesignLibrary.Root, "settings.txt");

		public static void Load() {
			try {
				if (!File.Exists(FilePath))
					return;
				foreach (string line in File.ReadAllLines(FilePath)) {
					int eq = line.IndexOf('=');
					if (eq <= 0)
						continue;
					string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
					switch (key) {
						case "studioButton":
							ShowStudioButton = value != "0";
							break;
						case "reference":
							ShowReference = value != "0";
							break;
						case "grid":
							ShowGrid = value != "0";
							break;
						case "tutorialDone":
							TutorialDone = value == "1";
							break;
						case "previewScale":
							if (int.TryParse(value, out int ps))
								PreviewScale = Math.Clamp(ps, 2, 4);
							break;
						case "retextures":
							Retextures.Enabled.Clear();
							foreach (string id in value.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
								if (Guid.TryParse(id, out Guid g))
									Retextures.Enabled.Add(g);
							}
							break;
						case "previewNight":
							PreviewNight = value == "1";
							break;
						case "recent":
							RecentColors = value.Split(',', StringSplitOptions.RemoveEmptyEntries)
								.Select(s => Pixels.TryParseHex(s, out Color c) ? c : (Color?)null)
								.Where(c => c.HasValue).Select(c => c.Value).Take(16).ToList();
							break;
					}
				}
			}
			catch (IOException) {
			}
		}

		public static void Save() {
			try {
				Directory.CreateDirectory(DesignLibrary.Root);
				File.WriteAllLines(FilePath, new[] {
					"studioButton=" + (ShowStudioButton ? "1" : "0"),
					"reference=" + (ShowReference ? "1" : "0"),
					"grid=" + (ShowGrid ? "1" : "0"),
					"tutorialDone=" + (TutorialDone ? "1" : "0"),
					"previewScale=" + PreviewScale,
					"retextures=" + string.Join(",", Retextures.Enabled.Select(g => g.ToString("N"))),
					"previewNight=" + (PreviewNight ? "1" : "0"),
					"recent=" + string.Join(",", RecentColors.Select(Pixels.ToHex))
				});
			}
			catch (IOException) {
			}
		}

		public static void PushRecent(Color c) {
			if (c.A == 0)
				return;
			RecentColors.Remove(c);
			RecentColors.Insert(0, c);
			if (RecentColors.Count > 16)
				RecentColors.RemoveRange(16, RecentColors.Count - 16);
		}
	}
}
