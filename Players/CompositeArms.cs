using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using VanityStudio.Core;

namespace VanityStudio.Players
{
	/// <summary>
	/// Shortswords, guitars, golf clubs, yoyos and friends don't use the old swing frames: vanilla turns the arm freely
	/// around the shoulder ("composite arms"). Designs are drawn on the old 20-frame sheet, so for those moments the body
	/// design is split at draw time: the sleeve is cut out of the idle pose and turned with the real arm, and the hole it
	/// leaves in the torso is patched from a pose where the arm isn't in the way.
	///
	/// Nothing is saved or changed in the design itself; this only affects drawing, so every existing design keeps working.
	/// Which pixels count as "arm" comes from the player's own vanilla body sheets, so it follows resource packs too.
	/// </summary>
	internal static class CompositeArms
	{
		private const int W = 40, H = 56;

		// composite player sheets: torso skin, undershirt, shirt, arm skin, undershirt arm, hand, shirt arm
		private static readonly int[] skinSheets = { 3, 4, 6, 7, 8, 9, 13 };

		private static readonly Dictionary<int, List<(Color[] px, int width)>> sheets = new();
		private static readonly Dictionary<(int, int, int), bool[]> tiles = new();
		private static readonly Dictionary<(int, bool, int, int), bool[]> regions = new();
		private static readonly Dictionary<(int, int, int, int), Point> alignments = new();

		// where to look for torso pixels hidden under an arm: idle first, then poses with the arm up or out of the way
		private static readonly int[] patchOrder = { 0, 5, 1, 2, 3, 4, 6, 14, 15, 16, 7, 8, 9, 10, 11, 12, 13, 17, 18, 19 };

		public const int CutFront = 1, CutBack = 2;
		private const int KindFrontArm = 8, KindBackArm = 9;

		public static void Unload() {
			sheets.Clear();
			tiles.Clear();
			regions.Clear();
			alignments.Clear();
		}

		/// <summary>Only the body slot has sleeves, and only the new framing code has free arms.</summary>
		public static bool Applies(DesignSlot slot, ref PlayerDrawSet info) {
			if (slot != DesignSlot.Body || !info.usesCompositeTorso)
				return false;
			Player p = info.drawPlayer;
			return (p.compositeFrontArm.enabled || p.compositeBackArm.enabled) && Sheets(info.skinVar).Count > 0;
		}

		public static int Cuts(Player p) => (p.compositeFrontArm.enabled ? CutFront : 0) | (p.compositeBackArm.enabled ? CutBack : 0);

		public static int StretchRow(Rectangle armFrame) => armFrame.Height > 0 ? Math.Clamp(armFrame.Y / armFrame.Height, 0, 3) : 0;

		#region pieces

		public static Texture2D Torso(BakedDesign bake, BakedPass pass, int frame, int pose, int cuts, int skin, bool male) =>
			Piece(pass, Key(cuts, skin, male, pose, frame), () => BuildTorso(bake, pass, frame, pose, cuts, skin, male));

		public static Texture2D FrontArm(BakedDesign bake, BakedPass pass, int frame, int stretch, int skin, bool male) =>
			Piece(pass, Key(KindFrontArm, skin, male, stretch, frame), () => BuildArm(bake, pass, frame, 7, stretch, skin, male));

		public static Texture2D BackArm(BakedDesign bake, BakedPass pass, int frame, int stretch, int skin, bool male) =>
			Piece(pass, Key(KindBackArm, skin, male, stretch, frame), () => BuildArm(bake, pass, frame, 8, stretch, skin, male));

		private static int Key(int kind, int skin, bool male, int pose, int frame) =>
			(((kind * 64 + (skin & 63)) * 2 + (male ? 1 : 0)) * 32 + pose) * 64 + (frame & 63);

		private static Texture2D Piece(BakedPass pass, int key, Func<Color[]> build) {
			if (pass.Pieces.TryGetValue(key, out Texture2D tex) && !tex.IsDisposed)
				return tex;
			Color[] px = build();
			tex = new Texture2D(Main.graphics.GraphicsDevice, W, H);
			tex.SetData(Pixels.Premultiply(px));
			pass.Pieces[key] = tex;
			return tex;
		}

		private static Color[] CellOf(BakedDesign bake, BakedPass pass, int frame, int pose, out int shift, out int drawnIn) {
			Rectangle src = bake.Source(frame, pose, out bool overridden);
			var px = new Color[W * H];
			Pixels.Blit(px, W, H, pass.Atlas, bake.AtlasWidth, bake.AtlasHeight, src.X, src.Y, W, H, 0, 0);
			// inherited poses reuse the base drawing, which is drawn over the idle figure and bobbed only when drawn
			shift = overridden ? Slots.BobFor(pose) : 0;
			drawnIn = overridden ? pose : 0;
			return px;
		}

		private static Color[] BuildTorso(BakedDesign bake, BakedPass pass, int frame, int pose, int cuts, int skin, bool male) {
			Color[] cell = CellOf(bake, pass, frame, pose, out int shift, out int drawnIn);
			bool[] cut = Cut(skin, male, drawnIn, cuts);
			var result = (Color[])cell.Clone();
			var others = new Dictionary<int, (Color[] px, int shift, bool[] cut)>();
			for (int y = 0; y < H; y++) {
				int ty = y - shift;
				if (ty < 0 || ty >= H)
					continue;
				for (int x = 0; x < W; x++) {
					if (!cut[ty * W + x])
						continue;
					result[y * W + x] = Color.Transparent;
					foreach (int q in patchOrder) {
						if (q == pose)
							continue;
						if (!others.TryGetValue(q, out var o)) {
							Color[] px = CellOf(bake, pass, frame, q, out int qs, out int qd);
							o = (px, qs, Cut(skin, male, qd, cuts));
							others[q] = o;
						}
						if (o.cut[ty * W + x])
							continue;
						int qy = ty + o.shift;
						if (qy >= 0 && qy < H)
							result[y * W + x] = o.px[qy * W + x];
						break;
					}
				}
			}
			return result;
		}

		// the sleeve from the idle drawing, moved onto vanilla's free-arm frame so it turns around the same shoulder
		private static Color[] BuildArm(BakedDesign bake, BakedPass pass, int frame, int column, int stretch, int skin, bool male) {
			Color[] cell = CellOf(bake, pass, frame, 0, out int shift, out _);
			bool[] region = Region(skin, male, 0, front: true);
			Point d = Align(skin, column, stretch);
			var result = new Color[W * H];
			for (int ty = 0; ty < H; ty++) {
				int cy = ty + shift, oy = ty + d.Y;
				if (cy < 0 || cy >= H || oy < 0 || oy >= H)
					continue;
				for (int x = 0; x < W; x++) {
					int ox = x + d.X;
					if (region[ty * W + x] && ox >= 0 && ox < W)
						result[oy * W + ox] = cell[cy * W + x];
				}
			}
			return result;
		}

		#endregion

		#region masks

		private static List<(Color[] px, int width)> Sheets(int skin) {
			if (sheets.TryGetValue(skin, out var list))
				return list;
			list = new List<(Color[], int)>();
			if (skin >= 0 && skin < TextureAssets.Players.GetLength(0)) {
				foreach (int i in skinSheets) {
					Texture2D t = TextureAssets.Players[skin, i]?.Value;
					// a resource pack could swap in something that isn't laid out like a composite sheet
					if (t == null || t.Width < W * 9 || t.Height < H * 4)
						continue;
					var px = new Color[t.Width * t.Height];
					t.GetData(px);
					list.Add((px, t.Width));
				}
			}
			sheets[skin] = list;
			return list;
		}

		private static bool[] Tile(int skin, int tx, int ty) {
			if (tiles.TryGetValue((skin, tx, ty), out bool[] m))
				return m;
			m = new bool[W * H];
			foreach (var (px, width) in Sheets(skin)) {
				int height = px.Length / width;
				for (int y = 0; y < H; y++) {
					int sy = ty * H + y;
					if (sy >= height)
						break;
					for (int x = 0; x < W; x++) {
						if (px[sy * width + tx * W + x].A > 0)
							m[y * W + x] = true;
					}
				}
			}
			tiles[(skin, tx, ty)] = m;
			return m;
		}

		private static bool[] Torso(int skin, bool male, int pose) {
			int row = male ? 0 : 2;
			return Or(Tile(skin, pose == 5 ? 1 : 0, row), Tile(skin, 0, row + 1), Tile(skin, 1, row + 1));
		}

		/// <summary>
		/// Pixels that belong to an arm in a frame drawn over <paramref name="pose"/>: the vanilla arm itself, plus a little
		/// room around it for sleeves that stick out, but never anything that's torso.
		/// </summary>
		private static bool[] Region(int skin, bool male, int pose, bool front) {
			var key = (skin, male, pose, front ? 0 : 1);
			if (regions.TryGetValue(key, out bool[] r))
				return r;
			Point f = SheetIO.FrontArmFrame(pose, out _);
			bool[] torso = Torso(skin, male, pose);
			bool[] frontArm = Tile(skin, f.X, f.Y);
			bool[] frontRegion = Or(frontArm, AndNot(Grow(frontArm, 2), torso));
			if (front) {
				r = frontRegion;
			}
			else {
				// the back arm sits behind everything, so only what peeks out past the torso and the front arm is its
				bool[] backArm = Tile(skin, f.X, f.Y + 2);
				r = AndNot(AndNot(Or(backArm, Grow(backArm, 2)), torso), frontRegion);
			}
			regions[key] = r;
			return r;
		}

		private static bool[] Cut(int skin, bool male, int pose, int cuts) {
			bool[] none = new bool[W * H];
			bool[] r = none;
			if ((cuts & CutFront) != 0)
				r = Or(r, Region(skin, male, pose, true));
			if ((cuts & CutBack) != 0)
				r = Or(r, Region(skin, male, pose, false));
			return r;
		}

		// the idle arm and vanilla's free arm aren't drawn at quite the same spot in their frames; slide one onto the other
		private static Point Align(int skin, int column, int stretch) {
			var key = (skin, column, stretch, 0);
			if (alignments.TryGetValue(key, out Point best))
				return best;
			bool[] from = Tile(skin, 2, 0), to = Tile(skin, column, stretch);
			int bestScore = -1;
			for (int dy = -8; dy <= 8; dy++) {
				for (int dx = -8; dx <= 8; dx++) {
					int score = 0;
					for (int y = 0; y < H; y++) {
						int oy = y + dy;
						if (oy < 0 || oy >= H)
							continue;
						for (int x = 0; x < W; x++) {
							int ox = x + dx;
							if (ox >= 0 && ox < W && from[y * W + x] && to[oy * W + ox])
								score++;
						}
					}
					// ties go to the smaller move
					if (score > bestScore || score == bestScore && Math.Abs(dx) + Math.Abs(dy) < Math.Abs(best.X) + Math.Abs(best.Y)) {
						bestScore = score;
						best = new Point(dx, dy);
					}
				}
			}
			alignments[key] = best;
			return best;
		}

		private static bool[] Or(params bool[][] masks) {
			var r = new bool[W * H];
			foreach (bool[] m in masks) {
				for (int i = 0; i < r.Length; i++)
					r[i] |= m[i];
			}
			return r;
		}

		private static bool[] AndNot(bool[] a, bool[] b) {
			var r = new bool[W * H];
			for (int i = 0; i < r.Length; i++)
				r[i] = a[i] && !b[i];
			return r;
		}

		private static bool[] Grow(bool[] m, int by) {
			var r = (bool[])m.Clone();
			for (int pass = 0; pass < by; pass++) {
				var next = (bool[])r.Clone();
				for (int y = 0; y < H; y++) {
					for (int x = 0; x < W; x++) {
						if (r[y * W + x])
							continue;
						if (x > 0 && r[y * W + x - 1] || x < W - 1 && r[y * W + x + 1] || y > 0 && r[(y - 1) * W + x] || y < H - 1 && r[(y + 1) * W + x])
							next[y * W + x] = true;
					}
				}
				r = next;
			}
			return r;
		}

		#endregion
	}
}
