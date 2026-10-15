using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>Per-layer effects: glowing pixels, pulsing, a dye shader and particles.</summary>
	internal static class EffectsPopup
	{
		private static int dyeScroll;

		public static void Open() {
			StudioState.Popup = Popup.Effects;
			dyeScroll = 0;
		}

		public static void Draw(Design d) {
			DesignLayer l = d.Layers[StudioState.Layer];
			int w = 560, h = d.IsWearable ? 470 : 330;
			var r = new Rectangle(Main.screenWidth / 2 - w / 2, Main.screenHeight / 2 - h / 2, w, h);
			Gui.Rect(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * 0.35f);
			Gui.Panel(r, new Color(33, 43, 79), Gui.Gold * 0.8f);
			if (!Gui.Hover(r) && (Gui.LeftPressed || Gui.RightPressed)) {
				StudioState.Popup = Popup.None;
				Gui.LeftPressed = Gui.RightPressed = false;
				return;
			}

			int x = r.X + 16, y = r.Y + 14;
			Gui.Icon(Icons.Wand, new Vector2(x + 10, y + 12), Color.White);
			Gui.Text(L.Get("EffectsTitle", l.Name), new Vector2(x + 28, y + 13), Gui.Gold, 0.95f, 0f, 0.5f);
			if (Gui.IconButton(new Rectangle(r.Right - 42, r.Y + 10, 30, 30), Icons.Close, "fx-close", L.Get("TipClose"))) {
				StudioState.Popup = Popup.None;
				return;
			}
			y += 38;

			// glow / pulse
			bool glow = l.Glow, pulse = l.Pulse;
			string gl = L.Get("FxGlow"), pl = L.Get("FxPulse");
			var gb = new Rectangle(x, y, Gui.IconTextWidth(gl), 30);
			if (Gui.IconTextButton(gb, Icons.Star, gl, "fx-glow", glow, L.Get("TipFxGlow"))) {
				StudioState.Snapshot();
				l.Glow = !glow;
				StudioState.Changed();
			}
			var pb = new Rectangle(gb.Right + 6, y, Gui.IconTextWidth(pl), 30);
			if (Gui.IconTextButton(pb, Icons.Sparkles, pl, "fx-pulse", pulse, L.Get("TipFxPulse"))) {
				StudioState.Snapshot();
				l.Pulse = !pulse;
				StudioState.Changed();
			}
			Gui.Text(L.Get("FxGlowHint"), new Vector2(pb.Right + 12, y + 15), Gui.Muted, 0.68f, 0f, 0.5f);
			y += 42;

			// dye shader
			int dyeItem = Targets.ResolveItem(l.Dye);
			Gui.Icon(Icons.Drop, new Vector2(x + 10, y + 10), Color.White, 0.9f);
			string dyeName = dyeItem > 0 ? Lang.GetItemNameValue(dyeItem) : L.Get("FxNoDye");
			Gui.Text(L.Get("FxDye") + ": " + dyeName, new Vector2(x + 26, y + 11), Color.White, 0.8f, 0f, 0.5f);
			if (dyeItem > 0 && Gui.Button(new Rectangle(r.Right - 16 - 90, y - 2, 90, 26), L.Get("FxRemove"), "fx-nodye")) {
				StudioState.Snapshot();
				l.Dye = "";
				StudioState.Changed();
			}
			y += 26;
			List<int> dyes = Targets.Dyes;
			const int cell = 32;
			int cols = (w - 32) / (cell + 2);
			int rowsVisible = 3;
			var grid = new Rectangle(x, y, cols * (cell + 2), rowsVisible * (cell + 2));
			int totalRows = (dyes.Count + cols - 1) / cols;
			if (Gui.Hover(grid) && Gui.Scroll != 0) {
				dyeScroll = Math.Clamp(dyeScroll - Math.Sign(Gui.Scroll), 0, Math.Max(0, totalRows - rowsVisible));
				Gui.Scroll = 0;
			}
			for (int i = dyeScroll * cols; i < dyes.Count && i < (dyeScroll + rowsVisible) * cols; i++) {
				int type = dyes[i];
				int k = i - dyeScroll * cols;
				var c = new Rectangle(grid.X + k % cols * (cell + 2), grid.Y + k / cols * (cell + 2), cell, cell);
				bool sel = type == dyeItem;
				bool hover = Gui.Free && Gui.Hover(c);
				Gui.Slot(c, sel, hover);
				DrawItemIcon(type, c, 0.8f);
				if (hover) {
					Gui.Tip = Lang.GetItemNameValue(type);
					if (Gui.LeftPressed) {
						Gui.LeftPressed = false;
						StudioState.Snapshot();
						l.Dye = Targets.Key(DesignKind.Item, type);
						StudioState.Changed();
					}
				}
			}
			if (totalRows > rowsVisible) {
				float frac = dyeScroll / (float)Math.Max(1, totalRows - rowsVisible);
				int bh = Math.Max(16, grid.Height * rowsVisible / totalRows);
				Gui.Rect(new Rectangle(grid.Right + 4, grid.Y + (int)((grid.Height - bh) * frac), 4, bh), Color.White * 0.35f);
			}
			y = grid.Bottom + 6;
			Gui.Text(d.IsWearable ? L.Get("FxDyeHint") : L.Get("FxDyeHintSprite"), new Vector2(x, y + 8), Gui.Muted, 0.66f, 0f, 0.5f);
			y += 26;

			if (!d.IsWearable)
				return;

			// particles
			Gui.Icon(Icons.Sparkles, new Vector2(x + 10, y + 10), Color.White, 0.9f);
			Gui.Text(L.Get("FxParticles"), new Vector2(x + 26, y + 11), Color.White, 0.8f, 0f, 0.5f);
			y += 24;
			int px = x;
			var presets = Targets.DustPresets;
			bool none = l.Dust < 0 || l.DustRate <= 0f;
			string noneLabel = L.Get("FxNone");
			int nw = (int)Gui.Measure(noneLabel, 0.72f).X + 22;
			if (Gui.Button(new Rectangle(px, y, nw, 26), noneLabel, "fx-dust-none", none)) {
				StudioState.Snapshot();
				l.Dust = -1;
				StudioState.Changed();
			}
			px += nw + 4;
			foreach (var (dust, key) in presets) {
				string label = L.Get(key);
				int bw = (int)Gui.Measure(label, 0.72f).X + 22;
				if (px + bw > r.Right - 16) {
					px = x;
					y += 30;
				}
				if (Gui.Button(new Rectangle(px, y, bw, 26), label, "fx-dust" + dust, !none && l.Dust == dust)) {
					StudioState.Snapshot();
					l.Dust = dust;
					if (l.DustRate <= 0f)
						l.DustRate = 0.15f;
					StudioState.Changed();
				}
				px += bw + 4;
			}
			y += 36;
			if (!none) {
				Gui.Text(L.Get("FxRate"), new Vector2(x, y + Gui.BarHeight / 2 + 2), Gui.Muted, 0.72f, 0f, 0.5f);
				float rate = l.DustRate;
				float lw = Gui.Measure(L.Get("FxRate"), 0.72f).X + 14;
				if (Gui.ColorBar(new Vector2(x + lw, y), ref rate, "fx-rate", t => Color.Lerp(new Color(40, 40, 52), Gui.Gold, t))) {
					StudioState.SnapshotOnce("fx-rate");
					l.DustRate = Math.Max(0.02f, (float)Math.Round(rate, 2));
					StudioState.Changed();
				}
			}
		}

		public static void DrawItemIcon(int type, Rectangle cell, float maxScale) {
			Main.instance.LoadItem(type);
			var asset = TextureAssets.Item[type];
			if (!asset.IsLoaded)
				return;
			Texture2D tex = asset.Value;
			Rectangle frame = Main.itemAnimations[type] != null ? Main.itemAnimations[type].GetFrame(tex) : tex.Frame();
			float s = Math.Min(maxScale, Math.Min((cell.Width - 8f) / frame.Width, (cell.Height - 8f) / frame.Height));
			if (s > 1f)
				s = (float)Math.Floor(s);
			Gui.Sb.Draw(tex, cell.Center.ToVector2(), frame, Color.White, 0f, frame.Size() / 2f, s, SpriteEffects.None, 0f);
		}
	}
}
