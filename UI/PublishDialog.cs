using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>Share the open design on the Studio Workshop under your Steam name.</summary>
	internal static class PublishDialog
	{
		private static Design design;
		private static string name = "", description = "";
		private static Texture2D preview;
		private static int previewRev = -1;

		public static void Open(Design d) {
			design = d;
			name = d.Name;
			description = d.Description ?? "";
			WorkshopClient.PublishStatus = null;
			WorkshopClient.Refresh();
			StudioState.Popup = Popup.Publish;
		}

		public static void Draw() {
			Design d = design;
			if (d == null) {
				StudioState.Popup = Popup.None;
				return;
			}
			int w = 600, h = 340;
			var r = new Rectangle(Main.screenWidth / 2 - w / 2, Main.screenHeight / 2 - h / 2, w, h);
			Gui.Rect(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * 0.4f);
			Gui.Panel(r, new Color(33, 43, 79), Gui.Gold * 0.8f);
			if (!Gui.Hover(r) && Gui.LeftPressed && !WorkshopClient.Publishing) {
				StudioState.Popup = Popup.None;
				Gui.LeftPressed = false;
				return;
			}

			int x = r.X + 16, y = r.Y + 14;
			Gui.Icon(Icons.CloudUp, new Vector2(x + 10, y + 12), Color.White);
			Gui.Text(L.Get("PubTitle"), new Vector2(x + 28, y + 13), Gui.Gold, 0.95f, 0f, 0.5f);
			if (Gui.IconButton(new Rectangle(r.Right - 42, r.Y + 10, 30, 30), Icons.Close, "pub-close", L.Get("TipClose"))) {
				StudioState.Popup = Popup.None;
				return;
			}
			y += 40;

			// thumbnail on the left, fields on the right
			var thumb = new Rectangle(x, y, 140, 170);
			Gui.Checker(thumb, 6);
			if (preview == null || preview.IsDisposed || previewRev != d.Revision || preview.Width != d.Width || preview.Height != d.Height) {
				BakedDesign.DisposeLater(preview);
				preview = new Texture2D(Main.instance.GraphicsDevice, d.Width, d.Height);
				preview.SetData(Pixels.Premultiply(d.IsWearable ? d.Flatten(0, Design.BasePose) : d.Flatten(0, 0)));
				previewRev = d.Revision;
			}
			float s = Math.Min((float)thumb.Width / d.Width, (float)thumb.Height / d.Height);
			if (s >= 1f)
				s = (float)Math.Floor(s);
			Gui.Sb.Draw(preview, new Rectangle(thumb.Center.X - (int)(d.Width * s / 2), thumb.Center.Y - (int)(d.Height * s / 2), (int)(d.Width * s), (int)(d.Height * s)), Color.White);
			Gui.Outline(thumb, Color.Black * 0.6f);

			int fx = thumb.Right + 16, fw = r.Right - 16 - fx;
			Gui.Text(L.Get("PubName"), new Vector2(fx, y + 8), Gui.Muted, 0.72f, 0f, 0.5f);
			Gui.TextBox(new Rectangle(fx, y + 18, fw, 30), "pub-name", ref name, 40, d.Name);
			Gui.Text(L.Get("PubDescription"), new Vector2(fx, y + 64), Gui.Muted, 0.72f, 0f, 0.5f);
			Gui.TextBox(new Rectangle(fx, y + 74, fw, 30), "pub-desc", ref description, 140, L.Get("PubDescriptionHint"));

			string what = string.IsNullOrEmpty(d.BasedOn)
				? L.Get("WsOriginal", d.IsWearable ? L.Slot(d.Slot) : L.Get("Kind" + d.Kind))
				: L.Get("WsRework", d.BasedOn);
			Gui.Icon(Icons.ForDesign(d), new Vector2(fx + 10, y + 126), Color.White, 0.8f);
			Gui.Text(what, new Vector2(fx + 24, y + 127), Color.White, 0.74f, 0f, 0.5f);

			var (_, me) = WorkshopClient.Me();
			Gui.Icon(Icons.User, new Vector2(fx + 10, y + 154), Color.White, 0.8f);
			Gui.Text(L.Get("PubAs", me), new Vector2(fx + 24, y + 155), Gui.Gold * 0.9f, 0.74f, 0f, 0.5f);

			int by = r.Bottom - 46;
			string status = WorkshopClient.Publishing ? L.Get("PubSending")
				: WorkshopClient.PublishStatus == "norelay" ? L.Get("PubNoRelay")
				: WorkshopClient.PublishStatus == null ? L.Get("PubNote")
				: WorkshopClient.PublishOk ? L.Get("PubDone", WorkshopClient.PublishStatus) : L.Get("PubFailed", WorkshopClient.PublishStatus);
			Color sc = WorkshopClient.PublishStatus == null || WorkshopClient.Publishing ? Gui.Muted : WorkshopClient.PublishOk ? new Color(120, 230, 120) : new Color(255, 140, 120);
			Gui.Text(Gui.Fit(status, w - 200, 0.7f), new Vector2(x, by + 15), sc, 0.7f, 0f, 0.5f);

			string label = L.Get("PubSend");
			var send = new Rectangle(r.Right - 16 - Gui.IconTextWidth(label), by, Gui.IconTextWidth(label), 32);
			bool can = !WorkshopClient.Publishing && !string.IsNullOrWhiteSpace(name) && !(WorkshopClient.PublishOk && WorkshopClient.PublishStatus != null);
			if (Gui.IconTextButton(send, Icons.CloudUp, label, "pub-send", true, null, can)) {
				d.Description = description.Trim();
				StudioState.Changed();
				WorkshopClient.Publish(d, name.Trim(), description.Trim());
			}
		}
	}
}
