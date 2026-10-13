using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>
	/// First-run walkthrough. Dims the studio, cuts a hole around the part being explained and puts a card next to it.
	/// Can be skipped at any point and reopened from the "?" button.
	/// </summary>
	internal static class Tutorial
	{
		// region names are filled in by StudioWindow every frame
		private static readonly (string key, string region)[] steps = {
			("Welcome", null),
			("Setup", "header-left"),
			("Tools", "tools"),
			("Canvas", "canvas"),
			("Poses", "poses"),
			("Timeline", "timeline"),
			("Preview", "preview"),
			("Colors", "colors"),
			("Actions", "actions"),
			("Wear", "equip")
		};

		public static readonly Dictionary<string, Rectangle> Regions = new();

		private static int step = -1;
		private static bool dontShowAgain = true;

		public static bool Active => step >= 0;

		public static void Start() {
			step = 0;
			dontShowAgain = true;
			StudioState.LibraryOpen = false;
		}

		public static void End() {
			if (step < 0)
				return;
			step = -1;
			Gui.Locked = false;
			if (dontShowAgain) {
				StudioSettings.TutorialDone = true;
				StudioSettings.Save();
			}
			SoundEngine.PlaySound(SoundID.MenuClose);
		}

		private static void Go(int to) {
			if (to >= steps.Length) {
				End();
				return;
			}
			step = Math.Max(0, to);
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		public static void Draw() {
			if (step < 0)
				return;
			Gui.Locked = false;

			var (key, region) = steps[step];
			Rectangle? target = region != null && Regions.TryGetValue(region, out Rectangle r) ? r : null;

			var screen = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);
			Color shade = Color.Black * 0.62f;
			if (target is Rectangle t) {
				t.Inflate(4, 4);
				Gui.Rect(new Rectangle(0, 0, screen.Width, t.Y), shade);
				Gui.Rect(new Rectangle(0, t.Bottom, screen.Width, screen.Height - t.Bottom), shade);
				Gui.Rect(new Rectangle(0, t.Y, t.X, t.Height), shade);
				Gui.Rect(new Rectangle(t.Right, t.Y, screen.Width - t.Right, t.Height), shade);
				float pulse = 0.6f + 0.4f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);
				Gui.Outline(t, Gui.Gold * pulse, 2);
			}
			else {
				Gui.Rect(screen, shade);
			}

			string title = L.Get("Tut" + key + "Title");
			const int width = 400;
			List<string> lines = Wrap(L.Get("Tut" + key), width - 32, 0.8f);
			int lineH = 22;
			int height = 56 + lines.Count * lineH + 78;

			Rectangle card = Place(target, width, height, screen);
			Gui.Panel(card, new Color(33, 43, 79), Gui.Gold);

			int x = card.X + 16, y = card.Y + 14;
			if (step == 0)
				DrawMascot(new Vector2(card.Right - 34, card.Y + 30));
			Gui.Text(title, new Vector2(x, y + 12), Gui.Gold, 1f, 0f, 0.5f);
			Gui.Text($"{step + 1}/{steps.Length}", new Vector2(card.Right - (step == 0 ? 64 : 16), y + 12), Gui.Muted, 0.7f, 1f, 0.5f);
			y += 38;
			foreach (string line in lines) {
				Gui.Text(line, new Vector2(x, y + lineH / 2 + 2), Color.White, 0.8f, 0f, 0.5f);
				y += lineH;
			}

			int by = card.Bottom - 40;
			Gui.Checkbox(new Rectangle(x, by - 30, width - 32, 24), L.Get("TutDontShow"), "tut-dontshow", ref dontShowAgain);

			bool last = step == steps.Length - 1;
			string nextLabel = last ? L.Get("TutFinish") : L.Get("TutNext");
			int nextW = (int)Gui.Measure(nextLabel).X + 36;
			if (Gui.Button(new Rectangle(card.Right - 16 - nextW, by, nextW, 30), nextLabel, "tut-next", true))
				Go(step + 1);
			int backW = (int)Gui.Measure(L.Get("TutBack")).X + 30;
			if (step > 0 && Gui.Button(new Rectangle(card.Right - 22 - nextW - backW, by, backW, 30), L.Get("TutBack"), "tut-back"))
				Go(step - 1);
			if (!last) {
				int skipW = (int)Gui.Measure(L.Get("TutSkip")).X + 30;
				if (Gui.Button(new Rectangle(x, by, skipW, 30), L.Get("TutSkip"), "tut-skip"))
					End();
			}

			if (Gui.EscapePressed)
				End();
			else if (KeyJustPressed(Keys.Enter) || KeyJustPressed(Keys.Right))
				Go(step + 1);
			else if (KeyJustPressed(Keys.Left))
				Go(step - 1);
		}

		private static bool KeyJustPressed(Keys k) {
			// Gui.KeyPressed is blocked while the tutorial locks the studio, so check here
			bool down = Main.keyState.IsKeyDown(k);
			bool was = Main.oldKeyState.IsKeyDown(k);
			return down && !was;
		}

		private static Rectangle Place(Rectangle? target, int w, int h, Rectangle screen) {
			Rectangle card;
			if (target is not Rectangle t) {
				card = new Rectangle(screen.Center.X - w / 2, screen.Center.Y - h / 2, w, h);
			}
			else if (t.Right + 16 + w < screen.Width - 8) {
				card = new Rectangle(t.Right + 16, t.Center.Y - h / 2, w, h);
			}
			else if (t.X - 16 - w > 8) {
				card = new Rectangle(t.X - 16 - w, t.Center.Y - h / 2, w, h);
			}
			else if (t.Bottom + 16 + h < screen.Height - 8) {
				card = new Rectangle(t.Center.X - w / 2, t.Bottom + 16, w, h);
			}
			else {
				card = new Rectangle(t.Center.X - w / 2, t.Y - 16 - h, w, h);
			}
			card.X = Math.Clamp(card.X, 8, Math.Max(8, screen.Width - w - 8));
			card.Y = Math.Clamp(card.Y, 8, Math.Max(8, screen.Height - h - 8));
			// a big region (the canvas) can leave no room outside it: then sit inside, near its bottom
			if (target is Rectangle big && big.Intersects(card) && big.Width * big.Height > screen.Width * screen.Height / 6)
				card.Y = Math.Clamp(big.Bottom - h - 16, 8, screen.Height - h - 8);
			return card;
		}

		private static List<string> Wrap(string text, float width, float scale) {
			var result = new List<string>();
			foreach (string paragraph in text.Split('\n')) {
				var line = new StringBuilder();
				foreach (string word in paragraph.Split(' ')) {
					string attempt = line.Length == 0 ? word : line + " " + word;
					if (Gui.Measure(attempt, scale).X > width && line.Length > 0) {
						result.Add(line.ToString());
						line.Clear().Append(word);
					}
					else {
						line.Clear().Append(attempt);
					}
				}
				result.Add(line.ToString());
			}
			return result;
		}

		private static void DrawMascot(Vector2 center) {
			var tex = Terraria.ModLoader.ModContent.Request<Microsoft.Xna.Framework.Graphics.Texture2D>("VanityStudio/Assets/Mascot").Value;
			float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f) * 2f;
			Gui.Sb.Draw(tex, center + new Vector2(0, bob), null, Color.White, 0f, tex.Size() / 2f, 0.55f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
		}
	}
}
