using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	internal static class ColorPanel
	{
		// Resurrect 64 by Kerrie Lake
		private static readonly uint[] resurrect64 = {
			0x2e222f, 0x3e3546, 0x625565, 0x966c6c, 0xab947a, 0x694f62, 0x7f708a, 0x9babb2,
			0xc7dcd0, 0xffffff, 0x6e2727, 0xb33831, 0xea4f36, 0xf57d4a, 0xae2334, 0xe83b3b,
			0xfb6b1d, 0xf79617, 0xf9c22b, 0x7a3045, 0x9e4539, 0xcd683d, 0xe6904e, 0xfbb954,
			0x4c3e24, 0x676633, 0xa2a947, 0xd5e04b, 0xfbff86, 0x165a4c, 0x239063, 0x1ebc73,
			0x91db69, 0xcddf6c, 0x313638, 0x374e4a, 0x547e64, 0x92a984, 0xb2ba90, 0x0b5e65,
			0x0b8a8f, 0x0eaf9b, 0x30e1b9, 0x8ff8e2, 0x323353, 0x484a77, 0x4d65b4, 0x4d9be6,
			0x8fd3ff, 0x45293f, 0x6b3e75, 0x905ea9, 0xa884f3, 0xeaaded, 0x753c54, 0xa24b6f,
			0xcf657f, 0xed8099, 0x831c5d, 0xc32454, 0xf04f78, 0xf68181, 0xfca790, 0xfdcbb0
		};

		private static Color[] palette;
		private static float hue, sat = 1f, lum = 0.5f, alpha = 1f;
		private static string hexText;
		private static Color syncedFor;

		public static Color[] Palette {
			get {
				if (palette == null) {
					palette = new Color[resurrect64.Length];
					for (int i = 0; i < palette.Length; i++) {
						uint v = resurrect64[i];
						palette[i] = new Color((int)(v >> 16 & 0xFF), (int)(v >> 8 & 0xFF), (int)(v & 0xFF));
					}
				}
				return palette;
			}
		}

		public static void SyncFromPrimary() {
			Color c = StudioState.Primary;
			Vector3 hsl = Main.rgbToHsl(c);
			hue = hsl.X;
			sat = hsl.Y;
			lum = hsl.Z;
			alpha = c.A / 255f;
			hexText = Pixels.ToHex(c);
			syncedFor = c;
		}

		private static void SetPrimary(Color c) {
			StudioState.Primary = c;
			SyncFromPrimary();
		}

		public static int Draw(Rectangle area) {
			if (syncedFor != StudioState.Primary || hexText == null)
				SyncFromPrimary();

			Gui.Panel(area);
			int x = area.X + 10, y = area.Y + 10;

			// primary on top of secondary, like most paint programs
			var second = new Rectangle(x + 18, y + 18, 30, 30);
			var first = new Rectangle(x, y, 30, 30);
			DrawSwatch(second, StudioState.Secondary, false);
			DrawSwatch(first, StudioState.Primary, true);
			if (Gui.Free && Gui.Hover(new Rectangle(x, y, 48, 48))) {
				Gui.Tip = L.Get("TipSwapColors");
				if (Gui.LeftPressed) {
					(StudioState.Primary, StudioState.Secondary) = (StudioState.Secondary, StudioState.Primary);
					SyncFromPrimary();
					Gui.LeftPressed = false;
				}
			}

			Gui.Text("#", new Vector2(x + 58, y + 12), Gui.Muted, 0.8f, 0f, 0.5f);
			if (Gui.TextBox(new Rectangle(x + 70, y, 110, 26), "hex", ref hexText, 8, null, Uri.IsHexDigit)) {
				if (Pixels.TryParseHex(hexText, out Color parsed))
					SetPrimary(parsed);
				else
					hexText = Pixels.ToHex(StudioState.Primary);
			}
			if (Gui.Button(new Rectangle(x + 186, y, 70, 26), L.Get("Clear"), "transparent", StudioState.Primary.A == 0, true, L.Get("TipTransparent")))
				SetPrimary(Color.Transparent);
			Gui.Text(L.Get("RightClickHint"), new Vector2(x + 58, y + 40), Gui.Muted, 0.65f, 0f, 0.5f);

			y += 58;
			bool changed = false;
			changed |= Bar(ref y, x, area, "H", ref hue, "hue", null);
			float h = hue, s = sat, l = lum;
			changed |= Bar(ref y, x, area, "S", ref sat, "sat", t => Main.hslToRgb(h, t, l));
			changed |= Bar(ref y, x, area, "L", ref lum, "lum", t => Main.hslToRgb(h, s, t));
			Color solid = Main.hslToRgb(h, s, l);
			changed |= Bar(ref y, x, area, "A", ref alpha, "alpha", t => Color.Lerp(new Color(50, 50, 60), solid, t));
			if (changed) {
				Color c = Main.hslToRgb(hue, sat, lum);
				c.A = (byte)Math.Round(alpha * 255f);
				if (c.A == 0)
					c = Color.Transparent;
				StudioState.Primary = c;
				syncedFor = c;
				hexText = Pixels.ToHex(c);
			}
			return area.Bottom;
		}

		private static bool Bar(ref int y, int x, Rectangle area, string label, ref float value, string id, Func<float, Color> gradient) {
			Gui.Text(label, new Vector2(x + 4, y + Gui.BarHeight / 2 + 2), Gui.Muted, 0.75f, 0f, 0.5f);
			bool changed = Gui.ColorBar(new Vector2(x + 22, y), ref value, id, gradient);
			Gui.Text(((int)Math.Round(value * 100)).ToString(), new Vector2(x + 30 + Gui.BarWidth, y + Gui.BarHeight / 2 + 2), Color.White, 0.7f, 0f, 0.5f);
			y += Gui.BarHeight + 10;
			return changed;
		}

		private static void DrawSwatch(Rectangle r, Color c, bool front) {
			Gui.Rect(new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4), Color.Black);
			Gui.Checker(r, 5);
			Gui.Rect(r, c);
			Gui.Outline(r, front ? Color.White : Gui.Muted);
		}

		/// <summary>Palette grid + the character's own colours + recently used. Returns the bottom edge.</summary>
		public static int DrawPalettes(Rectangle area) {
			Gui.Panel(area);
			int x = area.X + 10, y = area.Y + 8;
			int cell = Math.Min(16, (area.Width - 20) / 16);

			Gui.Text("Resurrect 64", new Vector2(x, y + 8), Gui.Muted, 0.7f, 0f, 0.5f);
			y += 18;
			Color[] pal = Palette;
			for (int i = 0; i < pal.Length; i++)
				Swatch(new Rectangle(x + i % 16 * cell, y + i / 16 * cell, cell, cell), pal[i], "pal" + i);
			y += (pal.Length + 15) / 16 * cell + 6;

			Gui.Text(L.Get("CharacterColors"), new Vector2(x, y + 8), Gui.Muted, 0.7f, 0f, 0.5f);
			y += 18;
			Player p = Main.LocalPlayer;
			var mine = new List<Color> { p.skinColor, p.hairColor, p.eyeColor, p.shirtColor, p.underShirtColor, p.pantsColor, p.shoeColor };
			// a darker and lighter step of skin is what you end up wanting for shading anyway
			mine.Add(Color.Lerp(p.skinColor, Color.Black, 0.25f));
			mine.Add(Color.Lerp(p.skinColor, Color.White, 0.25f));
			for (int i = 0; i < mine.Count; i++)
				Swatch(new Rectangle(x + i * cell, y, cell, cell), mine[i], "mine" + i);
			y += cell + 6;

			Gui.Text(L.Get("Recent"), new Vector2(x, y + 8), Gui.Muted, 0.7f, 0f, 0.5f);
			y += 18;
			var recent = StudioSettings.RecentColors;
			for (int i = 0; i < 16; i++) {
				var r = new Rectangle(x + i * cell, y, cell, cell);
				if (i < recent.Count)
					Swatch(r, recent[i], "recent" + i);
				else
					Gui.Rect(r, Color.Black * 0.25f);
			}
			return y + cell;
		}

		private static void Swatch(Rectangle r, Color c, string id) {
			Gui.Rect(r, c);
			bool hover = Gui.Free && Gui.Hover(r);
			if (c == StudioState.Primary)
				Gui.Outline(r, Color.White);
			else if (c == StudioState.Secondary)
				Gui.Outline(r, Gui.Muted);
			if (!hover)
				return;
			Gui.Outline(r, Gui.Gold);
			Gui.Tip = "#" + Pixels.ToHex(c);
			if (Gui.LeftPressed) {
				SetPrimary(c);
				Gui.LeftPressed = false;
			}
			else if (Gui.RightPressed) {
				StudioState.Secondary = c;
				Gui.RightPressed = false;
			}
		}
	}
}
