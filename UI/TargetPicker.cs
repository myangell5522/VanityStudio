using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>Pick what to retexture: a searchable grid of every item, NPC, boss or projectile, sprites included.</summary>
	internal static class TargetPicker
	{
		private enum Tab
		{
			Items,
			Npcs,
			Bosses,
			Projectiles
		}

		private static Tab tab;
		private static string search = "";
		private static string lastSearch;
		private static Tab lastTab = (Tab)(-1);
		private static readonly List<int> results = new();
		private static readonly Dictionary<Tab, List<(int type, string name)>> names = new();
		private static int scroll;

		public static void Open(DesignKind kind) {
			tab = kind switch {
				DesignKind.NPC => Tab.Npcs,
				DesignKind.Projectile => Tab.Projectiles,
				_ => Tab.Items
			};
			search = "";
			lastSearch = null;
			scroll = 0;
			StudioState.Popup = Popup.Target;
		}

		private static DesignKind KindOf(Tab t) => t switch {
			Tab.Items => DesignKind.Item,
			Tab.Projectiles => DesignKind.Projectile,
			_ => DesignKind.NPC
		};

		private static List<(int, string)> Names(Tab t) {
			if (names.TryGetValue(t, out var list))
				return list;
			list = new List<(int, string)>();
			DesignKind kind = KindOf(t);
			int count = Targets.Count(kind);
			for (int i = 1; i < count; i++) {
				if (t == Tab.Bosses && !Targets.IsBoss(i))
					continue;
				string n = Targets.DisplayName(kind, i);
				if (string.IsNullOrWhiteSpace(n) || n.StartsWith("ItemName.") || n.StartsWith("NPCName.") || n.StartsWith("ProjectileName."))
					continue;
				list.Add((i, n));
			}
			names[t] = list;
			return list;
		}

		public static void Unload() => names.Clear();

		public static void Draw() {
			int w = Math.Min(Main.screenWidth - 80, 760), h = Math.Min(Main.screenHeight - 80, 560);
			var r = new Rectangle(Main.screenWidth / 2 - w / 2, Main.screenHeight / 2 - h / 2, w, h);
			Gui.Rect(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * 0.4f);
			Gui.Panel(r, new Color(33, 43, 79), Gui.Gold * 0.8f);
			if (!Gui.Hover(r) && (Gui.LeftPressed || Gui.RightPressed)) {
				StudioState.Popup = Popup.None;
				Gui.LeftPressed = Gui.RightPressed = false;
				return;
			}

			int x = r.X + 16, y = r.Y + 14;
			Gui.Text(L.Get("PickTitle"), new Vector2(x, y + 13), Gui.Gold, 0.95f, 0f, 0.5f);
			if (Gui.IconButton(new Rectangle(r.Right - 42, r.Y + 10, 30, 30), Icons.Close, "pick-close", L.Get("TipClose"))) {
				StudioState.Popup = Popup.None;
				return;
			}
			y += 36;

			// tabs
			(Tab t, int icon, string key)[] tabs = {
				(Tab.Items, Icons.Sword, "KindItem"), (Tab.Npcs, Icons.Slime, "KindNPC"),
				(Tab.Bosses, Icons.Boss, "KindBoss"), (Tab.Projectiles, Icons.Projectile, "KindProjectile")
			};
			int tx = x;
			foreach (var (t, icon, key) in tabs) {
				string label = L.Get(key);
				var b = new Rectangle(tx, y, Gui.IconTextWidth(label), 30);
				if (Gui.IconTextButton(b, icon, label, "pick-tab" + t, tab == t)) {
					tab = t;
					scroll = 0;
				}
				tx = b.Right + 4;
			}
			// quick picks: what's in your hand or on the cursor
			if (tab == Tab.Items) {
				Item held = Main.mouseItem != null && !Main.mouseItem.IsAir ? Main.mouseItem : Main.LocalPlayer.HeldItem;
				if (held != null && !held.IsAir) {
					string label = L.Get("PickHeld", held.Name);
					var b = new Rectangle(r.Right - 16 - Math.Min(Gui.IconTextWidth(label), 240), y, Math.Min(Gui.IconTextWidth(label), 240), 30);
					if (Gui.IconTextButton(b, Icons.Sword, label, "pick-held")) {
						Pick(DesignKind.Item, held.type);
						return;
					}
				}
			}
			y += 38;

			Gui.Icon(Icons.Search, new Vector2(x + 10, y + 15), Color.White, 0.9f);
			Gui.TextBox(new Rectangle(x + 26, y, w - 58, 30), "pick-search", ref search, 40, L.Get("PickSearch"));
			y += 38;

			if (tab != lastTab || search != lastSearch) {
				lastTab = tab;
				lastSearch = search;
				results.Clear();
				string q = search.Trim();
				foreach (var (type, name) in Names(tab)) {
					if (q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase) || Targets.Key(KindOf(tab), type).Contains(q, StringComparison.OrdinalIgnoreCase))
						results.Add(type);
				}
				scroll = 0;
			}

			const int cell = 52;
			var grid = new Rectangle(x, y, w - 32, r.Bottom - 34 - y);
			int cols = Math.Max(1, grid.Width / (cell + 4));
			int rowsVisible = Math.Max(1, grid.Height / (cell + 4));
			int totalRows = (results.Count + cols - 1) / cols;
			if (Gui.Hover(grid) && Gui.Scroll != 0) {
				scroll = Math.Clamp(scroll - Math.Sign(Gui.Scroll) * 2, 0, Math.Max(0, totalRows - rowsVisible));
				Gui.Scroll = 0;
			}
			DesignKind kind = KindOf(tab);
			for (int i = scroll * cols; i < results.Count && i < (scroll + rowsVisible) * cols; i++) {
				int k = i - scroll * cols;
				var c = new Rectangle(grid.X + k % cols * (cell + 4), grid.Y + k / cols * (cell + 4), cell, cell);
				int type = results[i];
				bool hover = Gui.Free && Gui.Hover(c);
				Gui.Slot(c, false, hover);
				DrawSprite(kind, type, c);
				if (!hover)
					continue;
				Gui.Tip = Targets.DisplayName(kind, type) + "\n" + Targets.Key(kind, type);
				if (Gui.LeftPressed) {
					Gui.LeftPressed = false;
					Pick(kind, type);
					return;
				}
			}
			Gui.Text(L.Get("PickCount", results.Count) + "   " + L.Get("PickHint"), new Vector2(x, r.Bottom - 18), Gui.Muted, 0.68f, 0f, 0.5f);
		}

		private static void Pick(DesignKind kind, int type) {
			StudioState.Popup = Popup.None;
			StudioState.LibraryOpen = false;
			StudioState.NewRetexture(kind, type);
		}

		private static void DrawSprite(DesignKind kind, int type, Rectangle c) {
			Texture2D tex;
			Rectangle frame;
			switch (kind) {
				case DesignKind.Item:
					EffectsPopup.DrawItemIcon(type, c, 1.5f);
					return;
				case DesignKind.NPC:
					Main.instance.LoadNPC(type);
					if (!TextureAssets.Npc[type].IsLoaded)
						return;
					tex = TextureAssets.Npc[type].Value;
					frame = new Rectangle(0, 0, tex.Width, tex.Height / Math.Max(1, Main.npcFrameCount[type]));
					break;
				default:
					Main.instance.LoadProjectile(type);
					if (!TextureAssets.Projectile[type].IsLoaded)
						return;
					tex = TextureAssets.Projectile[type].Value;
					frame = new Rectangle(0, 0, tex.Width, tex.Height / Math.Max(1, Main.projFrames[type]));
					break;
			}
			float s = Math.Min(1.5f, Math.Min((c.Width - 8f) / frame.Width, (c.Height - 8f) / frame.Height));
			Gui.Sb.Draw(tex, c.Center.ToVector2(), frame, Color.White, 0f, frame.Size() / 2f, s, SpriteEffects.None, 0f);
		}
	}
}
