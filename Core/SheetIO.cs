using System;
using System.IO;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
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

		public static string Export(Design d) {
			Directory.CreateDirectory(DesignLibrary.ExportsDir);
			string baseName = DesignLibrary.SafeFileName(d.Name) + "_" + d.Slot;
			int sw = d.Width * d.FrameCount, sh = d.Height * Design.PoseCount;
			var sheet = new Color[sw * sh];
			for (int f = 0; f < d.FrameCount; f++) {
				for (int pose = 0; pose < Design.PoseCount; pose++) {
					Color[] cell = d.WornFrame(f, pose);
					Pixels.Blit(sheet, sw, sh, cell, d.Width, d.Height, 0, 0, d.Width, d.Height, f * d.Width, pose * d.Height);
				}
			}
			string png = Path.Combine(DesignLibrary.ExportsDir, baseName + ".png");
			WritePng(png, sheet, sw, sh);
			DesignLibrary.WriteFile(Path.Combine(DesignLibrary.ExportsDir, baseName + ".vsd"), d);
			return png;
		}

		#endregion

		#region import

		/// <summary>
		/// Puts an image into a new layer. Understands vanilla 20-frame sheets (and wider ones, one column per
		/// animation frame); anything else is pasted into the current frame's base drawing.
		/// </summary>
		public static int ImportImage(Design d, Color[] px, int w, int h, string layerName, int atFrame) {
			if (d.Layers.Count >= Design.MaxLayers)
				return -1;
			var layer = new DesignLayer { Name = layerName };
			d.Layers.Add(layer);
			int layerIndex = d.Layers.Count - 1;

			int cellH = 0, cellW = 0;
			if (h % Design.PoseCount == 0) {
				int ch = h / Design.PoseCount;
				if (ch == 56 && w % 40 == 0) {
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
				layer.Cels[new CelKey(atFrame, Design.BasePose)] = cel;
				return layerIndex;
			}

			int columns = Math.Min(w / cellW, Design.MaxFrames);
			while (d.FrameCount < atFrame + columns && d.FrameCount < Design.MaxFrames)
				d.InsertFrame(d.FrameCount, -1);

			int dx = (d.Width - cellW) / 2, dy = (d.Height - cellH) / 2;
			int bobSlot = Slots.Bobs(d.Slot) ? 1 : 0;
			for (int col = 0; col < columns && atFrame + col < d.FrameCount; col++) {
				int frame = atFrame + col;
				Color[] Cell(int row) {
					var c = d.NewBuffer();
					Pixels.Blit(c, d.Width, d.Height, px, w, h, col * cellW, row * cellH, cellW, cellH, dx, dy);
					return c;
				}
				Color[] baseCel = Cell(0);
				layer.Cels[new CelKey(frame, Design.BasePose)] = baseCel;
				for (int pose = 1; pose < Design.PoseCount; pose++) {
					Color[] cel = Cell(pose);
					// vanilla sheets have the walk bob baked in, so only keep rows that really differ
					Color[] expected = bobSlot == 1 ? Pixels.Shifted(baseCel, d.Width, d.Height, 0, Slots.BobFor(pose)) : baseCel;
					if (!Pixels.Same(cel, expected))
						layer.Cels[new CelKey(frame, pose)] = cel;
				}
			}
			return layerIndex;
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

		private static Color[] Grab(Asset<Texture2D> asset, out int w, out int h) {
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
