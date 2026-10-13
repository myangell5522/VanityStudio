using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace VanityStudio.Core
{
	// All pixel buffers in the mod are straight (non-premultiplied) alpha.
	// Textures get premultiplied right before upload.
	public static class Pixels
	{
		public static Color Over(Color dst, Color src, float opacity = 1f) {
			float sa = src.A / 255f * opacity;
			if (sa <= 0f)
				return dst;
			float da = dst.A / 255f;
			float oa = sa + da * (1f - sa);
			if (oa <= 0f)
				return Color.Transparent;
			float r = (src.R * sa + dst.R * da * (1f - sa)) / oa;
			float g = (src.G * sa + dst.G * da * (1f - sa)) / oa;
			float b = (src.B * sa + dst.B * da * (1f - sa)) / oa;
			return new Color((int)(r + 0.5f), (int)(g + 0.5f), (int)(b + 0.5f), (int)(oa * 255f + 0.5f));
		}

		public static void Composite(Color[] dst, Color[] src, float opacity) {
			if (src == null || opacity <= 0f)
				return;
			for (int i = 0; i < dst.Length; i++) {
				if (src[i].A != 0)
					dst[i] = Over(dst[i], src[i], opacity);
			}
		}

		public static Color[] Premultiply(Color[] src) {
			var dst = new Color[src.Length];
			for (int i = 0; i < src.Length; i++) {
				Color c = src[i];
				if (c.A == 0)
					continue;
				if (c.A == 255) {
					dst[i] = c;
					continue;
				}
				dst[i] = new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
			}
			return dst;
		}

		public static void Unpremultiply(Color[] px) {
			for (int i = 0; i < px.Length; i++) {
				Color c = px[i];
				if (c.A == 0) {
					px[i] = Color.Transparent;
					continue;
				}
				if (c.A == 255)
					continue;
				px[i] = new Color(Math.Min(255, c.R * 255 / c.A), Math.Min(255, c.G * 255 / c.A), Math.Min(255, c.B * 255 / c.A), c.A);
			}
		}

		public static Color[] Shifted(Color[] src, int w, int h, int dx, int dy) {
			var dst = new Color[src.Length];
			for (int y = 0; y < h; y++) {
				int sy = y - dy;
				if (sy < 0 || sy >= h)
					continue;
				for (int x = 0; x < w; x++) {
					int sx = x - dx;
					if (sx < 0 || sx >= w)
						continue;
					dst[y * w + x] = src[sy * w + sx];
				}
			}
			return dst;
		}

		public static Color[] FlipH(Color[] src, int w, int h) {
			var dst = new Color[src.Length];
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					dst[y * w + x] = src[y * w + (w - 1 - x)];
			return dst;
		}

		// 1px outline around everything opaque, the classic pixel-art finishing pass
		public static Color[] Outline(Color[] src, int w, int h, Color color) {
			var dst = (Color[])src.Clone();
			for (int y = 0; y < h; y++) {
				for (int x = 0; x < w; x++) {
					if (src[y * w + x].A != 0)
						continue;
					bool edge = (x > 0 && src[y * w + x - 1].A > 0) || (x < w - 1 && src[y * w + x + 1].A > 0)
						|| (y > 0 && src[(y - 1) * w + x].A > 0) || (y < h - 1 && src[(y + 1) * w + x].A > 0);
					if (edge)
						dst[y * w + x] = color;
				}
			}
			return dst;
		}

		public static void ReplaceColor(Color[] px, Color from, Color to) {
			for (int i = 0; i < px.Length; i++) {
				if (Matches(px[i], from))
					px[i] = to;
			}
		}

		public static bool Same(Color[] a, Color[] b) {
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++) {
				// fully transparent pixels can carry junk rgb
				if (a[i].A == 0 && b[i].A == 0)
					continue;
				if (a[i] != b[i])
					return false;
			}
			return true;
		}

		public static bool IsEmpty(Color[] a) {
			if (a == null)
				return true;
			for (int i = 0; i < a.Length; i++) {
				if (a[i].A != 0)
					return false;
			}
			return true;
		}

		public static void Blit(Color[] dst, int dw, int dh, Color[] src, int sw, int sh, int sx, int sy, int cw, int ch, int dx, int dy) {
			for (int y = 0; y < ch; y++) {
				int ty = dy + y, fy = sy + y;
				if (ty < 0 || ty >= dh || fy < 0 || fy >= sh)
					continue;
				for (int x = 0; x < cw; x++) {
					int tx = dx + x, fx = sx + x;
					if (tx < 0 || tx >= dw || fx < 0 || fx >= sw)
						continue;
					dst[ty * dw + tx] = src[fy * sw + fx];
				}
			}
		}

		public static void BlitOver(Color[] dst, int dw, int dh, Color[] src, int sw, int sh, int sx, int sy, int cw, int ch, int dx, int dy) {
			for (int y = 0; y < ch; y++) {
				int ty = dy + y, fy = sy + y;
				if (ty < 0 || ty >= dh || fy < 0 || fy >= sh)
					continue;
				for (int x = 0; x < cw; x++) {
					int tx = dx + x, fx = sx + x;
					if (tx < 0 || tx >= dw || fx < 0 || fx >= sw)
						continue;
					Color c = src[fy * sw + fx];
					if (c.A != 0)
						dst[ty * dw + tx] = Over(dst[ty * dw + tx], c);
				}
			}
		}

		// Bresenham, calls plot for every pixel on the line including both ends.
		public static void Line(int x0, int y0, int x1, int y1, Action<int, int> plot) {
			int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
			int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
			int err = dx + dy;
			while (true) {
				plot(x0, y0);
				if (x0 == x1 && y0 == y1)
					break;
				int e2 = 2 * err;
				if (e2 >= dy) {
					err += dy;
					x0 += sx;
				}
				if (e2 <= dx) {
					err += dx;
					y0 += sy;
				}
			}
		}

		public static void Flood(Color[] px, int w, int h, int x, int y, Color with) {
			if (x < 0 || y < 0 || x >= w || y >= h)
				return;
			Color target = px[y * w + x];
			if (Matches(target, with))
				return;
			var stack = new Stack<int>();
			stack.Push(y * w + x);
			while (stack.Count > 0) {
				int i = stack.Pop();
				if (!Matches(px[i], target))
					continue;
				px[i] = with;
				int cx = i % w, cy = i / w;
				if (cx > 0) stack.Push(i - 1);
				if (cx < w - 1) stack.Push(i + 1);
				if (cy > 0) stack.Push(i - w);
				if (cy < h - 1) stack.Push(i + w);
			}
		}

		private static bool Matches(Color a, Color b) => (a.A == 0 && b.A == 0) || a == b;

		public static string ToHex(Color c) => c.A == 255 ? $"{c.R:X2}{c.G:X2}{c.B:X2}" : $"{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

		public static bool TryParseHex(string s, out Color c) {
			c = Color.White;
			s = s.Trim().TrimStart('#');
			if (s.Length != 6 && s.Length != 8)
				return false;
			try {
				byte r = Convert.ToByte(s.Substring(0, 2), 16);
				byte g = Convert.ToByte(s.Substring(2, 2), 16);
				byte b = Convert.ToByte(s.Substring(4, 2), 16);
				byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
				c = new Color(r, g, b, a);
				return true;
			}
			catch (FormatException) {
				return false;
			}
		}
	}
}
