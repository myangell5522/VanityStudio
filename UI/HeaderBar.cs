using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>Top row: what the design is (name, slot or target) on the left, everything you do with it on the right.</summary>
	internal static class HeaderBar
	{
		private static string nameEdit;

		public static void Draw(Rectangle r, Design d) {
			Texture2D mascot = ModContent.Request<Texture2D>("VanityStudio/Assets/Mascot").Value;
			float bob = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f) * 1.5f;
			Gui.Sb.Draw(mascot, new Vector2(r.X + 16, r.Center.Y + bob), null, Color.White, 0f, mascot.Size() / 2f, 30f / mascot.Width, SpriteEffects.None, 0f);
			Gui.Text("Vanity Studio", new Vector2(r.X + 36, r.Center.Y + 3), Color.White, 1f, 0f, 0.5f);
			int x = r.X + 36 + (int)Gui.Measure("Vanity Studio", 1f).X + 18;
			int leftStart = x;

			if (Gui.Focus != "name")
				nameEdit = d.Name;
			if (Gui.TextBox(new Rectangle(x, r.Y + 2, 200, 30), "name", ref nameEdit, 40, L.Get("NamePlaceholder"))) {
				string n = string.IsNullOrWhiteSpace(nameEdit) ? d.Name : nameEdit.Trim();
				if (n != d.Name) {
					StudioState.Snapshot();
					d.Name = n;
					StudioState.Changed();
				}
			}
			x += 208;

			if (d.IsWearable) {
				// five slot tabs with pictures instead of a word you have to read
				for (int i = 0; i < Slots.Count; i++) {
					var slot = (DesignSlot)i;
					var b = new Rectangle(x, r.Y + 1, 32, 32);
					if (Gui.IconButton(b, Icons.ForSlot(slot), "slot" + i, L.Slot(slot) + "\n" + L.Get("Tip" + "Slot" + slot), d.Slot == slot) && d.Slot != slot) {
						StudioState.Snapshot();
						d.ChangeSlot(slot);
						StudioState.Changed();
						CanvasView.ResetView = true;
					}
					x += 34;
				}
				x += 6;
				bool hide = d.HideVanilla;
				string label = L.Get("HideVanilla");
				var hr = new Rectangle(x, r.Y + 2, Gui.IconTextWidth(label), 30);
				if (Gui.IconTextButton(hr, hide ? Icons.EyeOff : Icons.Eye, label, "hide", hide, L.Get("TipHideVanilla"))) {
					StudioState.Snapshot();
					d.HideVanilla = !hide;
					StudioState.Changed();
				}
				x = hr.Right + 4;
			}
			else {
				// a retexture: say plainly what is being replaced
				string what = L.Get("Kind" + d.Kind) + ": " + (string.IsNullOrEmpty(d.BasedOn) ? d.Target : d.BasedOn);
				var chip = new Rectangle(x, r.Y + 2, Math.Min(Gui.IconTextWidth(what), 300), 30);
				Gui.Panel(chip, new Color(40, 52, 98), Color.Black);
				Gui.Icon(Icons.ForDesign(d), new Vector2(chip.X + 16, chip.Center.Y), Color.White, 0.9f);
				Gui.Text(Gui.Fit(what, chip.Width - 36), new Vector2(chip.X + 30, chip.Center.Y + 2), Color.White, 0.8f, 0f, 0.5f);
				if (Gui.Hover(chip))
					Gui.Tip = L.Get("TipRetextureChip", d.Target, d.Width, d.Height, d.Rows);
				x = chip.Right + 4;
			}
			Tutorial.Regions["header-left"] = new Rectangle(leftStart, r.Y, x - leftStart, r.Height);

			int bx = r.Right - 32;
			Rectangle Next(int gap = 4) {
				var b = new Rectangle(bx, r.Y + 1, 32, 32);
				bx -= 32 + gap;
				return b;
			}

			if (Gui.IconButton(Next(10), Icons.Close, "close", L.Get("TipClose"))) {
				StudioState.Close();
				return;
			}
			if (Gui.IconButton(Next(10), Icons.Help, "help", L.Get("TipHelp")))
				Tutorial.Start();

			bool applied = StudioState.IsApplied(d);
			var equipRect = Next(10);
			Tutorial.Regions["equip"] = equipRect;
			int actionsRight = equipRect.Right;
			string equipTip = d.IsWearable ? (applied ? L.Get("TipUnequip") : L.Get("TipEquip")) : (applied ? L.Get("TipUnapply") : L.Get("TipApply"));
			if (Gui.IconButton(equipRect, d.IsWearable ? Icons.Shirt : Icons.Check, "equip", equipTip, applied))
				StudioState.Equip();

			if (Gui.IconButton(Next(), Icons.CloudUp, "publish", L.Get("TipPublish")))
				PublishDialog.Open(d);
			if (Gui.IconButton(Next(), Icons.Export, "export", L.Get("TipExport")))
				StudioWindow.Export(d);
			if (Gui.IconButton(Next(), Icons.Import, "import", L.Get("TipImport")))
				StudioWindow.ImportFile();
			if (Gui.IconButton(Next(), d.IsWearable ? Icons.Reference : Icons.Copy, "from-equipped", d.IsWearable ? L.Get("TipFromEquipped") : L.Get("TipRecopy")))
				StudioWindow.ImportEquipped(d);
			if (Gui.IconButton(Next(10), Icons.Save, "save", L.Get("TipSave"))) {
				DesignLibrary.Save(d);
				StudioState.Notify(L.Get("Saved"));
			}
			if (Gui.IconButton(Next(), Icons.Globe, "workshop", L.Get("TipWorkshop"), StudioState.LibraryOpen && StudioState.WorkshopTab)) {
				bool open = !(StudioState.LibraryOpen && StudioState.WorkshopTab);
				StudioState.LibraryOpen = open;
				StudioState.WorkshopTab = open;
			}
			if (Gui.IconButton(Next(10), Icons.Library, "library", L.Get("TipLibrary"), StudioState.LibraryOpen && !StudioState.WorkshopTab)) {
				bool open = !(StudioState.LibraryOpen && !StudioState.WorkshopTab);
				StudioState.LibraryOpen = open;
				StudioState.WorkshopTab = false;
			}
			if (Gui.IconButton(Next(), Icons.Redo, "redo", L.Get("TipRedo"), false, StudioState.CanRedo))
				StudioState.Redo();
			if (Gui.IconButton(Next(), Icons.Undo, "undo", L.Get("TipUndo"), false, StudioState.CanUndo))
				StudioState.Undo();
			Tutorial.Regions["actions"] = new Rectangle(bx + 36, r.Y, actionsRight - bx - 36, r.Height);
		}
	}
}
