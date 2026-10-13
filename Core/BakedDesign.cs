using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace VanityStudio.Core
{
	/// <summary>
	/// A flattened, ready-to-draw copy of a design. This is what players wear and what goes over the network:
	/// one atlas, columns are animation frames, rows are the base drawing followed by any pose overrides.
	/// </summary>
	public sealed class BakedDesign : IDisposable
	{
		private const int MaxSide = 128;

		public DesignSlot Slot;
		public bool HideVanilla;
		public int Width, Height;
		public int[] FrameTimes = { 120 };
		public sbyte[] Rows = { Design.BasePose };
		public Color[] Atlas;

		private readonly int[] rowOfPose = new int[Design.PoseCount];
		private int totalTime;
		private Texture2D texture;
		private bool textureDirty = true;

		public int Frames => FrameTimes.Length;
		public int AtlasWidth => Width * Frames;
		public int AtlasHeight => Height * Rows.Length;

		public static BakedDesign From(Design d) {
			var b = new BakedDesign();
			b.Rebake(d);
			return b;
		}

		public void Rebake(Design d) {
			Slot = d.Slot;
			HideVanilla = d.HideVanilla;
			Width = d.Width;
			Height = d.Height;
			FrameTimes = d.FrameTimes.ToArray();

			var rows = new List<sbyte> { Design.BasePose };
			for (int pose = 0; pose < Design.PoseCount; pose++) {
				if (d.PoseUsed(pose))
					rows.Add((sbyte)pose);
			}
			Rows = rows.ToArray();

			int aw = AtlasWidth;
			var atlas = new Color[aw * AtlasHeight];
			for (int r = 0; r < Rows.Length; r++) {
				for (int f = 0; f < Frames; f++) {
					Color[] cell = d.Flatten(f, Rows[r]);
					Pixels.Blit(atlas, aw, AtlasHeight, cell, Width, Height, 0, 0, Width, Height, f * Width, r * Height);
				}
			}
			Atlas = atlas;
			Index();
			textureDirty = true;
		}

		private void Index() {
			Array.Fill(rowOfPose, 0);
			for (int r = 1; r < Rows.Length; r++) {
				if (Rows[r] >= 0 && Rows[r] < Design.PoseCount)
					rowOfPose[Rows[r]] = r;
			}
			totalTime = 0;
			foreach (int t in FrameTimes)
				totalTime += Math.Max(10, t);
		}

		public int FrameAt(double milliseconds) {
			if (Frames <= 1 || totalTime <= 0)
				return 0;
			double t = milliseconds % totalTime;
			for (int i = 0; i < Frames; i++) {
				t -= Math.Max(10, FrameTimes[i]);
				if (t < 0)
					return i;
			}
			return Frames - 1;
		}

		public Rectangle Source(int frame, int pose, out bool overridden) {
			frame = Math.Clamp(frame, 0, Frames - 1);
			int row = pose >= 0 && pose < Design.PoseCount ? rowOfPose[pose] : 0;
			overridden = row != 0;
			return new Rectangle(frame * Width, row * Height, Width, Height);
		}

		public Color[] Cell(int frame, int pose) {
			Rectangle src = Source(frame, pose, out _);
			var px = new Color[Width * Height];
			Pixels.Blit(px, Width, Height, Atlas, AtlasWidth, AtlasHeight, src.X, src.Y, Width, Height, 0, 0);
			return px;
		}

		public Texture2D GetTexture() {
			if (Main.dedServ || Atlas == null)
				return null;
			if (texture != null && (texture.IsDisposed || texture.Width != AtlasWidth || texture.Height != AtlasHeight)) {
				DisposeTexture(texture);
				texture = null;
			}
			if (texture == null) {
				texture = new Texture2D(Main.graphics.GraphicsDevice, AtlasWidth, AtlasHeight);
				textureDirty = true;
			}
			if (textureDirty) {
				texture.SetData(Pixels.Premultiply(Atlas));
				textureDirty = false;
			}
			return texture;
		}

		public void Dispose() {
			DisposeTexture(texture);
			texture = null;
		}

		private static void DisposeTexture(Texture2D tex) {
			if (tex == null || tex.IsDisposed)
				return;
			Main.QueueMainThreadAction(tex.Dispose);
		}

		#region network / disk form

		public byte[] Serialize() {
			using var ms = new MemoryStream();
			using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true))
			using (var w = new BinaryWriter(z)) {
				w.Write((byte)1);
				w.Write((byte)Slot);
				w.Write(HideVanilla);
				w.Write((byte)Width);
				w.Write((byte)Height);
				w.Write((byte)Frames);
				foreach (int t in FrameTimes)
					w.Write((ushort)Math.Clamp(t, 10, 60000));
				w.Write((byte)Rows.Length);
				foreach (sbyte r in Rows)
					w.Write(r);
				foreach (Color c in Atlas) {
					if (c.A == 0) {
						w.Write(0u);
						continue;
					}
					w.Write(c.PackedValue);
				}
			}
			return ms.ToArray();
		}

		public static BakedDesign Deserialize(byte[] data) {
			using var ms = new MemoryStream(data);
			using var z = new DeflateStream(ms, CompressionMode.Decompress);
			using var r = new BinaryReader(z);
			if (r.ReadByte() != 1)
				throw new InvalidDataException("bad outfit version");
			var b = new BakedDesign {
				Slot = (DesignSlot)r.ReadByte(),
				HideVanilla = r.ReadBoolean(),
				Width = r.ReadByte(),
				Height = r.ReadByte()
			};
			int frames = r.ReadByte();
			if ((byte)b.Slot >= Slots.Count || b.Width == 0 || b.Height == 0 || b.Width > MaxSide || b.Height > MaxSide || frames == 0 || frames > Design.MaxFrames)
				throw new InvalidDataException("outfit header out of range");
			b.FrameTimes = new int[frames];
			for (int i = 0; i < frames; i++)
				b.FrameTimes[i] = r.ReadUInt16();
			int rows = r.ReadByte();
			if (rows == 0 || rows > Design.PoseCount + 1)
				throw new InvalidDataException("outfit rows out of range");
			b.Rows = new sbyte[rows];
			for (int i = 0; i < rows; i++)
				b.Rows[i] = r.ReadSByte();
			var atlas = new Color[b.AtlasWidth * b.AtlasHeight];
			for (int i = 0; i < atlas.Length; i++)
				atlas[i] = new Color { PackedValue = r.ReadUInt32() };
			b.Atlas = atlas;
			b.Index();
			return b;
		}

		#endregion
	}
}
