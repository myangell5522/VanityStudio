using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities.FileBrowser;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	internal static class StudioWindow
	{
		private const int RightColumn = 286;
		private const int TimelineHeight = 160;

		private static string nameEdit;
		private static string renameText;
		private static int renaming = -1;
		private static int layerScroll, frameScroll;
		private static int lastLayerClick = -1;
		private static uint lastLayerClickTime;

		public static void Draw() {
			Design d = StudioState.Doc;
			if (d == null)
				return;

			Gui.BeginFrame();
			Main.LocalPlayer.mouseInterface = true;
			PlayerInput.LockVanillaMouseScroll("VanityStudio/Studio");

			Gui.Locked = Tutorial.Active;
			if (!Gui.Typing && !Tutorial.Active && Gui.EscapePressed) {
				if (StudioState.LibraryOpen)
					StudioState.LibraryOpen = false;
				else {
					StudioState.ReopenInventory = true;
					StudioState.Close();
					return;
				}
			}

			Gui.Rect(new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * 0.45f);

			int w = Math.Min(Main.screenWidth - 16, 1260), h = Math.Min(Main.screenHeight - 16, 760);
			var win = new Rectangle((Main.screenWidth - w) / 2, (Main.screenHeight - h) / 2, w, h);
			Gui.Panel(win, new Color(33, 43, 79) * 0.97f, Color.Black);

			var header = new Rectangle(win.X + 10, win.Y + 8, win.Width - 20, 34);
			int bodyTop = header.Bottom + 8;
			var right = new Rectangle(win.Right - 10 - RightColumn, bodyTop, RightColumn, win.Bottom - 10 - bodyTop);
			var left = new Rectangle(win.X + 10, bodyTop, right.X - 8 - (win.X + 10), right.Height);

			DrawHeader(header, d);
			var R = Tutorial.Regions;

			if (StudioState.LibraryOpen) {
				LibraryView.Draw(left);
			}
			else {
				var timeline = new Rectangle(left.X, left.Bottom - TimelineHeight, left.Width, TimelineHeight);
				var tools = new Rectangle(left.X, left.Y, 44, timeline.Y - 8 - left.Y);
				var poses = new Rectangle(tools.Right + 8, left.Y, left.Right - tools.Right - 8, 30);
				var canvas = new Rectangle(poses.X, poses.Bottom + 6, poses.Width, timeline.Y - 8 - poses.Bottom - 6);

				R["tools"] = tools;
				R["poses"] = poses;
				R["canvas"] = canvas;
				R["timeline"] = timeline;
				DrawToolbar(tools);
				DrawPoseStrip(poses, d);
				DrawTimeline(timeline, d);
				// canvas last so the panels above get first pick of the mouse
				CanvasView.Draw(canvas);
				DrawToast(canvas);
			}

			R["preview"] = new Rectangle(right.X, right.Y, right.Width, 268);
			R["colors"] = new Rectangle(right.X, right.Y + 276, right.Width, right.Bottom - right.Y - 276);
			int y = DrawPreview(new Rectangle(right.X, right.Y, right.Width, 268));
			y = ColorPanel.Draw(new Rectangle(right.X, y + 8, right.Width, 196));
			ColorPanel.DrawPalettes(new Rectangle(right.X, y + 4, right.Width, right.Bottom - y - 4));

			if (!Tutorial.Active)
				Shortcuts(d);
			Tutorial.Draw();
			Gui.EndFrame();
		}

		#region header

		private static void DrawHeader(Rectangle r, Design d) {
			Texture2DIcon(new Vector2(r.X + 16, r.Center.Y));
			Gui.Text("Vanity Studio", new Vector2(r.X + 36, r.Center.Y + 3), Color.White, 1f, 0f, 0.5f);
			int x = r.X + 36 + (int)Gui.Measure("Vanity Studio", 1f).X + 18;

			int leftStart = x;
			if (Gui.Focus != "name")
				nameEdit = d.Name;
			if (Gui.TextBox(new Rectangle(x, r.Y + 2, 210, 30), "name", ref nameEdit, 40, L.Get("NamePlaceholder"))) {
				string n = string.IsNullOrWhiteSpace(nameEdit) ? d.Name : nameEdit.Trim();
				if (n != d.Name) {
					StudioState.Snapshot();
					d.Name = n;
					StudioState.Changed();
				}
			}
			x += 218;

			int slotW = 0;
			for (int i = 0; i < Slots.Count; i++)
				slotW = Math.Max(slotW, (int)Gui.Measure("< " + L.Slot((DesignSlot)i) + " >").X + 28);
			var slotRect = new Rectangle(x, r.Y + 2, slotW, 30);
			if (Gui.Button(slotRect, "< " + L.Slot(d.Slot) + " >", "slot", false, true, L.Get("TipSlot"))) {
				StudioState.Snapshot();
				d.ChangeSlot((DesignSlot)(((int)d.Slot + 1) % Slots.Count));
				StudioState.Changed();
				CanvasView.ResetView = true;
			}
			else if (Gui.Hover(slotRect) && Gui.RightPressed) {
				StudioState.Snapshot();
				d.ChangeSlot((DesignSlot)(((int)d.Slot + Slots.Count - 1) % Slots.Count));
				StudioState.Changed();
				CanvasView.ResetView = true;
				Gui.RightPressed = false;
			}
			x += slotW + 8;

			bool hide = d.HideVanilla;
			int hideW = (int)Gui.Measure(L.Get("HideVanilla")).X + 30;
			Tutorial.Regions["header-left"] = new Rectangle(leftStart, r.Y, x + hideW - leftStart, r.Height);
			if (Gui.Toggle(new Rectangle(x, r.Y + 2, hideW, 30), L.Get("HideVanilla"), "hide", ref hide, L.Get("TipHideVanilla"))) {
				StudioState.Snapshot();
				d.HideVanilla = hide;
				StudioState.Changed();
			}

			int bx = r.Right - 32;
			Rectangle Next(int gap = 4) {
				var b = new Rectangle(bx, r.Y + 1, 32, 32);
				bx -= 32 + gap;
				return b;
			}

			if (Gui.IconButton(Next(12), Icons.Close, "close", L.Get("TipClose"))) {
				StudioState.Close();
				return;
			}
			if (Gui.IconButton(Next(12), Icons.Help, "help", L.Get("TipHelp")))
				Tutorial.Start();
			bool equipped = StudioState.Local.IsEquipped(d);
			var equipRect = Next();
			Tutorial.Regions["equip"] = equipRect;
			int actionsRight = equipRect.Right;
			if (Gui.IconButton(equipRect, Icons.Shirt, "equip", equipped ? L.Get("TipUnequip") : L.Get("TipEquip"), equipped))
				StudioState.Equip();
			if (Gui.IconButton(Next(), Icons.Export, "export", L.Get("TipExport")))
				Export(d);
			if (Gui.IconButton(Next(), Icons.Import, "import", L.Get("TipImport")))
				ImportFile();
			if (Gui.IconButton(Next(), Icons.Reference, "from-equipped", L.Get("TipFromEquipped")))
				ImportEquipped(d);
			if (Gui.IconButton(Next(), Icons.Save, "save", L.Get("TipSave"))) {
				DesignLibrary.Save(d);
				StudioState.Notify(L.Get("Saved"));
			}
			if (Gui.IconButton(Next(12), Icons.Library, "library", L.Get("TipLibrary"), StudioState.LibraryOpen))
				StudioState.LibraryOpen = !StudioState.LibraryOpen;
			if (Gui.IconButton(Next(), Icons.Redo, "redo", L.Get("TipRedo"), false, StudioState.CanRedo))
				StudioState.Redo();
			if (Gui.IconButton(Next(), Icons.Undo, "undo", L.Get("TipUndo"), false, StudioState.CanUndo))
				StudioState.Undo();
			Tutorial.Regions["actions"] = new Rectangle(bx + 36, r.Y, actionsRight - bx - 36, r.Height);
		}

		private static void Texture2DIcon(Vector2 center) {
			var tex = ModContent.Request<Microsoft.Xna.Framework.Graphics.Texture2D>("VanityStudio/Assets/Mascot").Value;
			float s = 30f / Math.Max(tex.Width, tex.Height);
			Gui.Sb.Draw(tex, center, null, Color.White, 0f, tex.Size() / 2f, s, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
		}

		#endregion

		#region toolbar

		private static void DrawToolbar(Rectangle r) {
			Gui.Panel(r);
			int y = r.Y + 6;
			var x = r.X + 5;
			void ToolButton(Tool t, int icon, string key, string shortcut) {
				if (Gui.IconButton(new Rectangle(x, y, 34, 34), icon, "tool" + t, L.Get(key) + $" ({shortcut})", StudioState.Tool == t))
					StudioState.Tool = t;
				y += 37;
			}
			ToolButton(Tool.Pencil, Icons.Pencil, "ToolPencil", "B");
			ToolButton(Tool.Eraser, Icons.Eraser, "ToolEraser", "E");
			ToolButton(Tool.Fill, Icons.Bucket, "ToolFill", "G");
			ToolButton(Tool.Picker, Icons.Picker, "ToolPicker", "I / Alt");
			ToolButton(Tool.Line, Icons.Line, "ToolLine", "L");
			ToolButton(Tool.Rect, Icons.Rect, "ToolRect", "U");
			ToolButton(Tool.Move, Icons.Move, "ToolMove", "V");

			y += 4;
			Gui.Rect(new Rectangle(x + 4, y - 4, 26, 2), Color.Black * 0.5f);

			var brush = new Rectangle(x, y, 34, 26);
			if (Gui.Button(brush, StudioState.Brush + "px", "brush", false, true, L.Get("TipBrush"))) {
				StudioState.Brush = StudioState.Brush % 4 + 1;
			}
			else if (Gui.Hover(brush) && Gui.RightPressed) {
				StudioState.Brush = (StudioState.Brush + 2) % 4 + 1;
				Gui.RightPressed = false;
			}
			y += 30;

			if (Gui.IconButton(new Rectangle(x, y, 34, 34), Icons.Mirror, "mirror", L.Get("TipMirror"), StudioState.Mirror))
				StudioState.Mirror = !StudioState.Mirror;
			y += 37;
			if (Gui.IconButton(new Rectangle(x, y, 34, 34), Icons.Grid, "grid", L.Get("TipGrid"), StudioSettings.ShowGrid))
				StudioSettings.ShowGrid = !StudioSettings.ShowGrid;
			y += 37;
			var refRect = new Rectangle(x, y, 34, 34);
			if (Gui.IconButton(refRect, Icons.Reference, "reference", L.Get("TipReference", (int)(StudioState.ReferenceAlpha * 100)), StudioSettings.ShowReference))
				StudioSettings.ShowReference = !StudioSettings.ShowReference;
			if (Gui.Hover(refRect) && Gui.Scroll != 0) {
				StudioState.ReferenceAlpha = Math.Clamp(StudioState.ReferenceAlpha + Math.Sign(Gui.Scroll) * 0.05f, 0.1f, 1f);
				Gui.Scroll = 0;
			}
		}

		#endregion

		#region poses

		private static void DrawPoseStrip(Rectangle r, Design d) {
			Gui.Panel(r);
			int x = r.X + 6, y = r.Y + 3;
			int h = r.Height - 6;

			// 20 legacy frames: stand, 4 swing frames, jump, 14 walk frames
			int Wide(string s) => (int)Gui.Measure(s, 0.75f).X + 20;
			int baseW = (int)Gui.Measure(L.Get("PoseBase")).X + 26;
			int standW = Wide(L.Get("PoseStand")), jumpW = Wide(L.Get("PoseJump"));
			int useLabel = (int)Gui.Measure(L.Get("GroupUse"), 0.7f).X + 6, walkLabel = (int)Gui.Measure(L.Get("GroupWalk"), 0.7f).X + 6;
			int available = r.Width - 12;
			bool labels = true;
			int Cell() => (available - baseW - 10 - standW - 2 - 8 - (labels ? useLabel + walkLabel : 0) - 8 - jumpW - 2 - 8) / 18 - 2;
			int cell = Cell();
			if (cell < 16) {
				labels = false;
				cell = Cell();
			}
			cell = Math.Clamp(cell, 12, 26);

			if (Gui.Button(new Rectangle(x, y, baseW, h), L.Get("PoseBase"), "pose-base", StudioState.Pose < 0, true, L.Get("TipPoseBase")))
				StudioState.Pose = Design.BasePose;
			x += baseW + 10;

			PoseButton(ref x, y, standW, h, 0, L.Get("PoseStand"), d);
			x += 8;
			if (labels) {
				Gui.Text(L.Get("GroupUse"), new Vector2(x, y + h / 2 + 2), Gui.Muted, 0.7f, 0f, 0.5f);
				x += useLabel;
			}
			for (int i = 1; i <= 4; i++)
				PoseButton(ref x, y, cell, h, i, i.ToString(), d);
			x += 8;
			PoseButton(ref x, y, jumpW, h, 5, L.Get("PoseJump"), d);
			x += 8;
			if (labels) {
				Gui.Text(L.Get("GroupWalk"), new Vector2(x, y + h / 2 + 2), Gui.Muted, 0.7f, 0f, 0.5f);
				x += walkLabel;
			}
			for (int i = 6; i < Design.PoseCount; i++)
				PoseButton(ref x, y, cell, h, i, (i - 5).ToString(), d);
		}

		private static void PoseButton(ref int x, int y, int w, int h, int pose, string label, Design d) {
			var r = new Rectangle(x, y, w, h);
			x += w + 2;
			bool selected = StudioState.Pose == pose;
			bool hasOverride = d.HasOverride(StudioState.Frame, pose);
			bool hover = Gui.Free && Gui.Hover(r);
			Gui.Slot(r, selected, hover);
			Gui.Text(label, new Vector2(r.Center.X, r.Center.Y + 2), hasOverride ? Color.White : Gui.Muted, 0.7f, 0.5f, 0.5f);
			if (hasOverride)
				Gui.Rect(new Rectangle(r.Right - 5, r.Y + 3, 3, 3), Gui.Gold);
			if (!hover)
				return;
			Gui.MarkHover("pose" + pose, L.Pose(pose) + "\n" + (hasOverride ? L.Get("TipPoseOverride") : L.Get("TipPoseInherit")));
			if (Gui.LeftPressed) {
				StudioState.Pose = selected ? Design.BasePose : pose;
				Gui.LeftPressed = false;
				SoundEngine.PlaySound(SoundID.MenuTick);
			}
			else if (Gui.RightPressed && hasOverride) {
				StudioState.Snapshot();
				d.ClearOverride(StudioState.Frame, pose);
				StudioState.Changed();
				Gui.RightPressed = false;
				SoundEngine.PlaySound(SoundID.Grab);
			}
		}

		#endregion

		#region timeline

		private static void DrawTimeline(Rectangle r, Design d) {
			Gui.Panel(r);
			const int layersW = 262, rowH = 22, cellW = 22;
			int top = r.Y + 8;

			// --- layer buttons
			int bx = r.X + 8;
			Rectangle B() {
				var b = new Rectangle(bx, top, 28, 28);
				bx += 31;
				return b;
			}
			if (Gui.IconButton(B(), Icons.Plus, "layer-add", L.Get("TipLayerAdd"), false, d.Layers.Count < Design.MaxLayers)) {
				StudioState.Snapshot();
				d.Layers.Insert(StudioState.Layer + 1, new DesignLayer { Name = L.Get("LayerName", d.Layers.Count + 1) });
				StudioState.Layer++;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Copy, "layer-dup", L.Get("TipLayerDuplicate"), false, d.Layers.Count < Design.MaxLayers)) {
				StudioState.Snapshot();
				DesignLayer copy = d.Layers[StudioState.Layer].Clone();
				copy.Name += " +";
				d.Layers.Insert(StudioState.Layer + 1, copy);
				StudioState.Layer++;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Trash, "layer-del", L.Get("TipLayerDelete"), false, d.Layers.Count > 1)) {
				StudioState.Snapshot();
				d.Layers.RemoveAt(StudioState.Layer);
				StudioState.ClampSelection();
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Up, "layer-up", L.Get("TipLayerUp"), false, StudioState.Layer < d.Layers.Count - 1)) {
				StudioState.Snapshot();
				int i = StudioState.Layer;
				(d.Layers[i], d.Layers[i + 1]) = (d.Layers[i + 1], d.Layers[i]);
				StudioState.Layer++;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Down, "layer-down", L.Get("TipLayerDown"), false, StudioState.Layer > 0)) {
				StudioState.Snapshot();
				int i = StudioState.Layer;
				(d.Layers[i], d.Layers[i - 1]) = (d.Layers[i - 1], d.Layers[i]);
				StudioState.Layer--;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Merge, "layer-merge", L.Get("TipLayerMerge"), false, StudioState.Layer > 0)) {
				StudioState.Snapshot();
				d.MergeDown(StudioState.Layer);
				StudioState.Layer--;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Flip, "layer-flip", L.Get("TipLayerFlip")))
				EditCels(d, Gui.Shift, cel => Pixels.FlipH(cel, d.Width, d.Height));
			if (Gui.IconButton(B(), Icons.Outline, "layer-outline", L.Get("TipLayerOutline")))
				EditCels(d, Gui.Shift, cel => Pixels.Outline(cel, d.Width, d.Height, StudioState.Primary.A > 0 ? StudioState.Primary : new Color(24, 18, 32)));

			// --- frame buttons
			int fx0 = r.X + layersW + 14;
			bx = fx0;
			if (Gui.IconButton(B(), Icons.Plus, "frame-add", L.Get("TipFrameAdd"), false, d.FrameCount < Design.MaxFrames)) {
				StudioState.Snapshot();
				d.InsertFrame(StudioState.Frame + 1, -1);
				StudioState.Frame++;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Copy, "frame-dup", L.Get("TipFrameDuplicate"), false, d.FrameCount < Design.MaxFrames)) {
				StudioState.Snapshot();
				d.InsertFrame(StudioState.Frame + 1, StudioState.Frame);
				StudioState.Frame++;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Trash, "frame-del", L.Get("TipFrameDelete"), false, d.FrameCount > 1)) {
				StudioState.Snapshot();
				d.RemoveFrame(StudioState.Frame);
				StudioState.ClampSelection();
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Left, "frame-left", L.Get("TipFrameLeft"), false, StudioState.Frame > 0)) {
				StudioState.Snapshot();
				d.SwapFrames(StudioState.Frame, StudioState.Frame - 1);
				StudioState.Frame--;
				StudioState.Changed();
			}
			if (Gui.IconButton(B(), Icons.Right, "frame-right", L.Get("TipFrameRight"), false, StudioState.Frame < d.FrameCount - 1)) {
				StudioState.Snapshot();
				d.SwapFrames(StudioState.Frame, StudioState.Frame + 1);
				StudioState.Frame++;
				StudioState.Changed();
			}
			bx += 8;
			if (Gui.IconButton(B(), StudioState.Playing ? Icons.Pause : Icons.Play, "play", L.Get("TipPlay"), StudioState.Playing, d.FrameCount > 1))
				StudioState.Playing = !StudioState.Playing;
			if (Gui.IconButton(B(), Icons.Onion, "onion", L.Get("TipOnion"), StudioState.Onion))
				StudioState.Onion = !StudioState.Onion;
			bx += 10;
			DrawDuration(new Rectangle(bx, top, 160, 28), d);

			// --- rows
			int numbersY = top + 34;
			int rowsTop = numbersY + 16;
			int opacityY = r.Bottom - Gui.BarHeight - 10;
			int rowsBottom = opacityY - 6;
			int visible = Math.Max(1, (rowsBottom - rowsTop) / rowH);
			layerScroll = Math.Clamp(layerScroll, 0, Math.Max(0, d.Layers.Count - visible));

			var rowsArea = new Rectangle(r.X + 6, rowsTop, r.Width - 12, visible * rowH);
			if (Gui.Hover(rowsArea) && Gui.Scroll != 0 && Gui.Free) {
				if (Gui.Shift)
					frameScroll -= Math.Sign(Gui.Scroll) * 2;
				else
					layerScroll -= Math.Sign(Gui.Scroll);
				Gui.Scroll = 0;
			}
			int cellsW = r.Right - 8 - fx0;
			int visibleFrames = Math.Max(1, cellsW / cellW);
			frameScroll = Math.Clamp(frameScroll, 0, Math.Max(0, d.FrameCount - visibleFrames));
			if (StudioState.Frame < frameScroll)
				frameScroll = StudioState.Frame;
			else if (StudioState.Frame >= frameScroll + visibleFrames)
				frameScroll = StudioState.Frame - visibleFrames + 1;

			Gui.Text(L.Get("Layers"), new Vector2(r.X + 10, numbersY + 8), Gui.Muted, 0.65f, 0f, 0.5f);
			for (int f = frameScroll; f < d.FrameCount && f < frameScroll + visibleFrames; f++) {
				int cx = fx0 + (f - frameScroll) * cellW;
				var numRect = new Rectangle(cx, numbersY, cellW - 2, 14);
				bool cur = f == StudioState.Frame;
				if (cur)
					Gui.Rect(new Rectangle(cx, numbersY, cellW - 2, rowsTop - numbersY + visible * rowH), Gui.Gold * 0.12f);
				Gui.Text((f + 1).ToString(), new Vector2(numRect.Center.X, numRect.Center.Y + 2), cur ? Gui.Gold : Gui.Muted, 0.65f, 0.5f, 0.5f);
				if (Gui.Free && Gui.Hover(numRect) && Gui.LeftPressed) {
					StudioState.Frame = f;
					StudioState.Playing = false;
					Gui.LeftPressed = false;
				}
			}

			for (int row = 0; row < visible; row++) {
				int li = d.Layers.Count - 1 - (layerScroll + row);
				if (li < 0)
					break;
				DrawLayerRow(new Rectangle(r.X + 6, rowsTop + row * rowH, layersW, rowH - 2), li, d);
				DesignLayer l = d.Layers[li];
				for (int f = frameScroll; f < d.FrameCount && f < frameScroll + visibleFrames; f++) {
					var cr = new Rectangle(fx0 + (f - frameScroll) * cellW, rowsTop + row * rowH, cellW - 2, rowH - 2);
					DrawCel(cr, l, li, f);
				}
			}

			// --- opacity of the current layer
			DesignLayer layer = d.Layers[StudioState.Layer];
			Gui.Text(L.Get("Opacity"), new Vector2(r.X + 10, opacityY + Gui.BarHeight / 2 + 2), Gui.Muted, 0.7f, 0f, 0.5f);
			float op = layer.Opacity;
			float labelW = Gui.Measure(L.Get("Opacity"), 0.7f).X + 16;
			if (Gui.ColorBar(new Vector2(r.X + 10 + labelW, opacityY), ref op, "opacity", t => Color.Lerp(new Color(40, 40, 52), Color.White, t))) {
				StudioState.SnapshotOnce("opacity");
				layer.Opacity = (float)Math.Round(op, 2);
				StudioState.Changed();
			}
			Gui.Text((int)Math.Round(layer.Opacity * 100) + "%", new Vector2(r.X + 18 + labelW + Gui.BarWidth, opacityY + Gui.BarHeight / 2 + 2), Color.White, 0.7f, 0f, 0.5f);
		}

		// Shift-click on layer tools applies them to every frame and pose of the layer, otherwise just what's on screen.
		private static void EditCels(Design d, bool wholeLayer, Func<Color[], Color[]> edit) {
			DesignLayer l = d.Layers[StudioState.Layer];
			StudioState.Snapshot();
			if (wholeLayer) {
				foreach (var key in l.Cels.Keys.ToList())
					l.Cels[key] = edit(l.Cels[key]);
			}
			else {
				Color[] cel = d.GetOrCreate(StudioState.Layer, StudioState.Frame, StudioState.Pose);
				Array.Copy(edit(cel), cel, cel.Length);
			}
			StudioState.Changed();
		}

		private static void DrawDuration(Rectangle r, Design d) {
			int ms = d.FrameTimes[StudioState.Frame];
			var minus = new Rectangle(r.X, r.Y, 28, 28);
			var plus = new Rectangle(r.X + 104, r.Y, 28, 28);
			int step = Gui.Shift ? 50 : 10;
			int changeBy = 0;
			if (Gui.IconButton(minus, Icons.Minus, "dur-minus", L.Get("TipDuration")))
				changeBy = -step;
			if (Gui.IconButton(plus, Icons.Plus, "dur-plus", L.Get("TipDuration")))
				changeBy = step;
			var mid = new Rectangle(r.X + 31, r.Y, 70, 28);
			Gui.Panel(mid, new Color(28, 36, 66), Color.Black);
			Gui.Text(ms + " ms", new Vector2(mid.Center.X, mid.Center.Y + 2), Color.White, 0.75f, 0.5f, 0.5f);
			if (Gui.Hover(mid)) {
				Gui.Tip = L.Get("TipDuration");
				if (Gui.Scroll != 0) {
					changeBy = Math.Sign(Gui.Scroll) * step;
					Gui.Scroll = 0;
				}
			}
			if (Gui.Button(new Rectangle(r.X + 136, r.Y, 52, 28), L.Get("All"), "dur-all", false, d.FrameCount > 1, L.Get("TipDurationAll"))) {
				StudioState.Snapshot();
				for (int i = 0; i < d.FrameCount; i++)
					d.FrameTimes[i] = ms;
				StudioState.Changed();
			}
			if (changeBy != 0) {
				StudioState.SnapshotOnce("duration" + StudioState.Frame);
				d.FrameTimes[StudioState.Frame] = Math.Clamp(ms + changeBy, 10, 5000);
				StudioState.Changed();
			}
		}

		private static void DrawLayerRow(Rectangle r, int li, Design d) {
			DesignLayer l = d.Layers[li];
			bool selected = li == StudioState.Layer;
			bool hover = Gui.Free && Gui.Hover(r);
			Gui.Rect(r, selected ? Gui.ButtonOn * 0.85f : hover ? Gui.ButtonHover * 0.6f : Color.Black * 0.2f);
			if (selected)
				Gui.Outline(r, Gui.Gold * 0.7f);

			var eye = new Rectangle(r.X + 2, r.Y, 22, r.Height);
			Gui.Icon(l.Visible ? Icons.Eye : Icons.EyeOff, eye.Center.ToVector2(), Color.White, 0.8f);
			var nameRect = new Rectangle(r.X + 26, r.Y, r.Width - 70, r.Height);

			if (renaming == li) {
				if (Gui.TextBox(new Rectangle(nameRect.X, r.Y - 3, nameRect.Width, r.Height + 6), "rename", ref renameText, 24)) {
					if (!string.IsNullOrWhiteSpace(renameText) && renameText != l.Name) {
						StudioState.Snapshot();
						l.Name = renameText.Trim();
						StudioState.Changed();
					}
					renaming = -1;
				}
				else if (Gui.Focus != "rename") {
					renaming = -1;
				}
			}
			else {
				Gui.Text(Gui.Fit(l.Name, nameRect.Width, 0.75f), new Vector2(nameRect.X, r.Center.Y + 2), l.Visible ? Color.White : Gui.Muted, 0.75f, 0f, 0.5f);
			}
			Gui.Text((int)Math.Round(l.Opacity * 100) + "%", new Vector2(r.Right - 6, r.Center.Y + 2), Gui.Muted, 0.65f, 1f, 0.5f);

			if (!hover || renaming == li)
				return;
			if (Gui.Hover(eye)) {
				Gui.Tip = L.Get("TipVisible");
				if (Gui.LeftPressed) {
					StudioState.Snapshot();
					l.Visible = !l.Visible;
					StudioState.Changed();
					Gui.LeftPressed = false;
					SoundEngine.PlaySound(SoundID.MenuTick);
				}
				return;
			}
			Gui.Tip = L.Get("TipRename");
			if (Gui.LeftPressed) {
				uint now = Main.GameUpdateCount;
				if (lastLayerClick == li && now - lastLayerClickTime < 25) {
					renaming = li;
					renameText = l.Name;
					Gui.Focus = "rename";
					Main.clrInput();
				}
				lastLayerClick = li;
				lastLayerClickTime = now;
				StudioState.Layer = li;
				Gui.LeftPressed = false;
			}
		}

		private static void DrawCel(Rectangle r, DesignLayer l, int li, int f) {
			bool current = li == StudioState.Layer && f == StudioState.Frame;
			bool hover = Gui.Free && Gui.Hover(r);
			Gui.Slot(r, current, hover);
			bool hasBase = l.Cels.ContainsKey(new CelKey(f, Design.BasePose));
			bool hasPose = l.Cels.Keys.Any(k => k.Frame == f && k.Pose >= 0);
			if (hasBase)
				Gui.Rect(new Rectangle(r.Center.X - 3, r.Center.Y - 3, 6, 6), current ? Color.Black : Color.White * 0.85f);
			else if (!hasPose)
				Gui.Rect(new Rectangle(r.Center.X - 1, r.Center.Y - 1, 2, 2), Color.White * 0.3f);
			if (hasPose)
				Gui.Rect(new Rectangle(r.Right - 6, r.Y + 3, 3, 3), Gui.Gold);
			if (!hover)
				return;
			Gui.Tip = $"{l.Name} · {L.Get("FrameN", f + 1)}\n{L.Get("TipCelClear")}";
			if (Gui.LeftPressed) {
				StudioState.Layer = li;
				StudioState.Frame = f;
				StudioState.Playing = false;
				Gui.LeftPressed = false;
			}
			else if (Gui.RightPressed && (hasBase || hasPose)) {
				StudioState.Snapshot();
				if (StudioState.Pose >= 0)
					l.Cels.Remove(new CelKey(f, StudioState.Pose));
				else
					l.Cels.Remove(new CelKey(f, Design.BasePose));
				StudioState.Changed();
				Gui.RightPressed = false;
			}
		}

		#endregion

		#region preview

		private static int DrawPreview(Rectangle r) {
			Gui.Panel(r);
			var stage = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, 196);
			Gui.Rect(stage, new Color(22, 26, 52));
			// a hint of sky and a floor, like the character-select backdrop
			for (int i = 0; i < 6; i++)
				Gui.Rect(new Rectangle(stage.X, stage.Y + i * 12, stage.Width, 12), new Color(70, 90, 160) * (0.22f - i * 0.035f));
			Gui.Rect(new Rectangle(stage.X, stage.Bottom - 20, stage.Width, 20), new Color(40, 34, 30));
			Gui.Rect(new Rectangle(stage.X, stage.Bottom - 20, stage.Width, 3), new Color(94, 140, 60));
			Gui.Outline(stage, Color.Black * 0.7f);

			Player dummy = StudioState.Dummy;
			if (dummy != null) {
				int pose = StudioState.PreviewPoseNow(out bool flying);
				StudioState.SetDummyPose(pose);
				float lift = flying ? (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f) * 4f + 12f : pose == 5 ? 14f : 0f;
				Gui.BeginClip(stage, immediate: true);
				Gui.DrawPlayer(dummy, new Vector2(stage.Center.X, stage.Bottom - 20 - lift), StudioSettings.PreviewScale);
				Gui.EndClip();
			}
			if (Gui.Free && Gui.Hover(stage)) {
				Gui.Tip = L.Get("TipPreview", StudioSettings.PreviewScale);
				if (Gui.Scroll != 0) {
					StudioSettings.PreviewScale = Math.Clamp(StudioSettings.PreviewScale + Math.Sign(Gui.Scroll), 2, 4);
					Gui.Scroll = 0;
				}
			}

			if (Gui.IconButton(new Rectangle(stage.Right - 30, stage.Y + 4, 26, 26), Icons.Flip, "turn", L.Get("TipTurn")))
				StudioState.PreviewDirection = -StudioState.PreviewDirection;

			string[] names = { "PvAuto", "PvIdle", "PvWalk", "PvJump", "PvFly", "PvUse", "PvPose" };
			int bw = (r.Width - 16 - 3 * 4) / 4;
			for (int i = 0; i < names.Length; i++) {
				int col = i % 4, row = i / 4;
				var b = new Rectangle(r.X + 8 + col * (bw + 4), stage.Bottom + 6 + row * 29, bw, 26);
				if (Gui.Button(b, L.Get(names[i]), "pv" + i, (int)StudioState.Preview == i))
					StudioState.Preview = (PreviewMode)i;
			}
			return r.Bottom;
		}

		#endregion

		private static void DrawToast(Rectangle canvas) {
			if (StudioState.ToastTime <= 0 || string.IsNullOrEmpty(StudioState.Toast))
				return;
			float a = Math.Min(1f, StudioState.ToastTime / 30f);
			Vector2 size = Gui.Measure(StudioState.Toast, 0.85f);
			var r = new Rectangle(canvas.Center.X - (int)size.X / 2 - 16, canvas.Bottom - 54, (int)size.X + 32, 34);
			Gui.Panel(r, Gui.PanelBlue * a, Color.Black * a);
			Gui.Text(StudioState.Toast, new Vector2(r.Center.X, r.Center.Y + 2), Color.White * a, 0.85f, 0.5f, 0.5f);
		}

		#region actions

		internal static void Export(Design d) {
			try {
				DesignLibrary.Save(d);
				string png = SheetIO.Export(d);
				StudioState.Notify(L.Get("Exported", Path.GetFileName(png)));
				Main.NewText(L.Get("ExportedChat", png), Gui.Gold);
			}
			catch (Exception e) {
				VanityStudio.Instance.Logger.Error("Export failed", e);
				StudioState.Notify(L.Get("ExportFailed"));
			}
		}

		internal static void ImportFile() {
			string path = FileBrowser.OpenFilePanel(L.Get("ImportTitle"), new[] { new ExtensionFilter("PNG / Vanity Studio", "png", "vsd") });
			Gui.ResetInput();
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return;
			try {
				if (path.EndsWith(".vsd", StringComparison.OrdinalIgnoreCase)) {
					Design loaded = DesignLibrary.TryLoadFile(path);
					if (loaded == null) {
						StudioState.Notify(L.Get("ImportFailed"));
						return;
					}
					Design copy = loaded.Duplicate(DesignLibrary.UniqueName(loaded.Name));
					DesignLibrary.Add(copy);
					StudioState.Load(copy);
					StudioState.LibraryOpen = false;
					StudioState.Notify(L.Get("Imported", copy.Name));
					return;
				}
				Color[] px = SheetIO.ReadPng(path, out int w, out int h);
				Design d = StudioState.Doc;
				StudioState.Snapshot();
				int layer = SheetIO.ImportImage(d, px, w, h, Path.GetFileNameWithoutExtension(path), StudioState.Frame);
				if (layer < 0) {
					StudioState.Notify(L.Get("TooManyLayers"));
					return;
				}
				StudioState.Layer = layer;
				StudioState.LibraryOpen = false;
				StudioState.Changed();
				StudioState.Notify(L.Get("Imported", Path.GetFileName(path)));
			}
			catch (Exception e) {
				VanityStudio.Instance.Logger.Warn("Import failed: " + e.Message);
				StudioState.Notify(L.Get("ImportFailed"));
			}
		}

		internal static void ImportEquipped(Design d) {
			Color[] sheet;
			string itemName;
			try {
				sheet = SheetIO.SheetFromEquipped(Main.LocalPlayer, d.Slot, out itemName);
			}
			catch (Exception e) {
				VanityStudio.Instance.Logger.Warn("Reading equipped sprite failed: " + e.Message);
				sheet = null;
				itemName = null;
			}
			if (sheet == null) {
				StudioState.Notify(L.Get("NothingEquipped", L.Slot(d.Slot)));
				return;
			}
			StudioState.Snapshot();
			int layer = SheetIO.ImportImage(d, sheet, 40, 1120, itemName, StudioState.Frame);
			if (layer < 0) {
				StudioState.Notify(L.Get("TooManyLayers"));
				return;
			}
			StudioState.Layer = layer;
			StudioState.Changed();
			StudioState.Notify(L.Get("ImportedEquipped", itemName));
		}

		#endregion

		private static void Shortcuts(Design d) {
			if (Gui.Typing || Gui.Active != null)
				return;
			if (Gui.Ctrl) {
				if (Gui.KeyPressed(Keys.Z)) {
					if (Gui.Shift)
						StudioState.Redo();
					else
						StudioState.Undo();
				}
				if (Gui.KeyPressed(Keys.Y))
					StudioState.Redo();
				if (Gui.KeyPressed(Keys.S)) {
					DesignLibrary.Save(d);
					StudioState.Notify(L.Get("Saved"));
				}
				return;
			}
			if (StudioState.LibraryOpen)
				return;
			if (Gui.KeyPressed(Keys.B)) StudioState.Tool = Tool.Pencil;
			if (Gui.KeyPressed(Keys.E)) StudioState.Tool = Tool.Eraser;
			if (Gui.KeyPressed(Keys.G)) StudioState.Tool = Tool.Fill;
			if (Gui.KeyPressed(Keys.I)) StudioState.Tool = Tool.Picker;
			if (Gui.KeyPressed(Keys.L)) StudioState.Tool = Tool.Line;
			if (Gui.KeyPressed(Keys.U)) StudioState.Tool = Tool.Rect;
			if (Gui.KeyPressed(Keys.V)) StudioState.Tool = Tool.Move;
			if (Gui.KeyPressed(Keys.M)) StudioState.Mirror = !StudioState.Mirror;
			if (Gui.KeyPressed(Keys.F)) CanvasView.ResetView = true;
			if (Gui.KeyPressed(Keys.O)) StudioState.Onion = !StudioState.Onion;
			if (Gui.KeyPressed(Keys.OemOpenBrackets)) StudioState.Brush = Math.Max(1, StudioState.Brush - 1);
			if (Gui.KeyPressed(Keys.OemCloseBrackets)) StudioState.Brush = Math.Min(4, StudioState.Brush + 1);
			if (Gui.KeyPressed(Keys.X)) {
				(StudioState.Primary, StudioState.Secondary) = (StudioState.Secondary, StudioState.Primary);
				ColorPanel.SyncFromPrimary();
			}
			if (Gui.KeyPressed(Keys.Enter) && d.FrameCount > 1)
				StudioState.Playing = !StudioState.Playing;
			if (Gui.KeyPressed(Keys.Left) || Gui.KeyPressed(Keys.OemComma)) {
				StudioState.Playing = false;
				StudioState.Frame = (StudioState.Frame - 1 + d.FrameCount) % d.FrameCount;
			}
			if (Gui.KeyPressed(Keys.Right) || Gui.KeyPressed(Keys.OemPeriod)) {
				StudioState.Playing = false;
				StudioState.Frame = (StudioState.Frame + 1) % d.FrameCount;
			}
			if (Gui.KeyPressed(Keys.Up))
				StudioState.Layer = Math.Min(d.Layers.Count - 1, StudioState.Layer + 1);
			if (Gui.KeyPressed(Keys.Down))
				StudioState.Layer = Math.Max(0, StudioState.Layer - 1);
			if (Gui.KeyPressed(Keys.Delete)) {
				DesignLayer l = d.Layers[StudioState.Layer];
				var key = new CelKey(StudioState.Frame, StudioState.Pose);
				if (l.Cels.ContainsKey(key)) {
					StudioState.Snapshot();
					l.Cels.Remove(key);
					StudioState.Changed();
				}
			}
		}
	}
}
