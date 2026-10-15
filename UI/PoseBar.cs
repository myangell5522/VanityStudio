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
	/// <summary>
	/// Choosing what to draw on: "All poses" for the normal case, or one of the pose groups (stand, swing, jump, walk)
	/// when something has to look different there. Inside a group the frames are stepped through one by one,
	/// each new one can start from the previous drawing, and the preview follows whatever pose is selected.
	/// Retextures show their sprite-sheet frames instead.
	/// </summary>
	internal static class PoseBar
	{
		public const int Height = 66;

		private static Texture2D hoverThumb;
		private static int hoverPose = -2, hoverRev = -1;
		private static Design hoverDoc;

		public static void Draw(Rectangle r, Design d) {
			Gui.Panel(r);
			if (d.IsWearable)
				DrawWearable(r, d);
			else
				DrawSheet(r, d);
		}

		#region wearables

		private static void DrawWearable(Rectangle r, Design d) {
			int x = r.X + 6, y = r.Y + 4;
			const int tabH = 28;
			int group = StudioState.Pose < 0 ? -1 : PoseGroups.Of(StudioState.Pose);

			string allLabel = L.Get("PoseBase");
			var all = new Rectangle(x, y, Gui.IconTextWidth(allLabel), tabH);
			if (Gui.IconTextButton(all, Icons.Auto, allLabel, "pose-all", group < 0, L.Get("TipPoseBase")))
				Select(Design.BasePose);
			x = all.Right + 10;

			for (int g = 0; g < PoseGroups.All.Length; g++) {
				var (key, icon, poses) = PoseGroups.All[g];
				int drawn = poses.Count(p => d.HasOverride(StudioState.Frame, p));
				string label = L.Get(key) + (poses.Length > 1 ? $" {drawn}/{poses.Length}" : drawn > 0 ? " *" : "");
				var tab = new Rectangle(x, y, Gui.IconTextWidth(label), tabH);
				if (Gui.IconTextButton(tab, icon, label, "pose-group" + g, group == g, L.Get("Tip" + key)))
					Select(group == g ? Design.BasePose : FirstOrCurrent(poses));
				x = tab.Right + 4;
			}

			int y2 = y + tabH + 4;
			if (group < 0) {
				string hint = d.Slot == DesignSlot.Legs && !PoseGroups.All[3].poses.Any(p => d.HasOverride(StudioState.Frame, p))
					? L.Get("HintLegsWalk") : L.Get("HintAllPoses");
				Gui.Text(hint, new Vector2(r.X + 10, y2 + 13), Gui.Muted, 0.72f, 0f, 0.5f);
				if (d.Slot == DesignSlot.Legs) {
					string go = L.Get("StartWalk");
					var b = new Rectangle(r.Right - 8 - Gui.IconTextWidth(go, 0.75f), y2, Gui.IconTextWidth(go, 0.75f), 26);
					if (Gui.IconTextButton(b, Icons.FigWalk, go, "start-walk", false, null, true, 0.75f))
						Select(6);
				}
				return;
			}

			int[] list = PoseGroups.All[group].poses;
			int idx = Array.IndexOf(list, StudioState.Pose);

			// step buttons on the right: previous / next, start from previous pose, copy to the whole group, clear
			int bx = r.Right - 8;
			Rectangle Btn() {
				bx -= 28;
				var b = new Rectangle(bx, y2, 26, 26);
				bx -= 3;
				return b;
			}
			if (Gui.IconButton(Btn(), Icons.Trash, "pose-clear", L.Get("TipPoseClearGroup", L.Get(PoseGroups.All[group].key)), false, list.Any(p => d.HasOverride(StudioState.Frame, p)))) {
				StudioState.Snapshot();
				foreach (int p in list)
					d.ClearOverride(StudioState.Frame, p);
				StudioState.Changed();
			}
			if (list.Length > 1 && Gui.IconButton(Btn(), Icons.Copy, "pose-fill", L.Get("TipPoseFillGroup", L.Get(PoseGroups.All[group].key)))) {
				StudioState.Snapshot();
				foreach (int p in list)
					d.CopyPose(StudioState.Frame, StudioState.Pose, p);
				StudioState.Changed();
				StudioState.Notify(L.Get("PoseFilled"));
			}
			int prevPose = PreviousPose(list, idx);
			if (Gui.IconButton(Btn(), Icons.CopyPrev, "pose-from-prev", L.Get("TipPoseFromPrev", L.Pose(prevPose)), false, prevPose != StudioState.Pose)) {
				StudioState.Snapshot();
				d.CopyPose(StudioState.Frame, prevPose, StudioState.Pose);
				StudioState.Changed();
			}
			bx -= 8;
			if (list.Length > 1) {
				if (Gui.IconButton(Btn(), Icons.Right, "pose-next", L.Get("TipPoseNext")))
					Select(list[(idx + 1) % list.Length]);
				if (Gui.IconButton(Btn(), Icons.Left, "pose-prev", L.Get("TipPosePrev")))
					Select(list[(idx - 1 + list.Length) % list.Length]);
			}

			// the frames of this group as numbered cells
			int cx = r.X + 10;
			if (list.Length > 1) {
				int avail = bx - 10 - cx;
				int cell = Math.Clamp(avail / list.Length - 3, 18, 34);
				for (int i = 0; i < list.Length; i++) {
					int pose = list[i];
					var c = new Rectangle(cx, y2, cell, 26);
					cx += cell + 3;
					PoseCell(c, d, pose, (i + 1).ToString());
				}
				cx += 8;
			}
			bool has = d.HasOverride(StudioState.Frame, StudioState.Pose);
			string status = L.Pose(StudioState.Pose) + " · " + (has ? L.Get("PoseOwn") : L.Get("PoseInherits"));
			if (cx < bx - 20)
				Gui.Text(Gui.Fit(status, bx - 8 - cx, 0.72f), new Vector2(cx, y2 + 13), has ? Gui.Gold : Gui.Muted, 0.72f, 0f, 0.5f);
		}

		private static int FirstOrCurrent(int[] poses) => Array.IndexOf(poses, StudioState.Pose) >= 0 ? StudioState.Pose : poses[0];

		private static int PreviousPose(int[] list, int idx) {
			if (list.Length > 1)
				return list[(idx - 1 + list.Length) % list.Length];
			// jump starts from standing; standing has nothing before it
			return StudioState.Pose == 5 ? 0 : StudioState.Pose;
		}

		private static void PoseCell(Rectangle c, Design d, int pose, string label) {
			bool selected = StudioState.Pose == pose;
			bool own = d.HasOverride(StudioState.Frame, pose);
			bool hover = Gui.Free && Gui.Hover(c);
			Gui.Slot(c, selected, hover);
			Gui.Text(label, new Vector2(c.Center.X, c.Center.Y + 2), own ? Color.White : Gui.Muted, 0.72f, 0.5f, 0.5f);
			if (own)
				Gui.Rect(new Rectangle(c.Right - 6, c.Y + 3, 3, 3), Gui.Gold);
			if (!hover)
				return;
			Gui.MarkHover("pose" + pose, L.Pose(pose) + "\n" + (own ? L.Get("TipPoseOverride") : L.Get("TipPoseInherit")));
			DrawHoverThumb(d, pose, c);
			if (Gui.LeftPressed) {
				Gui.LeftPressed = false;
				Select(pose);
			}
			else if (Gui.RightPressed && own) {
				Gui.RightPressed = false;
				StudioState.Snapshot();
				d.ClearOverride(StudioState.Frame, pose);
				StudioState.Changed();
				SoundEngine.PlaySound(SoundID.Grab);
			}
		}

		// a little picture of that pose under the cursor, so you can see which one is which before clicking
		private static void DrawHoverThumb(Design d, int pose, Rectangle anchor) {
			if (hoverDoc != d || hoverPose != pose || hoverRev != d.Revision || hoverThumb == null || hoverThumb.IsDisposed) {
				Color[] px = d.IsWearable ? d.WornFrame(StudioState.Frame, pose) : d.Flatten(StudioState.Frame, pose);
				if (hoverThumb == null || hoverThumb.IsDisposed || hoverThumb.Width != d.Width || hoverThumb.Height != d.Height) {
					BakedDesign.DisposeLater(hoverThumb);
					hoverThumb = new Texture2D(Main.instance.GraphicsDevice, d.Width, d.Height);
				}
				hoverThumb.SetData(Pixels.Premultiply(px));
				hoverDoc = d;
				hoverPose = pose;
				hoverRev = d.Revision;
			}
			int s = d.Width * 2 <= 120 && d.Height * 2 <= 140 ? 2 : 1;
			var box = new Rectangle(anchor.Center.X - d.Width * s / 2 - 6, anchor.Bottom + 6, d.Width * s + 12, d.Height * s + 12);
			Texture2D tex = hoverThumb;
			// drawn after everything else, otherwise the canvas would paint over it
			Gui.Overlay(() => {
				Gui.Panel(box, Gui.PanelDark, Gui.Gold * 0.7f);
				var img = new Rectangle(box.X + 6, box.Y + 6, d.Width * s, d.Height * s);
				Gui.Checker(img, 4);
				Gui.Sb.Draw(tex, img, Color.White);
			});
		}

		public static void Select(int pose) {
			if (StudioState.Pose == pose)
				return;
			StudioState.Pose = pose;
			StudioState.Playing = false;
			// the preview follows: it shows exactly the pose being drawn, and goes back to cycling for "all poses"
			StudioState.Preview = pose < 0 ? PreviewMode.Auto : PreviewMode.Pose;
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		/// <summary>Tab / Shift+Tab: next or previous pose inside the current group, or through all of them.</summary>
		public static void Step(Design d, int dir) {
			if (!d.IsWearable) {
				Select(((Math.Max(0, StudioState.Pose) + dir) % d.Rows + d.Rows) % d.Rows);
				return;
			}
			if (StudioState.Pose < 0) {
				Select(dir > 0 ? 0 : Design.PoseCount - 1);
				return;
			}
			int g = PoseGroups.Of(StudioState.Pose);
			int[] list = PoseGroups.All[g].poses;
			int i = Array.IndexOf(list, StudioState.Pose) + dir;
			if (i >= 0 && i < list.Length) {
				Select(list[i]);
				return;
			}
			int ng = ((g + dir) % PoseGroups.All.Length + PoseGroups.All.Length) % PoseGroups.All.Length;
			int[] next = PoseGroups.All[ng].poses;
			Select(dir > 0 ? next[0] : next[^1]);
		}

		#endregion

		#region retextures

		private static int sheetScroll;

		private static void DrawSheet(Rectangle r, Design d) {
			int x = r.X + 8, y = r.Y + 4;
			string title = L.Get("SheetFrames", d.Rows);
			Gui.Icon(Icons.ForDesign(d), new Vector2(x + 10, y + 14), Color.White, 0.9f);
			Gui.Text(title, new Vector2(x + 24, y + 15), Color.White, 0.78f, 0f, 0.5f);
			Gui.Text(L.Get("HintSheet"), new Vector2(x + 34 + Gui.Measure(title, 0.78f).X, y + 15), Gui.Muted, 0.7f, 0f, 0.5f);

			int y2 = y + 32;
			int bx = r.Right - 8;
			Rectangle Btn() {
				bx -= 28;
				var b = new Rectangle(bx, y2, 26, 26);
				bx -= 3;
				return b;
			}
			int cur = Math.Max(0, StudioState.Pose);
			if (d.Rows > 1 && Gui.IconButton(Btn(), Icons.Copy, "sheet-fill", L.Get("TipSheetFill"))) {
				StudioState.Snapshot();
				for (int i = 0; i < d.Rows; i++)
					d.CopyPose(StudioState.Frame, cur, i);
				StudioState.Changed();
			}
			if (d.Rows > 1 && Gui.IconButton(Btn(), Icons.CopyPrev, "sheet-prev-copy", L.Get("TipPoseFromPrev", (cur - 1 + d.Rows) % d.Rows + 1))) {
				StudioState.Snapshot();
				d.CopyPose(StudioState.Frame, (cur - 1 + d.Rows) % d.Rows, cur);
				StudioState.Changed();
			}
			bx -= 8;
			if (d.Rows > 1) {
				if (Gui.IconButton(Btn(), Icons.Right, "sheet-next", L.Get("TipPoseNext")))
					Select((cur + 1) % d.Rows);
				if (Gui.IconButton(Btn(), Icons.Left, "sheet-prev", L.Get("TipPosePrev")))
					Select((cur - 1 + d.Rows) % d.Rows);
			}

			int cx = r.X + 10;
			const int cell = 28;
			int visible = Math.Max(1, (bx - 10 - cx) / (cell + 3));
			if (cur < sheetScroll)
				sheetScroll = cur;
			else if (cur >= sheetScroll + visible)
				sheetScroll = cur - visible + 1;
			sheetScroll = Math.Clamp(sheetScroll, 0, Math.Max(0, d.Rows - visible));
			var strip = new Rectangle(cx, y2, visible * (cell + 3), 26);
			if (Gui.Hover(strip) && Gui.Scroll != 0) {
				sheetScroll = Math.Clamp(sheetScroll - Math.Sign(Gui.Scroll), 0, Math.Max(0, d.Rows - visible));
				Gui.Scroll = 0;
			}
			for (int i = sheetScroll; i < d.Rows && i < sheetScroll + visible; i++) {
				var c = new Rectangle(cx, y2, cell, 26);
				cx += cell + 3;
				bool selected = cur == i;
				bool hover = Gui.Free && Gui.Hover(c);
				Gui.Slot(c, selected, hover);
				Gui.Text((i + 1).ToString(), new Vector2(c.Center.X, c.Center.Y + 2), selected ? Color.White : Gui.Muted, 0.7f, 0.5f, 0.5f);
				if (!hover)
					continue;
				Gui.MarkHover("sheet" + i, L.Get("SheetFrameN", i + 1));
				DrawHoverThumb(d, i, c);
				if (Gui.LeftPressed) {
					Gui.LeftPressed = false;
					Select(i);
				}
			}
		}

		#endregion
	}
}
