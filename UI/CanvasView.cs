using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>The drawing surface: zoom/pan, onion skin, the character underneath for reference, and the tools.</summary>
	internal static class CanvasView
	{
		public static bool ResetView = true;

		private static readonly int[] zoomSteps = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 14, 16, 20, 24, 32 };
		private static int zoom = 8;
		private static Vector2 pan;
		private static Vector2 panGrab, panMouse;

		private static Texture2D[] layerTex = Array.Empty<Texture2D>();
		private static Texture2D overlayTex, onionPrevTex, onionNextTex;
		private static int cachedRev = -1, cachedFrame = -1, cachedPose = -2, cachedLayers = -1;
		private static Design cachedDoc;
		private static bool overlayDirty;

		private static bool stroking, strokeRight;
		private static Tool strokeTool;
		private static Point last, start;
		private static Color[] moveOrigin, overlay;

		public static Point HoverPixel { get; private set; } = new(-1, -1);
		public static int Zoom => zoom;

		public static void Draw(Rectangle area) {
			Design d = StudioState.Doc;
			Gui.Panel(area, new Color(18, 20, 38) * 0.95f, Color.Black);
			Rectangle inner = area;
			inner.Inflate(-6, -6);

			if (ResetView)
				Fit(inner, d);

			Rectangle img = ImageRect(inner, d);
			HandleInput(inner, img, d);
			img = ImageRect(inner, d);
			RefreshTextures(d);

			Gui.BeginClip(inner, immediate: true);
			Gui.Checker(img, Math.Max(2, zoom * 2));

			if (StudioSettings.ShowReference && (StudioState.Dummy != null || !d.IsWearable))
				DrawReference(img, d);

			if (StudioState.Onion && d.FrameCount > 1) {
				if (onionPrevTex != null)
					Gui.Sb.Draw(onionPrevTex, img, new Color(255, 90, 90) * 0.35f);
				if (onionNextTex != null)
					Gui.Sb.Draw(onionNextTex, img, new Color(90, 255, 140) * 0.25f);
			}

			for (int i = 0; i < d.Layers.Count && i < layerTex.Length; i++) {
				DesignLayer l = d.Layers[i];
				if (l.Visible && layerTex[i] != null)
					Gui.Sb.Draw(layerTex[i], img, Color.White * l.Opacity);
				if (i == StudioState.Layer && overlay != null && overlayTex != null)
					Gui.Sb.Draw(overlayTex, img, Color.White);
			}

			DrawGrid(img, d);
			DrawCursor(img, d);
			Gui.Outline(new Rectangle(img.X - 1, img.Y - 1, img.Width + 2, img.Height + 2), Color.Black * 0.8f);
			Gui.EndClip();

			DrawStatus(inner, d);
			DrawHint(inner);
		}

		private static Rectangle ImageRect(Rectangle inner, Design d) {
			int w = d.Width * zoom, h = d.Height * zoom;
			return new Rectangle(inner.Center.X - w / 2 + (int)pan.X, inner.Center.Y - h / 2 + (int)pan.Y, w, h);
		}

		private static void Fit(Rectangle inner, Design d) {
			int best = 1;
			foreach (int z in zoomSteps) {
				if (d.Width * z <= inner.Width - 20 && d.Height * z <= inner.Height - 20)
					best = z;
			}
			zoom = best;
			pan = Vector2.Zero;
			ResetView = false;
		}

		#region input

		private static void HandleInput(Rectangle inner, Rectangle img, Design d) {
			bool over = Gui.Hover(inner);
			HoverPixel = over ? ToPixel(img) : new Point(-1, -1);

			if (over && Gui.Free && Gui.Scroll != 0) {
				int idx = Array.IndexOf(zoomSteps, zoom);
				if (idx < 0)
					idx = 6;
				int next = zoomSteps[Math.Clamp(idx + Math.Sign(Gui.Scroll), 0, zoomSteps.Length - 1)];
				if (next != zoom) {
					// keep the pixel under the cursor where it is
					Vector2 rel = (Gui.Mouse - new Vector2(img.X, img.Y)) / zoom;
					zoom = next;
					Rectangle moved = ImageRect(inner, d);
					Vector2 target = Gui.Mouse - rel * zoom;
					pan += target - new Vector2(moved.X, moved.Y);
				}
				Gui.Scroll = 0;
			}

			bool spaceDrag = Gui.KeyDown(Keys.Space) && !Gui.Typing;
			if (over && Gui.Free && (Gui.MiddleDown || spaceDrag && Gui.LeftPressed)) {
				Gui.Active = "canvas-pan";
				panGrab = pan;
				panMouse = Gui.Mouse;
				Gui.LeftPressed = false;
			}
			if (Gui.Active == "canvas-pan") {
				pan = panGrab + (Gui.Mouse - panMouse);
				return;
			}

			if (stroking) {
				bool held = strokeRight ? Gui.RightDown : Gui.LeftDown;
				if (!held) {
					EndStroke(d);
					return;
				}
				ContinueStroke(d, ToPixel(img));
				return;
			}

			if (over && Gui.Free && (Gui.LeftPressed || Gui.RightPressed)) {
				strokeRight = !Gui.LeftPressed;
				Gui.Active = "canvas";
				Gui.LeftPressed = Gui.RightPressed = false;
				BeginStroke(d, ToPixel(img));
			}
		}

		private static Point ToPixel(Rectangle img) {
			Vector2 rel = (Gui.Mouse - new Vector2(img.X, img.Y)) / zoom;
			return new Point((int)Math.Floor(rel.X), (int)Math.Floor(rel.Y));
		}

		private static bool Inside(Design d, Point p) => p.X >= 0 && p.Y >= 0 && p.X < d.Width && p.Y < d.Height;

		private static Color PaintColor => strokeTool == Tool.Eraser ? Color.Transparent : strokeRight ? StudioState.Secondary : StudioState.Primary;

		private static void BeginStroke(Design d, Point p) {
			StudioState.Playing = false;
			stroking = true;
			strokeTool = Gui.Alt ? Tool.Picker : StudioState.Tool;
			start = last = p;

			if (strokeTool == Tool.Picker) {
				Pick(d, p);
				return;
			}

			StudioState.SnapshotOnce("stroke" + Main.GameUpdateCount);
			Color[] cel = d.GetOrCreate(StudioState.Layer, StudioState.Frame, StudioState.Pose);
			switch (strokeTool) {
				case Tool.Pencil:
				case Tool.Eraser:
					Plot(d, cel, p.X, p.Y, PaintColor);
					break;
				case Tool.Fill:
					if (Inside(d, p) && Gui.Shift) {
						Pixels.ReplaceColor(cel, cel[p.Y * d.Width + p.X], PaintColor);
					}
					else if (Inside(d, p)) {
						Pixels.Flood(cel, d.Width, d.Height, p.X, p.Y, PaintColor);
						if (StudioState.Mirror)
							Pixels.Flood(cel, d.Width, d.Height, d.Width - 1 - p.X, p.Y, PaintColor);
					}
					break;
				case Tool.Line:
				case Tool.Rect:
					BuildShapePreview(d, p);
					return;
				case Tool.Move:
					moveOrigin = (Color[])cel.Clone();
					return;
			}
			StudioState.Changed();
		}

		private static void ContinueStroke(Design d, Point p) {
			if (p == last)
				return;
			switch (strokeTool) {
				case Tool.Picker:
					Pick(d, p);
					break;
				case Tool.Pencil:
				case Tool.Eraser: {
					Color[] cel = d.GetOrCreate(StudioState.Layer, StudioState.Frame, StudioState.Pose);
					Color c = PaintColor;
					Pixels.Line(last.X, last.Y, p.X, p.Y, (x, y) => Plot(d, cel, x, y, c));
					StudioState.Changed();
					break;
				}
				case Tool.Line:
				case Tool.Rect:
					BuildShapePreview(d, p);
					break;
				case Tool.Move: {
					Color[] cel = d.GetOrCreate(StudioState.Layer, StudioState.Frame, StudioState.Pose);
					Color[] moved = Pixels.Shifted(moveOrigin, d.Width, d.Height, p.X - start.X, p.Y - start.Y);
					Array.Copy(moved, cel, cel.Length);
					StudioState.Changed();
					break;
				}
			}
			last = p;
		}

		private static void EndStroke(Design d) {
			stroking = false;
			if (strokeTool is Tool.Line or Tool.Rect && overlay != null) {
				Color[] cel = d.GetOrCreate(StudioState.Layer, StudioState.Frame, StudioState.Pose);
				ForEachShapePixel(d, last, (x, y) => Plot(d, cel, x, y, PaintColor, brush: false));
				StudioState.Changed();
			}
			overlay = null;
			moveOrigin = null;
			if (strokeTool is Tool.Pencil or Tool.Fill or Tool.Line or Tool.Rect)
				StudioSettings.PushRecent(PaintColor);
		}

		private static void Plot(Design d, Color[] cel, int x, int y, Color c, bool brush = true) {
			int size = brush ? StudioState.Brush : 1;
			int x0 = x - (size - 1) / 2, y0 = y - (size - 1) / 2;
			for (int by = 0; by < size; by++) {
				for (int bx = 0; bx < size; bx++) {
					Set(d, cel, x0 + bx, y0 + by, c);
					if (StudioState.Mirror)
						Set(d, cel, d.Width - 1 - (x0 + bx), y0 + by, c);
				}
			}
		}

		private static void Set(Design d, Color[] cel, int x, int y, Color c) {
			if (x < 0 || y < 0 || x >= d.Width || y >= d.Height)
				return;
			cel[y * d.Width + x] = c;
		}

		private static void Pick(Design d, Point p) {
			if (!Inside(d, p))
				return;
			Color c = d.Flatten(StudioState.Frame, StudioState.Pose)[p.Y * d.Width + p.X];
			if (c.A == 0)
				return;
			if (strokeRight)
				StudioState.Secondary = c;
			else {
				StudioState.Primary = c;
				ColorPanel.SyncFromPrimary();
			}
		}

		private static void ForEachShapePixel(Design d, Point end, Action<int, int> plot) {
			if (strokeTool == Tool.Line) {
				Pixels.Line(start.X, start.Y, end.X, end.Y, (x, y) => {
					plot(x, y);
					if (StudioState.Mirror)
						plot(d.Width - 1 - x, y);
				});
				return;
			}
			int x0 = Math.Min(start.X, end.X), x1 = Math.Max(start.X, end.X);
			int y0 = Math.Min(start.Y, end.Y), y1 = Math.Max(start.Y, end.Y);
			bool filled = Gui.Shift;
			for (int y = y0; y <= y1; y++) {
				for (int x = x0; x <= x1; x++) {
					if (filled || x == x0 || x == x1 || y == y0 || y == y1) {
						plot(x, y);
						if (StudioState.Mirror)
							plot(d.Width - 1 - x, y);
					}
				}
			}
		}

		private static void BuildShapePreview(Design d, Point end) {
			overlay = d.NewBuffer();
			Color c = PaintColor;
			// erasing with a shape: show where it'll cut instead of drawing nothing
			if (c.A == 0)
				c = new Color(255, 255, 255, 120);
			ForEachShapePixel(d, end, (x, y) => {
				if (x >= 0 && y >= 0 && x < d.Width && y < d.Height)
					overlay[y * d.Width + x] = c;
			});
			overlayDirty = true;
		}

		#endregion

		#region drawing

		private static void RefreshTextures(Design d) {
			int frame = StudioState.Frame, pose = StudioState.Pose;
			if (cachedDoc != d || cachedRev != d.Revision || cachedFrame != frame || cachedPose != pose || cachedLayers != d.Layers.Count || layerTex.Length != d.Layers.Count) {
				cachedDoc = d;
				cachedRev = d.Revision;
				cachedFrame = frame;
				cachedPose = pose;
				cachedLayers = d.Layers.Count;

				if (layerTex.Length != d.Layers.Count) {
					var resized = new Texture2D[d.Layers.Count];
					for (int i = 0; i < layerTex.Length; i++) {
						if (i < resized.Length)
							resized[i] = layerTex[i];
						else
							layerTex[i]?.Dispose();
					}
					layerTex = resized;
				}
				for (int i = 0; i < d.Layers.Count; i++)
					Upload(ref layerTex[i], d, d.LayerPixels(d.Layers[i], frame, pose));

				if (d.FrameCount > 1) {
					int prev = (frame - 1 + d.FrameCount) % d.FrameCount, next = (frame + 1) % d.FrameCount;
					Upload(ref onionPrevTex, d, d.Flatten(prev, pose));
					Upload(ref onionNextTex, d, next != prev ? d.Flatten(next, pose) : null);
				}
			}
			if (overlayDirty && overlay != null) {
				Upload(ref overlayTex, d, overlay);
				overlayDirty = false;
			}
		}

		private static void Upload(ref Texture2D tex, Design d, Color[] px) {
			if (tex != null && (tex.Width != d.Width || tex.Height != d.Height)) {
				tex.Dispose();
				tex = null;
			}
			tex ??= new Texture2D(Main.instance.GraphicsDevice, d.Width, d.Height);
			tex.SetData(px != null ? Pixels.Premultiply(px) : new Color[d.Width * d.Height]);
		}

		private static void DrawReference(Rectangle img, Design d) {
			if (!d.IsWearable) {
				// retextures: the sprite as the game has it right now, under the drawing
				int type = Targets.Resolve(d.Kind, d.Target);
				var asset = type > 0 ? Targets.OriginalAsset(d.Kind, type) : null;
				if (asset == null || !asset.IsLoaded)
					return;
				Texture2D tex = asset.Value;
				int row = Math.Clamp(StudioState.Pose, 0, d.Rows - 1);
				var src = new Rectangle(0, row * d.Height, Math.Min(d.Width, tex.Width), Math.Min(d.Height, tex.Height - row * d.Height));
				if (src.Height > 0)
					Gui.Sb.Draw(tex, new Rectangle(img.X, img.Y, src.Width * zoom, src.Height * zoom), src, Color.White * StudioState.ReferenceAlpha);
				return;
			}
			Player dummy = StudioState.Dummy;
			var vp = dummy.GetModPlayer<Players.VanityPlayer>();
			Point off = Slots.FrameOffset(d.Slot);
			int pose = StudioState.Pose < 0 ? 0 : StudioState.Pose;
			StudioState.SetDummyPose(pose);
			// the 40x56 frame starts here; the renderer scales around the hitbox's bottom centre, 20px right and 52px down from it
			var frameTopLeft = new Vector2(img.X + off.X * zoom, img.Y + off.Y * zoom);
			Vector2 feet = frameTopLeft + new Vector2(20 * zoom, 52 * zoom);
			int savedDir = dummy.direction;
			dummy.direction = 1;
			vp.SuppressDesigns = true;
			try {
				Gui.DrawPlayer(dummy, feet, zoom, 1f - StudioState.ReferenceAlpha);
			}
			finally {
				vp.SuppressDesigns = false;
				dummy.direction = savedDir;
			}
		}

		private static void DrawGrid(Rectangle img, Design d) {
			if (StudioSettings.ShowGrid && zoom >= 6) {
				Color line = Color.White * 0.07f;
				for (int x = 1; x < d.Width; x++)
					Gui.Rect(new Rectangle(img.X + x * zoom, img.Y, 1, img.Height), line);
				for (int y = 1; y < d.Height; y++)
					Gui.Rect(new Rectangle(img.X, img.Y + y * zoom, img.Width, 1), line);
			}
			Point off = Slots.FrameOffset(d.Slot);
			if (off != Point.Zero) {
				// where the character's own 40x56 frame sits inside a bigger canvas
				var body = new Rectangle(img.X + off.X * zoom, img.Y + off.Y * zoom, 40 * zoom, 56 * zoom);
				Gui.Outline(body, Gui.Gold * 0.35f);
			}
		}

		private static void DrawCursor(Rectangle img, Design d) {
			Point p = HoverPixel;
			if (!Inside(d, p) || Gui.Active == "canvas-pan")
				return;
			int size = StudioState.Tool is Tool.Pencil or Tool.Eraser ? StudioState.Brush : 1;
			int x0 = p.X - (size - 1) / 2, y0 = p.Y - (size - 1) / 2;
			var r = new Rectangle(img.X + x0 * zoom, img.Y + y0 * zoom, size * zoom, size * zoom);
			Gui.Outline(r, Color.Black * 0.8f);
			r.Inflate(-1, -1);
			Gui.Outline(r, Color.White * 0.8f);
			if (StudioState.Mirror) {
				var m = new Rectangle(img.X + (d.Width - x0 - size) * zoom, img.Y + y0 * zoom, size * zoom, size * zoom);
				Gui.Outline(m, Color.White * 0.35f);
			}
		}

		private static void DrawStatus(Rectangle inner, Design d) {
			string where = Inside(d, HoverPixel) ? $"{HoverPixel.X}, {HoverPixel.Y}" : "";
			string pose = !d.IsWearable ? L.Get("SheetFrameN", Math.Max(0, StudioState.Pose) + 1) + " / " + d.Rows
				: StudioState.Pose < 0 ? L.Get("PoseBase") : L.Pose(StudioState.Pose) + (d.HasOverride(StudioState.Frame, StudioState.Pose) ? "" : " · " + L.Get("UsingBase"));
			Gui.Text($"{d.Width}x{d.Height}  x{zoom}   {where}", new Vector2(inner.X + 6, inner.Bottom - 4), Gui.Muted, 0.7f, 0f, 1f);
			Gui.Text(pose, new Vector2(inner.Right - 6, inner.Bottom - 4), StudioState.Pose < 0 ? Gui.Muted : Gui.Gold, 0.7f, 1f, 1f);
		}

		private static void DrawHint(Rectangle inner) {
			if (!Gui.Hover(inner) && Gui.Active != "canvas")
				return;
			string hint = L.Get("Hint" + StudioState.Tool) + "   " + L.Get("HintNav");
			Gui.Text(hint, new Vector2(inner.X + 6, inner.Y + 12), Gui.Muted * 0.9f, 0.65f, 0f, 0.5f);
		}

		#endregion

		public static void Unload() {
			foreach (var t in layerTex)
				t?.Dispose();
			layerTex = Array.Empty<Texture2D>();
			overlayTex?.Dispose();
			onionPrevTex?.Dispose();
			onionNextTex?.Dispose();
			overlayTex = onionPrevTex = onionNextTex = null;
			cachedDoc = null;
		}
	}
}
