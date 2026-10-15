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
		public static void ResetFilter() => filter = -1;
		private static uint armedAt;

		/// <summary>"My designs" / "Studio Workshop" switch, shared by both views.</summary>
		public static void DrawTabs(ref int x, int y) {
			string mine = L.Get("TabMine", DesignLibrary.All.Count), ws = L.Get("TabWorkshop");
			var a = new Rectangle(x, y, Gui.IconTextWidth(mine), 30);
			if (Gui.IconTextButton(a, Icons.Library, mine, "tab-mine", !StudioState.WorkshopTab))
				StudioState.WorkshopTab = false;
			var b = new Rectangle(a.Right + 4, y, Gui.IconTextWidth(ws), 30);
			if (Gui.IconTextButton(b, Icons.Globe, ws, "tab-ws", StudioState.WorkshopTab, L.Get("TipWorkshop")))
				StudioState.WorkshopTab = true;
			x = b.Right + 12;
		}

		// filter values: -1 all, 0..4 wearable slots, 10 + DesignKind for retextures
		private static bool Matches(Design d, int f) => f < 0 || (f < 10 ? d.IsWearable && (int)d.Slot == f : !d.IsWearable && (int)d.Kind == f - 10);

		public static void Draw(Rectangle area) {
			Gui.Panel(area);
			var all = DesignLibrary.Newest.Where(d => Matches(d, filter)).ToList();

			int x = area.X + 10, y = area.Y + 10;
			DrawTabs(ref x, y);

			int bx = area.Right - 10;
			Rectangle Btn(string label) {
				int w = Gui.IconTextWidth(label);
				bx -= w;
				var r = new Rectangle(bx, y, w, 30);
				bx -= 6;
				return r;
			}
			if (Gui.IconTextButton(Btn(L.Get("OpenFolder")), Icons.Folder, L.Get("OpenFolder"), "lib-folder")) {
				System.IO.Directory.CreateDirectory(DesignLibrary.Root);
				Utils.OpenFolder(DesignLibrary.Root);
			}
			if (Gui.IconTextButton(Btn(L.Get("ImportFile")), Icons.Import, L.Get("ImportFile"), "lib-import", false, L.Get("TipImport")))
				StudioWindow.ImportFile();

			y += 40;
			// new: something to wear (five slots) or a new look for something in the game
			Gui.Text(L.Get("NewWear"), new Vector2(x, y + 15), Gui.Muted, 0.78f, 0f, 0.5f);
			int nx = x + (int)Gui.Measure(L.Get("NewWear"), 0.78f).X + 10;
			for (int i = 0; i < Slots.Count; i++) {
				var s = (DesignSlot)i;
				if (Gui.IconButton(new Rectangle(nx, y, 32, 30), Icons.ForSlot(s), "new" + i, L.Get("TipNewFor", L.Slot(s)))) {
					StudioState.NewDesign(s);
					StudioState.LibraryOpen = false;
					return;
				}
				nx += 34;
			}
			nx += 16;
			Gui.Text(L.Get("NewRetexture"), new Vector2(nx, y + 15), Gui.Muted, 0.78f, 0f, 0.5f);
			nx += (int)Gui.Measure(L.Get("NewRetexture"), 0.78f).X + 10;
			foreach (var (kind, key) in new[] { (DesignKind.Item, "KindItem"), (DesignKind.NPC, "KindNPCBoss"), (DesignKind.Projectile, "KindProjectile") }) {
				string label = L.Get(key);
				var r = new Rectangle(nx, y, Gui.IconTextWidth(label, 0.75f), 30);
				if (Gui.IconTextButton(r, Icons.ForKind(kind), label, "newr" + kind, false, L.Get("TipNewRetexture"), true, 0.75f)) {
					TargetPicker.Open(kind);
					return;
				}
				nx = r.Right + 4;
			}

			y += 40;
			int fx = x;
			void Filter(int value, int icon, string tip) {
				int count = DesignLibrary.All.Count(d => Matches(d, value));
				if (Gui.IconButton(new Rectangle(fx, y, 32, 30), icon, "filter" + value, tip + " (" + count + ")", filter == value)) {
					filter = value;
					scroll = 0;
				}
				fx += 34;
			}
			Gui.Text(L.Get("Show"), new Vector2(fx, y + 15), Gui.Muted, 0.78f, 0f, 0.5f);
			fx += (int)Gui.Measure(L.Get("Show"), 0.78f).X + 10;
			Filter(-1, Icons.Library, L.Get("FilterAll"));
			for (int i = 0; i < Slots.Count; i++)
				Filter(i, Icons.ForSlot((DesignSlot)i), L.Slot((DesignSlot)i));
			Filter(10 + (int)DesignKind.Item, Icons.Sword, L.Get("KindItem"));
			Filter(10 + (int)DesignKind.NPC, Icons.Slime, L.Get("KindNPCBoss"));
			Filter(10 + (int)DesignKind.Projectile, Icons.Projectile, L.Get("KindProjectile"));

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
			bool equipped = StudioState.IsApplied(d);
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
			Gui.Icon(Icons.ForDesign(d), new Vector2(thumbArea.X + 11, thumbArea.Y + 11), Color.White, 0.8f);
			if (equipped)
				Gui.Icon(d.IsWearable ? Icons.Shirt : Icons.Check, new Vector2(thumbArea.Right - 10, thumbArea.Y + 10), Gui.Gold, 0.8f);

			Gui.Text(Gui.Fit(d.Name, card.Width - 16, 0.8f), new Vector2(card.X + 8, thumbArea.Bottom + 12), Color.White, 0.8f, 0f, 0.5f);
			string info = (d.IsWearable ? L.Slot(d.Slot) : string.IsNullOrEmpty(d.BasedOn) ? L.Get("Kind" + d.Kind) : d.BasedOn)
				+ (d.FrameCount > 1 ? " · " + L.Get("FramesN", d.FrameCount) : "")
				+ (string.IsNullOrEmpty(d.AuthorName) ? "" : " · " + d.AuthorName);
			Gui.Text(info, new Vector2(card.X + 8, thumbArea.Bottom + 28), Gui.Muted, 0.7f, 0f, 0.5f);

			if (!hover)
				return false;

			int by = card.Bottom - 32;
			var equipBtn = new Rectangle(card.X + 8, by, 28, 28);
			var dupBtn = new Rectangle(card.X + 40, by, 28, 28);
			var delBtn = new Rectangle(card.Right - 36, by, 28, 28);
			bool armed = armedDelete == d.Id && Main.GameUpdateCount - armedAt < 180;

			string equipTip = d.IsWearable ? (equipped ? L.Get("TipUnequip") : L.Get("TipEquip")) : (equipped ? L.Get("TipUnapply") : L.Get("TipApply"));
			if (Gui.IconButton(equipBtn, d.IsWearable ? Icons.Shirt : Icons.Check, "lib-equip" + d.Id, equipTip, equipped)) {
				if (!d.IsWearable) {
					Retextures.SetEnabled(d, !equipped);
					SoundEngine.PlaySound(equipped ? SoundID.Grab : SoundID.Item4);
					return false;
				}
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
			Retextures.Forget(d.Id);
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
			Color[] px = d.IsWearable ? d.Flatten(0, Design.BasePose) : d.Flatten(0, 0);
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
