using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace VanityStudio.Core
{
	/// <summary>
	/// Getting pixels in and out: PNG files, vanilla-layout sprite sheets and the textures of armor the player is wearing.
	/// A "sheet" here always means the vanilla layout: 20 legacy frames stacked vertically, one column per animation frame.
	/// </summary>
	public static class SheetIO
	{
		#region png

		public static void WritePng(string path, Color[] px, int w, int h) {
			using var fs = File.Create(path);
			WritePng(fs, px, w, h);
		}

		public static byte[] PngBytes(Color[] px, int w, int h) {
			using var ms = new MemoryStream();
			WritePng(ms, px, w, h);
			return ms.ToArray();
		}

		public static void WritePng(Stream fs, Color[] px, int w, int h) {
			fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

			var ihdr = new byte[13];
			WriteBE(ihdr, 0, w);
			WriteBE(ihdr, 4, h);
			ihdr[8] = 8; // bit depth
			ihdr[9] = 6; // RGBA
			Chunk(fs, "IHDR", ihdr);

			using (var raw = new MemoryStream()) {
				using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true)) {
					var row = new byte[w * 4 + 1];
					for (int y = 0; y < h; y++) {
						row[0] = 0;
						for (int x = 0; x < w; x++) {
							Color c = px[y * w + x];
							int o = 1 + x * 4;
							row[o] = c.R;
							row[o + 1] = c.G;
							row[o + 2] = c.B;
							row[o + 3] = c.A;
						}
						z.Write(row);
					}
				}
				Chunk(fs, "IDAT", raw.ToArray());
			}
			Chunk(fs, "IEND", Array.Empty<byte>());
		}

		private static void Chunk(Stream s, string type, byte[] data) {
			var len = new byte[4];
			WriteBE(len, 0, data.Length);
			s.Write(len);
			var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
			s.Write(typeBytes);
			s.Write(data);
			uint crc = Crc(typeBytes, 0xFFFFFFFFu);
			crc = Crc(data, crc) ^ 0xFFFFFFFFu;
			var c = new byte[4];
			WriteBE(c, 0, (int)crc);
			s.Write(c);
		}

		private static uint[] crcTable;

		private static uint Crc(byte[] data, uint crc) {
			if (crcTable == null) {
				crcTable = new uint[256];
				for (uint n = 0; n < 256; n++) {
					uint c = n;
					for (int k = 0; k < 8; k++)
						c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
					crcTable[n] = c;
				}
			}
			foreach (byte b in data)
				crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
			return crc;
		}

		private static void WriteBE(byte[] buf, int at, int v) {
			buf[at] = (byte)(v >> 24);
			buf[at + 1] = (byte)(v >> 16);
			buf[at + 2] = (byte)(v >> 8);
			buf[at + 3] = (byte)v;
		}

		public static Color[] ReadPng(string path, out int w, out int h) {
			using var src = File.OpenRead(path);
			using var raw = new MemoryStream();
			if (!ImageIO.ToRaw(src, raw))
				throw new InvalidDataException("Not an image FNA can read");
			raw.Position = 0;
			byte[] bytes = ImageIO.ReadRaw(new MemoryStream(raw.ToArray()), out w, out h);
			var px = new Color[w * h];
			for (int i = 0; i < px.Length; i++)
				px[i] = new Color(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
			return px;
		}

		#endregion

		#region export

		/// <summary>
		/// Writes the design in the layouts the game itself uses, so the files can go straight into a mod or a resource pack:
		/// frames stacked vertically, one file per animation frame, plus a tModLoader body sheet and a glowmask when they apply.
		/// Returns the folder.
		/// </summary>
		public static string Export(Design d) {
			string name = DesignLibrary.SafeFileName(d.Name);
			string folder = Path.Combine(DesignLibrary.ExportsDir, name + "_" + (d.IsWearable ? d.Slot.ToString() : d.Kind.ToString()));
			Directory.CreateDirectory(folder);
			string Suffix(int f) => d.FrameCount > 1 ? "_frame" + (f + 1) : "";

			for (int f = 0; f < d.FrameCount; f++) {
				if (d.IsWearable) {
					string stem = name + "_" + d.Slot;
					Color[] sheet = WornSheet(d, f, out int sw, out int sh);
					WritePng(Path.Combine(folder, stem + Suffix(f) + ".png"), sheet, sw, sh);
					if (d.Width != 40) {
						// vanilla back/front accessories are 40 wide; this is the middle of the bigger canvas
						Color[] narrow = Crop(sheet, sw, sh, (d.Width - 40) / 2, d.Height, 40, 56);
						WritePng(Path.Combine(folder, stem + "_40x56" + Suffix(f) + ".png"), narrow, 40, 56 * Design.PoseCount);
					}
					if (d.Slot == DesignSlot.Body && !Main.dedServ) {
						Color[] comp = CompositeBody(d, f, out int cw, out int ch);
						if (comp != null)
							WritePng(Path.Combine(folder, stem + "_Composite" + Suffix(f) + ".png"), comp, cw, ch);
					}
					Color[] glow = GlowSheet(d, f, Design.PoseCount, out bool any);
					if (any)
						WritePng(Path.Combine(folder, stem + "_Glow" + Suffix(f) + ".png"), glow, d.Width, d.Height * Design.PoseCount);
				}
				else {
					int sh = d.Height * d.Rows;
					var sheet = new Color[d.Width * sh];
					for (int r = 0; r < d.Rows; r++)
						Pixels.Blit(sheet, d.Width, sh, d.Flatten(f, r), d.Width, d.Height, 0, 0, d.Width, d.Height, 0, r * d.Height);
					WritePng(Path.Combine(folder, name + Suffix(f) + ".png"), sheet, d.Width, sh);
					Color[] glow = GlowSheet(d, f, d.Rows, out bool any);
					if (any)
						WritePng(Path.Combine(folder, name + "_Glow" + Suffix(f) + ".png"), glow, d.Width, sh);
				}
			}
			DesignLibrary.WriteFile(Path.Combine(folder, name + ".vsd"), d);
			return folder;
		}

		private static Color[] WornSheet(Design d, int frame, out int w, out int h) {
			w = d.Width;
			h = d.Height * Design.PoseCount;
			var sheet = new Color[w * h];
			for (int pose = 0; pose < Design.PoseCount; pose++)
				Pixels.Blit(sheet, w, h, d.WornFrame(frame, pose), d.Width, d.Height, 0, 0, d.Width, d.Height, 0, pose * d.Height);
			return sheet;
		}

		private static Color[] Crop(Color[] sheet, int sw, int sh, int x, int cellH, int cw, int ch) {
			var dst = new Color[cw * ch * Design.PoseCount];
			for (int pose = 0; pose < Design.PoseCount; pose++) {
				int y = pose * cellH + (cellH - ch) / 2;
				Pixels.Blit(dst, cw, ch * Design.PoseCount, sheet, sw, sh, x, y, cw, ch, 0, pose * ch);
			}
			return dst;
		}

		// Only the glowing layers, in the same layout: exactly what a glowmask texture is.
		private static Color[] GlowSheet(Design d, int frame, int rows, out bool any) {
			any = d.Layers.Any(l => l.Visible && (l.Glow || l.Pulse));
			int h = d.Height * rows;
			var sheet = new Color[d.Width * h];
			if (!any)
				return sheet;
			for (int r = 0; r < rows; r++) {
				var cell = d.NewBuffer();
				foreach (DesignLayer l in d.Layers) {
					if (l.Visible && (l.Glow || l.Pulse))
						Pixels.Composite(cell, d.LayerPixels(l, frame, r), l.Opacity);
				}
				Pixels.Blit(sheet, d.Width, h, cell, d.Width, d.Height, 0, 0, d.Width, d.Height, 0, r * d.Height);
			}
			return sheet;
		}

		/// <summary>
		/// tModLoader body textures use the 1.4 "composite" sheet: torso, shoulders and each arm position as separate
		/// 40x56 cells, so the arms can swing on their own. Our drawings are whole poses, so they are cut apart along the
		/// shape of the vanilla body: pixels over an arm go to that arm's cell, everything else to the torso.
		/// It's a starting point, sleeves that overlap the chest may need a touch-up.
		/// </summary>
		public static Color[] CompositeBody(Design d, int frame, out int w, out int h) {
			w = 360;
			h = 224;
			var skin = new List<(Color[] px, int w, int h)>();
			foreach (int id in new[] { PlayerTextureID.TorsoSkin, PlayerTextureID.Undershirt, PlayerTextureID.Shirt, PlayerTextureID.ArmSkin, PlayerTextureID.ArmUndershirt, PlayerTextureID.ArmHand, PlayerTextureID.ArmShirt, PlayerTextureID.Hands }) {
				Color[] px = Grab(TextureAssets.Players[0, id], out int tw, out int th);
				if (px != null)
					skin.Add((px, tw, th));
			}
			if (skin.Count == 0)
				return null;
			w = skin[0].w;
			h = skin[0].h;
			int W = w, H = h;

			bool[] Mask(Point cell) {
				var m = new bool[40 * 56];
				foreach (var (px, tw, th) in skin) {
					for (int y = 0; y < 56; y++) {
						for (int x = 0; x < 40; x++) {
							int sx = cell.X * 40 + x, sy = cell.Y * 56 + y;
							if (sx < tw && sy < th && px[sy * tw + sx].A > 0)
								m[y * 40 + x] = true;
						}
					}
				}
				// one pixel of slack so sleeves a bit wider than the arm still count as arm
				var grown = (bool[])m.Clone();
				for (int y = 0; y < 56; y++) {
					for (int x = 0; x < 40; x++) {
						if (!m[y * 40 + x])
							continue;
						if (x > 0) grown[y * 40 + x - 1] = true;
						if (x < 39) grown[y * 40 + x + 1] = true;
						if (y > 0) grown[(y - 1) * 40 + x] = true;
						if (y < 55) grown[(y + 1) * 40 + x] = true;
					}
				}
				return grown;
			}

			// the drawing for a pose without the walk bob, centred on the 40x56 body frame
			Color[] Pose(int pose) {
				Color[] worn = d.WornFrame(frame, pose);
				var px = new Color[40 * 56];
				Point off = Slots.FrameOffset(d.Slot);
				Pixels.Blit(px, 40, 56, worn, d.Width, d.Height, off.X, off.Y, 40, 56, 0, 0);
				return Pixels.Shifted(px, 40, 56, 0, -Slots.BobFor(pose));
			}

			var sheet = new Color[W * H];
			void Put(Point cell, Color[] px) => Pixels.Blit(sheet, W, H, px, 40, 56, 0, 0, 40, 56, cell.X * 40, cell.Y * 56);

			// torso: everything that isn't an arm, idle and jump versions, same for both body types
			foreach (var (torso, pose) in new[] { (new Point(0, 0), 0), (new Point(1, 0), 5) }) {
				Color[] px = Pose(pose);
				Point front = FrontArmFrame(pose, out _);
				bool[] fm = Mask(front), bm = Mask(new Point(front.X, front.Y + 2));
				var t = new Color[px.Length];
				for (int i = 0; i < px.Length; i++) {
					if (!fm[i] && !bm[i])
						t[i] = px[i];
				}
				Put(torso, t);
				Put(new Point(torso.X, torso.Y + 2), t);
			}

			// arms: each arm cell is cut from the first pose that uses it
			var done = new HashSet<Point>();
			for (int pose = 0; pose < Design.PoseCount; pose++) {
				Point front = FrontArmFrame(pose, out _);
				if (!done.Add(front))
					continue;
				Point back = new(front.X, front.Y + 2);
				Color[] px = Pose(pose);
				bool[] fm = Mask(front), bm = Mask(back);
				var f = new Color[px.Length];
				var b = new Color[px.Length];
				for (int i = 0; i < px.Length; i++) {
					if (fm[i])
						f[i] = px[i];
					else if (bm[i])
						b[i] = px[i];
				}
				Put(front, f);
				Put(back, b);
			}
			return sheet;
		}

		#endregion

		#region import

		/// <summary>
		/// Puts an image into a new layer. Understands vanilla sheets (frames stacked vertically, and wider ones with one
		/// column per animation frame); anything else is pasted into the current frame.
		/// </summary>
		public static int ImportImage(Design d, Color[] px, int w, int h, string layerName, int atFrame, int atPose = Design.BasePose) {
			if (d.Layers.Count >= Design.MaxLayers)
				return -1;
			var layer = new DesignLayer { Name = layerName };
			d.Layers.Add(layer);
			int layerIndex = d.Layers.Count - 1;
			int rows = d.Rows;

			int cellH = 0, cellW = 0;
			if (h % rows == 0) {
				int ch = h / rows;
				if (d.IsWearable && ch == 56 && w % 40 == 0) {
					cellH = 56;
					cellW = 40;
				}
				else if (ch == d.Height && w % d.Width == 0) {
					cellH = d.Height;
					cellW = d.Width;
				}
			}

			if (cellH == 0) {
				var cel = d.NewBuffer();
				int ox = Math.Max(0, (d.Width - w) / 2), oy = Math.Max(0, (d.Height - h) / 2);
				Pixels.Blit(cel, d.Width, d.Height, px, w, h, 0, 0, Math.Min(w, d.Width), Math.Min(h, d.Height), ox, oy);
				int pose = d.IsWearable ? atPose : Math.Max(0, atPose);
				layer.Cels[new CelKey(atFrame, pose)] = cel;
				return layerIndex;
			}

			int columns = Math.Min(w / cellW, Design.MaxFrames);
			while (d.FrameCount < atFrame + columns && d.FrameCount < Design.MaxFrames)
				d.InsertFrame(d.FrameCount, -1);

			int dx = (d.Width - cellW) / 2, dy = (d.Height - cellH) / 2;
			for (int col = 0; col < columns && atFrame + col < d.FrameCount; col++) {
				int frame = atFrame + col;
				Color[] Cell(int row) {
					var c = d.NewBuffer();
					Pixels.Blit(c, d.Width, d.Height, px, w, h, col * cellW, row * cellH, cellW, cellH, dx, dy);
					return c;
				}
				if (!d.IsWearable) {
					// sprite sheets: every frame of the sheet is its own drawing
					for (int r = 0; r < rows; r++)
						layer.Cels[new CelKey(frame, r)] = Cell(r);
					continue;
				}
				Color[] baseCel = Cell(0);
				layer.Cels[new CelKey(frame, Design.BasePose)] = baseCel;
				for (int pose = 1; pose < Design.PoseCount; pose++) {
					Color[] cel = Cell(pose);
					// vanilla sheets have the walk bob baked in, so only keep rows that really differ
					Color[] expected = d.Bobs ? Pixels.Shifted(baseCel, d.Width, d.Height, 0, Slots.BobFor(pose)) : baseCel;
					if (!Pixels.Same(cel, expected))
						layer.Cels[new CelKey(frame, pose)] = cel;
				}
			}
			return layerIndex;
		}

		/// <summary>The current sprite of an item / NPC / projectile, straight from the game (resource packs included).</summary>
		public static Color[] SheetFromTarget(DesignKind kind, int type, out int w, out int h) {
			w = h = 0;
			Asset<Texture2D> asset = Targets.OriginalAsset(kind, type);
			return asset == null ? null : Grab(asset, out w, out h);
		}

		/// <summary>Builds a vanilla 40x1120 sheet out of whatever the player is showing in that slot.</summary>
		public static Color[] SheetFromEquipped(Player p, DesignSlot slot, out string itemName) {
			itemName = null;
			switch (slot) {
				case DesignSlot.Head: {
					Item item = Visible(p, 10, 0, i => i.headSlot > 0);
					if (item == null)
						return null;
					itemName = item.Name;
					Main.instance.LoadArmorHead(item.headSlot);
					return Legacy(TextureAssets.ArmorHead[item.headSlot]);
				}
				case DesignSlot.Legs: {
					Item item = Visible(p, 12, 2, i => i.legSlot > 0);
					if (item == null)
						return null;
					itemName = item.Name;
					Main.instance.LoadArmorLegs(item.legSlot);
					return Legacy(TextureAssets.ArmorLeg[item.legSlot]);
				}
				case DesignSlot.Body: {
					Item item = Visible(p, 11, 1, i => i.bodySlot > 0);
					if (item == null)
						return null;
					itemName = item.Name;
					Main.instance.LoadArmorBody(item.bodySlot);
					// LoadArmorBody only requests the composite sheet for bodies that use the new framing
					Asset<Texture2D> composite = TextureAssets.ArmorBodyComposite[item.bodySlot];
					if (composite == null || composite.State == AssetState.NotLoaded)
						return Legacy(p.Male ? TextureAssets.ArmorBody[item.bodySlot] : TextureAssets.FemaleBody[item.bodySlot]);
					return ComposeBody(composite, p.Male);
				}
				case DesignSlot.Front:
				case DesignSlot.Back: {
					Item item = VisibleAccessory(p, slot == DesignSlot.Front ? i => i.frontSlot > 0 : i => i.backSlot > 0);
					if (item == null)
						return null;
					itemName = item.Name;
					if (slot == DesignSlot.Front) {
						Main.instance.LoadAccFront(item.frontSlot);
						return Legacy(TextureAssets.AccFront[item.frontSlot]);
					}
					Main.instance.LoadAccBack(item.backSlot);
					return Legacy(TextureAssets.AccBack[item.backSlot]);
				}
			}
			return null;
		}

		private static Item Visible(Player p, int vanity, int armor, Func<Item, bool> has) {
			if (!p.armor[vanity].IsAir && has(p.armor[vanity]))
				return p.armor[vanity];
			if (!p.armor[armor].IsAir && has(p.armor[armor]))
				return p.armor[armor];
			return null;
		}

		private static Item VisibleAccessory(Player p, Func<Item, bool> has) {
			for (int i = 13; i < 20; i++) {
				if (i < p.armor.Length && !p.armor[i].IsAir && has(p.armor[i]))
					return p.armor[i];
			}
			for (int i = 3; i < 10; i++) {
				if (!p.armor[i].IsAir && has(p.armor[i]) && !p.hideVisibleAccessory[i])
					return p.armor[i];
			}
			return null;
		}

		internal static Color[] Grab(Asset<Texture2D> asset, out int w, out int h) {
			w = h = 0;
			if (asset == null)
				return null;
			if (!asset.IsLoaded)
				asset.Wait?.Invoke();
			Texture2D tex = asset.Value;
			if (tex == null)
				return null;
			w = tex.Width;
			h = tex.Height;
			var px = new Color[w * h];
			tex.GetData(px);
			Pixels.Unpremultiply(px);
			return px;
		}

		private static Color[] Legacy(Asset<Texture2D> asset) {
			Color[] px = Grab(asset, out int w, out int h);
			if (px == null)
				return null;
			var sheet = new Color[40 * 1120];
			Pixels.Blit(sheet, 40, 1120, px, w, h, 0, 0, Math.Min(40, w), Math.Min(1120, h), 0, 0);
			return sheet;
		}

		// Composite body sheets are 9x4 grids of 40x56 parts (torso, shoulders, arms).
		// Glue them back into the old one-frame-per-pose layout.
		private static Color[] ComposeBody(Asset<Texture2D> asset, bool male) {
			Color[] px = Grab(asset, out int w, out int h);
			if (px == null)
				return null;
			var sheet = new Color[40 * 1120];
			int femaleShift = male ? 0 : 2;
			for (int pose = 0; pose < Design.PoseCount; pose++) {
				Point front = FrontArmFrame(pose, out bool shoulderOverArm);
				Point back = new(front.X, front.Y + 2);
				Point torso = new(pose == 5 ? 1 : 0, 0 + femaleShift);
				Point backShoulder = new(1, 1 + femaleShift);
				Point frontShoulder = new(0, 1 + femaleShift);

				var cell = new Color[40 * 56];
				void Part(Point p) => Pixels.BlitOver(cell, 40, 56, px, w, h, p.X * 40, p.Y * 56, 40, 56, 0, 0);
				Part(back);
				Part(backShoulder);
				Part(torso);
				if (shoulderOverArm) {
					Part(front);
					Part(frontShoulder);
				}
				else {
					Part(frontShoulder);
					Part(front);
				}
				cell = Pixels.Shifted(cell, 40, 56, 0, Slots.BobFor(pose));
				Pixels.Blit(sheet, 40, 1120, cell, 40, 56, 0, 0, 40, 56, 0, pose * 56);
			}
			return sheet;
		}

		// mirrors PlayerDrawSet.CreateCompositeData
		private static Point FrontArmFrame(int pose, out bool shoulderOverArm) {
			shoulderOverArm = true;
			switch (pose) {
				case 0: return new Point(2, 0);
				case 1: shoulderOverArm = false; return new Point(3, 0);
				case 2: shoulderOverArm = false; return new Point(4, 0);
				case 3: return new Point(5, 0);
				case 4: return new Point(6, 0);
				case 5: shoulderOverArm = false; return new Point(2, 1);
				case 6: return new Point(3, 1);
				case 7:
				case 8:
				case 9:
				case 10: return new Point(4, 1);
				case 11:
				case 12:
				case 13: return new Point(3, 1);
				case 14: return new Point(5, 1);
				case 15:
				case 16: return new Point(6, 1);
				case 17: return new Point(5, 1);
				default: return new Point(3, 1);
			}
		}

		#endregion
	}
}
