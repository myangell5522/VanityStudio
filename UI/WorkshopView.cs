using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>Everything people have shared, straight from the Studio Workshop repository.</summary>
	internal static class WorkshopView
	{
		private const int CardW = 156, CardH = 206, Gap = 10;
		private static int scroll;
		private static int filter = -1;   // -1 all, 0..4 wearable slots, 10+ kinds
		private static string search = "";

		public static void Draw(Rectangle area) {
			WorkshopClient.Refresh();
			Gui.Panel(area);
			int x = area.X + 10, y = area.Y + 10;

			LibraryView.DrawTabs(ref x, y);

			int bx = area.Right - 10;
			Rectangle Btn(string label, int icon) {
				int w = Gui.IconTextWidth(label);
				bx -= w;
				var r = new Rectangle(bx, y, w, 30);
				bx -= 6;
				return r;
			}
			if (Gui.IconTextButton(Btn(L.Get("WsOpenSite"), Icons.Globe), Icons.Globe, L.Get("WsOpenSite"), "ws-site"))
				Utils.OpenToURL(WorkshopClient.RepoUrl);
			if (Gui.IconTextButton(Btn(L.Get("WsRefresh"), Icons.Refresh), Icons.Refresh, L.Get("WsRefresh"), "ws-refresh", false, null, !WorkshopClient.Loading))
				WorkshopClient.Refresh(force: true);

			y += 40;
			// filters: one icon per kind of thing
			int fx = x;
			void Filter(int value, int icon, string tip) {
				var b = new Rectangle(fx, y, 32, 30);
				if (Gui.IconButton(b, icon, "ws-filter" + value, tip, filter == value)) {
					filter = value;
					scroll = 0;
				}
				fx += 34;
			}
			Filter(-1, Icons.Library, L.Get("FilterAll"));
			for (int i = 0; i < Slots.Count; i++)
				Filter(i, Icons.ForSlot((DesignSlot)i), L.Slot((DesignSlot)i));
			Filter(10 + (int)DesignKind.Item, Icons.Sword, L.Get("KindItem"));
			Filter(10 + (int)DesignKind.NPC, Icons.Slime, L.Get("KindNPC"));
			Filter(10 + (int)DesignKind.Projectile, Icons.Projectile, L.Get("KindProjectile"));
			fx += 8;
			Gui.Icon(Icons.Search, new Vector2(fx + 10, y + 15), Color.White, 0.9f);
			if (Gui.TextBox(new Rectangle(fx + 24, y, Math.Max(120, area.Right - 10 - fx - 24), 30), "ws-search", ref search, 30, L.Get("PickSearch")))
				scroll = 0;
			y += 40;

			var grid = new Rectangle(area.X + 8, y, area.Width - 16, area.Bottom - 8 - y);
			if (WorkshopClient.Loading && WorkshopClient.Entries.Count == 0) {
				Gui.Text(L.Get("WsLoading"), grid.Center.ToVector2(), Gui.Muted, 0.9f, 0.5f, 0.5f);
				return;
			}
			if (WorkshopClient.Error != null && WorkshopClient.Entries.Count == 0) {
				string msg = WorkshopClient.Error == "empty" ? L.Get("WsEmpty") : L.Get("WsError", WorkshopClient.Error);
				Gui.Text(msg, grid.Center.ToVector2(), Gui.Muted, 0.85f, 0.5f, 0.5f);
				return;
			}

			string q = search.Trim();
			var list = WorkshopClient.Entries.Where(e => {
				if (filter >= 10 && e.KindValue != (DesignKind)(filter - 10))
					return false;
				if (filter >= 0 && filter < 10 && (e.KindValue != DesignKind.Wearable || e.SlotValue != (DesignSlot)filter))
					return false;
				return q.Length == 0 || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || e.AuthorName.Contains(q, StringComparison.OrdinalIgnoreCase) || e.BasedOn.Contains(q, StringComparison.OrdinalIgnoreCase);
			}).ToList();

			if (list.Count == 0) {
				Gui.Text(L.Get("WsNothing"), grid.Center.ToVector2(), Gui.Muted, 0.9f, 0.5f, 0.5f);
				return;
			}

			int cols = Math.Max(1, (grid.Width + Gap) / (CardW + Gap));
			int rows = (list.Count + cols - 1) / cols;
			int maxScroll = Math.Max(0, rows * (CardH + Gap) - grid.Height);
			if (Gui.Hover(grid) && Gui.Scroll != 0) {
				scroll -= Math.Sign(Gui.Scroll) * 60;
				Gui.Scroll = 0;
			}
			scroll = Math.Clamp(scroll, 0, maxScroll);

			Gui.BeginClip(grid);
			Gui.InputClip = grid;
			int ox = grid.X + (grid.Width - (cols * (CardW + Gap) - Gap)) / 2;
			for (int i = 0; i < list.Count; i++) {
				var card = new Rectangle(ox + i % cols * (CardW + Gap), grid.Y + i / cols * (CardH + Gap) - scroll, CardW, CardH);
				if (card.Bottom < grid.Y || card.Y > grid.Bottom)
					continue;
				DrawCard(card, list[i]);
			}
			Gui.InputClip = null;
			Gui.EndClip();
		}

		private static void DrawCard(Rectangle card, WorkshopEntry e) {
			bool hover = Gui.Free && Gui.Hover(card);
			Design owned = WorkshopClient.Owned(e);
			Gui.Panel(card, hover ? Gui.ButtonHover * 0.9f : Gui.PanelBlue * 0.55f, owned != null ? Gui.Gold * 0.7f : hover ? Gui.Gold * 0.6f : Color.Black);

			var thumbArea = new Rectangle(card.X + 8, card.Y + 8, card.Width - 16, 112);
			Gui.Checker(thumbArea, 6);
			Texture2D tex = WorkshopClient.Thumb(e);
			if (tex != null) {
				float s = Math.Min((float)thumbArea.Width / tex.Width, (float)thumbArea.Height / tex.Height);
				if (s >= 1f)
					s = (float)Math.Floor(s);
				var dst = new Rectangle(thumbArea.Center.X - (int)(tex.Width * s / 2), thumbArea.Center.Y - (int)(tex.Height * s / 2), (int)(tex.Width * s), (int)(tex.Height * s));
				Gui.Sb.Draw(tex, dst, Color.White);
			}
			int kindIcon = e.KindValue == DesignKind.Wearable ? Icons.ForSlot(e.SlotValue) : Icons.ForKind(e.KindValue);
			Gui.Icon(kindIcon, new Vector2(thumbArea.X + 11, thumbArea.Y + 11), Color.White, 0.8f);

			int y = thumbArea.Bottom + 12;
			Gui.Text(Gui.Fit(e.Name, card.Width - 16, 0.82f), new Vector2(card.X + 8, y), Color.White, 0.82f, 0f, 0.5f);
			y += 20;
			// the unobtrusive line: what was reworked, or that it's an original
			string what = string.IsNullOrEmpty(e.BasedOn)
				? L.Get("WsOriginal", e.KindValue == DesignKind.Wearable ? L.Slot(e.SlotValue) : L.Get("Kind" + e.KindValue))
				: L.Get("WsRework", e.BasedOn);
			Gui.Text(Gui.Fit(what, card.Width - 16, 0.66f), new Vector2(card.X + 8, y), Gui.Muted, 0.66f, 0f, 0.5f);
			y += 20;

			// author: Steam avatar and nickname
			var av = new Rectangle(card.X + 8, y - 2, 20, 20);
			Texture2D avatar = WorkshopClient.Avatar(e.AuthorId);
			if (avatar != null)
				Gui.Sb.Draw(avatar, av, Color.White);
			else
				Gui.Icon(Icons.User, av.Center.ToVector2(), Color.White * 0.8f, 0.8f);
			Gui.Outline(av, Color.Black * 0.6f);
			Gui.Text(Gui.Fit(e.AuthorName, card.Width - 44, 0.72f), new Vector2(av.Right + 6, av.Center.Y + 2), Gui.Gold * 0.9f, 0.72f, 0f, 0.5f);

			if (hover && !string.IsNullOrWhiteSpace(e.Description))
				Gui.Tip = e.Name + "\n" + e.Description;

			// download / open
			var btn = new Rectangle(card.Right - 36, card.Y + 12, 28, 28);
			if (owned != null) {
				if (Gui.IconButton(btn, Icons.Check, "ws-open" + e.Id, L.Get("WsOpenOwned"), true)) {
					StudioState.Load(owned);
					StudioState.LibraryOpen = false;
				}
			}
			else if (WorkshopClient.IsDownloading(e)) {
				Gui.Slot(btn, false, false);
				Gui.Icon(Icons.Refresh, btn.Center.ToVector2(), Color.White * (0.5f + 0.5f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 8f)), 0.9f);
			}
			else if (Gui.IconButton(btn, Icons.CloudDown, "ws-get" + e.Id, L.Get("WsDownload"))) {
				WorkshopClient.Download(e, d => {
					if (d == null) {
						StudioState.Notify(L.Get("WsDownloadFailed"));
						return;
					}
					SoundEngine.PlaySound(SoundID.Item4);
					StudioState.Notify(L.Get("WsDownloaded", d.Name));
				});
			}
		}
	}
}
