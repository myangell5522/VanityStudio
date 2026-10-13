using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>Grid of every saved design, shared by all characters.</summary>
	internal static class LibraryView
	{
		private const int CardW = 136, CardH = 184, Gap = 10;

		private static readonly Dictionary<Guid, (int rev, Texture2D tex)> thumbs = new();
		private static int scroll;
		private static Guid armedDelete;
		private static int filter = -1;
		private static uint armedAt;

		public static void Draw(Rectangle area) {
			Gui.Panel(area);
			var all = DesignLibrary.Newest.Where(d => filter < 0 || (int)d.Slot == filter).ToList();

			int x = area.X + 10, y = area.Y + 10;
			Gui.Text(L.Get("LibraryTitle", all.Count), new Vector2(x, y + 15), Color.White, 0.9f, 0f, 0.5f);

			int bx = area.Right - 10;
			Rectangle Btn(string label) {
				int w = (int)Gui.Measure(label).X + 26;
				bx -= w;
				var r = new Rectangle(bx, y, w, 30);
				bx -= 6;
				return r;
			}
			if (Gui.Button(Btn(L.Get("OpenFolder")), L.Get("OpenFolder"), "lib-folder")) {
				System.IO.Directory.CreateDirectory(DesignLibrary.Root);
				Utils.OpenFolder(DesignLibrary.Root);
			}
			if (Gui.Button(Btn(L.Get("ImportFile")), L.Get("ImportFile"), "lib-import", false, true, L.Get("TipImport")))
				StudioWindow.ImportFile();

			y += 40;
			Gui.Text(L.Get("NewFor"), new Vector2(x, y + 15), Gui.Muted, 0.8f, 0f, 0.5f);
			int nx = x + (int)Gui.Measure(L.Get("NewFor")).X + 12;
			for (int i = 0; i < Slots.Count; i++) {
				var s = (DesignSlot)i;
				int nw = (int)Gui.Measure("+ " + L.Slot(s)).X + 26;
				var r = new Rectangle(nx, y, nw, 30);
				if (Gui.Button(r, "+ " + L.Slot(s), "new" + i, false, true, L.Get("TipNewFor", L.Slot(s)))) {
					StudioState.NewDesign(s);
					StudioState.LibraryOpen = false;
					return;
				}
				nx += nw + 6;
			}

			y += 40;
			Gui.Text(L.Get("Show"), new Vector2(x, y + 15), Gui.Muted, 0.8f, 0f, 0.5f);
			int fx = x + (int)Gui.Measure(L.Get("Show")).X + 12;
			for (int i = -1; i < Slots.Count; i++) {
				string label = i < 0 ? L.Get("FilterAll") : L.Slot((DesignSlot)i);
				int fw = (int)Gui.Measure(label).X + 24;
				int count = DesignLibrary.All.Count(d => i < 0 || (int)d.Slot == i);
				if (Gui.Button(new Rectangle(fx, y, fw, 30), label, "filter" + i, filter == i, true, count.ToString()))
					filter = i;
				fx += fw + 4;
			}

			y += 42;
			var grid = new Rectangle(area.X + 8, y, area.Width - 16, area.Bottom - 8 - y);
			int cols = Math.Max(1, (grid.Width + Gap) / (CardW + Gap));
			int rows = (all.Count + cols - 1) / cols;
			int contentH = rows * (CardH + Gap);
			int maxScroll = Math.Max(0, contentH - grid.Height);
			if (Gui.Hover(grid) && Gui.Scroll != 0) {
				scroll -= Math.Sign(Gui.Scroll) * 60;
				Gui.Scroll = 0;
			}
			scroll = Math.Clamp(scroll, 0, maxScroll);

			if (all.Count == 0)
				Gui.Text(L.Get("LibraryEmpty"), grid.Center.ToVector2(), Gui.Muted, 0.9f, 0.5f, 0.5f);

			int built = 0;
			Gui.BeginClip(grid);
			Gui.InputClip = grid;
			int offsetX = grid.X + (grid.Width - (cols * (CardW + Gap) - Gap)) / 2;
			for (int i = 0; i < all.Count; i++) {
				var card = new Rectangle(offsetX + i % cols * (CardW + Gap), grid.Y + i / cols * (CardH + Gap) - scroll, CardW, CardH);
				if (card.Bottom < grid.Y || card.Y > grid.Bottom)
					continue;
				if (DrawCard(card, all[i], ref built))
					break;
			}
			Gui.InputClip = null;
			Gui.EndClip();

			if (maxScroll > 0) {
				float frac = scroll / (float)maxScroll;
				int barH = Math.Max(30, grid.Height * grid.Height / Math.Max(1, contentH));
				Gui.Rect(new Rectangle(grid.Right - 4, grid.Y + (int)((grid.Height - barH) * frac), 4, barH), Color.White * 0.35f);
			}
		}

		// returns true when the list changed under us and drawing should stop for this frame
		private static bool DrawCard(Rectangle card, Design d, ref int built) {
			bool current = d == StudioState.Doc;
			bool equipped = StudioState.Local.IsEquipped(d);
			bool hover = Gui.Free && Gui.Hover(card);
			Gui.Panel(card, hover ? Gui.ButtonHover * 0.9f : Gui.PanelBlue * 0.55f, current ? Gui.Gold : hover ? Gui.Gold * 0.6f : Color.Black);

			var thumbArea = new Rectangle(card.X + 8, card.Y + 8, card.Width - 16, 118);
			Gui.Checker(thumbArea, 6);
			Texture2D tex = Thumb(d, ref built);
			if (tex != null) {
				int scale = Math.Max(1, Math.Min(thumbArea.Width / tex.Width, thumbArea.Height / tex.Height));
				var dst = new Rectangle(thumbArea.Center.X - tex.Width * scale / 2, thumbArea.Center.Y - tex.Height * scale / 2, tex.Width * scale, tex.Height * scale);
				Gui.Sb.Draw(tex, dst, Color.White);
			}
			if (equipped)
				Gui.Icon(Icons.Shirt, new Vector2(thumbArea.Right - 10, thumbArea.Y + 10), Gui.Gold, 0.8f);

			Gui.Text(Gui.Fit(d.Name, card.Width - 16, 0.8f), new Vector2(card.X + 8, thumbArea.Bottom + 12), Color.White, 0.8f, 0f, 0.5f);
			string info = L.Slot(d.Slot) + (d.FrameCount > 1 ? " · " + L.Get("FramesN", d.FrameCount) : "");
			Gui.Text(info, new Vector2(card.X + 8, thumbArea.Bottom + 28), Gui.Muted, 0.7f, 0f, 0.5f);

			if (!hover)
				return false;

			int by = card.Bottom - 32;
			var equipBtn = new Rectangle(card.X + 8, by, 28, 28);
			var dupBtn = new Rectangle(card.X + 40, by, 28, 28);
			var delBtn = new Rectangle(card.Right - 36, by, 28, 28);
			bool armed = armedDelete == d.Id && Main.GameUpdateCount - armedAt < 180;

			if (Gui.IconButton(equipBtn, Icons.Shirt, "lib-equip" + d.Id, equipped ? L.Get("TipUnequip") : L.Get("TipEquip"), equipped)) {
				var vp = StudioState.Local;
				if (equipped)
					vp.Unequip(d.Slot);
				else {
					vp.Equip(d);
					SoundEngine.PlaySound(SoundID.Item64 with { Volume = 0.6f });
				}
				return false;
			}
			if (Gui.IconButton(dupBtn, Icons.Copy, "lib-dup" + d.Id, L.Get("TipDuplicate"))) {
				Design copy = d.Duplicate(DesignLibrary.UniqueName(d.Name));
				DesignLibrary.Add(copy);
				return true;
			}
			if (Gui.IconButton(delBtn, Icons.Trash, "lib-del" + d.Id, armed ? L.Get("TipDeleteConfirm") : L.Get("TipDelete"), armed)) {
				if (!armed) {
					armedDelete = d.Id;
					armedAt = Main.GameUpdateCount;
					return false;
				}
				armedDelete = Guid.Empty;
				Delete(d);
				return true;
			}

			if (Gui.Hover(equipBtn) || Gui.Hover(dupBtn) || Gui.Hover(delBtn))
				return false;
			Gui.MarkHover("lib-card" + d.Id, L.Get("TipOpen"));
			if (Gui.LeftPressed) {
				Gui.LeftPressed = false;
				StudioState.Load(d);
				StudioState.LibraryOpen = false;
				SoundEngine.PlaySound(SoundID.MenuTick);
				return true;
			}
			return false;
		}

		private static void Delete(Design d) {
			StudioState.Local.Forget(d.Id);
			// the open design must be dropped first, or switching away would autosave it straight back to disk
			if (StudioState.Doc == d)
				StudioState.Forget();
			DesignLibrary.Delete(d);
			// the thumbnail is still queued in this frame's sprite batch, so it can only go away after the frame is drawn
			if (thumbs.Remove(d.Id, out var t) && t.tex != null)
				Main.QueueMainThreadAction(t.tex.Dispose);
			SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.4f });
			if (StudioState.Doc == null) {
				Design next = DesignLibrary.Newest.FirstOrDefault();
				if (next != null)
					StudioState.Load(next);
				else
					StudioState.NewDesign(DesignSlot.Body);
			}
		}

		private static Texture2D Thumb(Design d, ref int built) {
			if (thumbs.TryGetValue(d.Id, out var t) && (t.rev == d.Revision || built >= 3) && t.tex != null && !t.tex.IsDisposed)
				return t.tex;
			if (built >= 3)
				return null;
			built++;
			Color[] px = d.Flatten(0, Design.BasePose);
			Texture2D tex = t.tex;
			if (tex == null || tex.IsDisposed || tex.Width != d.Width || tex.Height != d.Height) {
				if (tex != null && !tex.IsDisposed)
					Main.QueueMainThreadAction(tex.Dispose);
				tex = new Texture2D(Main.instance.GraphicsDevice, d.Width, d.Height);
			}
			tex.SetData(Pixels.Premultiply(px));
			thumbs[d.Id] = (d.Revision, tex);
			return tex;
		}

		public static void Unload() {
			foreach (var t in thumbs.Values)
				t.tex?.Dispose();
			thumbs.Clear();
		}
	}
}
