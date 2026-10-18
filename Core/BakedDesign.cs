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
	/// One draw call's worth of a baked design: a run of neighbouring layers that share the same effects.
	/// Keeping runs (instead of grouping every glow layer together) keeps the stacking order the artist drew.
	/// </summary>
	public sealed class BakedPass
	{
		public bool Glow, Pulse;
		public int DyeItem;
		public int Dust = -1;
		public float DustRate;
		public Color[] Atlas;

		private Texture2D texture;
		private bool dirty = true;
		private Dictionary<int, int[]> emitPoints;

		// cut-up copies for free-arm poses (see Players/CompositeArms), made on demand
		internal readonly Dictionary<int, Texture2D> Pieces = new();

		public bool IsPlain => !Glow && !Pulse && DyeItem <= 0;
		public bool EmitsDust => Dust >= 0 && DustRate > 0f;

		public Texture2D GetTexture(int w, int h) {
			if (Main.dedServ || Atlas == null)
				return null;
			if (texture != null && (texture.IsDisposed || texture.Width != w || texture.Height != h)) {
				BakedDesign.DisposeLater(texture);
				texture = null;
			}
			if (texture == null) {
				texture = new Texture2D(Main.graphics.GraphicsDevice, w, h);
				dirty = true;
			}
			if (dirty) {
				texture.SetData(Pixels.Premultiply(Atlas));
				dirty = false;
			}
			return texture;
		}

		internal void MarkDirty() {
			dirty = true;
			emitPoints = null;
			ClearPieces();
		}

		private void ClearPieces() {
			foreach (Texture2D t in Pieces.Values)
				BakedDesign.DisposeLater(t);
			Pieces.Clear();
		}

		// opaque pixels of one cell, for throwing dust from
		public int[] EmitPoints(int key, Rectangle cell, int atlasWidth) {
			emitPoints ??= new Dictionary<int, int[]>();
			if (emitPoints.TryGetValue(key, out int[] pts))
				return pts;
			var list = new List<int>();
			for (int y = 0; y < cell.Height; y++) {
				for (int x = 0; x < cell.Width; x++) {
					if (Atlas[(cell.Y + y) * atlasWidth + cell.X + x].A > 40)
						list.Add(y * cell.Width + x);
				}
			}
			pts = list.ToArray();
			emitPoints[key] = pts;
			return pts;
		}

		public void Dispose() {
			BakedDesign.DisposeLater(texture);
			texture = null;
			ClearPieces();
		}
	}

	/// <summary>
	/// A flattened, ready-to-draw copy of a design. This is what players wear and what goes over the network:
	/// per pass one atlas, columns are animation frames, rows are the base drawing followed by any pose overrides
	/// (wearables) or every sheet frame in order (retextures).
	/// </summary>
	public sealed class BakedDesign : IDisposable
	{
		private const int MaxSide = Design.MaxSide;

		public DesignKind Kind;
		public DesignSlot Slot;
		public bool HideVanilla;
		public int Width, Height;
		public int[] FrameTimes = { 120 };
		public sbyte[] Rows = { Design.BasePose };
		public List<BakedPass> Passes = new();

		private int[] rowOfPose = new int[Design.PoseCount];
		private int totalTime;

		public int Frames => FrameTimes.Length;
		public int AtlasWidth => Width * Frames;
		public int AtlasHeight => Height * Rows.Length;
		public bool FullSheet => Kind != DesignKind.Wearable;

		public static BakedDesign From(Design d) {
			var b = new BakedDesign();
			b.Rebake(d);
			return b;
		}

		public void Rebake(Design d) {
			Kind = d.Kind;
			Slot = d.Slot;
			HideVanilla = d.HideVanilla;
			Width = d.Width;
			Height = d.Height;
			FrameTimes = d.FrameTimes.ToArray();

			var rows = new List<sbyte>();
			if (d.IsWearable) {
				rows.Add(Design.BasePose);
				for (int pose = 0; pose < Design.PoseCount; pose++) {
					if (d.PoseUsed(pose))
						rows.Add((sbyte)pose);
				}
			}
			else {
				for (int r = 0; r < d.Rows; r++)
					rows.Add((sbyte)r);
			}
			Rows = rows.ToArray();

			// split the visible layers into runs of identical effects
			var runs = new List<List<DesignLayer>>();
			foreach (DesignLayer l in d.Layers) {
				if (!l.Visible)
					continue;
				if (runs.Count == 0 || !runs[^1][0].Signature.Equals(l.Signature))
					runs.Add(new List<DesignLayer>());
				runs[^1].Add(l);
			}

			int aw = AtlasWidth, ah = AtlasHeight;
			var old = Passes;
			Passes = new List<BakedPass>();
			for (int i = 0; i < runs.Count; i++) {
				DesignLayer first = runs[i][0];
				BakedPass pass = i < old.Count ? old[i] : new BakedPass();
				pass.Glow = first.Glow;
				pass.Pulse = first.Pulse;
				pass.DyeItem = Targets.ResolveItem(first.Dye);
				pass.Dust = first.DustRate > 0f ? first.Dust : -1;
				pass.DustRate = first.Dust >= 0 ? first.DustRate : 0f;
				var atlas = new Color[aw * ah];
				for (int r = 0; r < Rows.Length; r++) {
					for (int f = 0; f < Frames; f++) {
						var cell = d.NewBuffer();
						foreach (DesignLayer l in runs[i])
							Pixels.Composite(cell, d.LayerPixels(l, f, Rows[r]), l.Opacity);
						Pixels.Blit(atlas, aw, ah, cell, Width, Height, 0, 0, Width, Height, f * Width, r * Height);
					}
				}
				pass.Atlas = atlas;
				pass.MarkDirty();
				Passes.Add(pass);
			}
			for (int i = runs.Count; i < old.Count; i++)
				old[i].Dispose();
			Index();
		}

		private void Index() {
			int poses = FullSheet ? Rows.Length : Design.PoseCount;
			rowOfPose = new int[Math.Max(1, poses)];
			for (int r = 0; r < Rows.Length; r++) {
				int pose = Rows[r];
				if (pose >= 0 && pose < rowOfPose.Length)
					rowOfPose[pose] = r;
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
			int row = pose >= 0 && pose < rowOfPose.Length ? rowOfPose[pose] : 0;
			overridden = FullSheet || row != 0;
			return new Rectangle(frame * Width, row * Height, Width, Height);
		}

		/// <summary>Flattened look of all passes for one cell (thumbnails, previews of remote outfits).</summary>
		public Color[] Cell(int frame, int pose) {
			Rectangle src = Source(frame, pose, out _);
			var px = new Color[Width * Height];
			foreach (BakedPass p in Passes) {
				var part = new Color[Width * Height];
				Pixels.Blit(part, Width, Height, p.Atlas, AtlasWidth, AtlasHeight, src.X, src.Y, Width, Height, 0, 0);
				Pixels.Composite(px, part, 1f);
			}
			return px;
		}

		public void Dispose() {
			foreach (BakedPass p in Passes)
				p.Dispose();
		}

		internal static void DisposeLater(Texture2D tex) {
			if (tex == null || tex.IsDisposed)
				return;
			// it may still be queued in this frame's sprite batch
			Main.QueueMainThreadAction(tex.Dispose);
		}

		#region network form

		public byte[] Serialize() {
			using var ms = new MemoryStream();
			using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true))
			using (var w = new BinaryWriter(z)) {
				w.Write((byte)2);
				w.Write((byte)Kind);
				w.Write((byte)Slot);
				w.Write(HideVanilla);
				w.Write((ushort)Width);
				w.Write((ushort)Height);
				w.Write((byte)Frames);
				foreach (int t in FrameTimes)
					w.Write((ushort)Math.Clamp(t, 10, 60000));
				w.Write((byte)Rows.Length);
				foreach (sbyte r in Rows)
					w.Write(r);
				w.Write((byte)Passes.Count);
				foreach (BakedPass p in Passes) {
					w.Write(p.Glow);
					w.Write(p.Pulse);
					w.Write(p.DyeItem);
					w.Write((short)p.Dust);
					w.Write(p.DustRate);
					foreach (Color c in p.Atlas)
						w.Write(c.A == 0 ? 0u : c.PackedValue);
				}
			}
			return ms.ToArray();
		}

		public static BakedDesign Deserialize(byte[] data) {
			using var ms = new MemoryStream(data);
			using var z = new DeflateStream(ms, CompressionMode.Decompress);
			using var r = new BinaryReader(z);
			if (r.ReadByte() != 2)
				throw new InvalidDataException("bad outfit version");
			var b = new BakedDesign {
				Kind = (DesignKind)r.ReadByte(),
				Slot = (DesignSlot)r.ReadByte(),
				HideVanilla = r.ReadBoolean(),
				Width = r.ReadUInt16(),
				Height = r.ReadUInt16()
			};
			int frames = r.ReadByte();
			if (b.Kind != DesignKind.Wearable || (byte)b.Slot >= Slots.Count || b.Width == 0 || b.Height == 0 || b.Width > 128 || b.Height > 128 || frames == 0 || frames > Design.MaxFrames)
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
			int passes = r.ReadByte();
			if (passes > Design.MaxLayers)
				throw new InvalidDataException("too many passes");
			for (int p = 0; p < passes; p++) {
				var pass = new BakedPass {
					Glow = r.ReadBoolean(),
					Pulse = r.ReadBoolean(),
					DyeItem = r.ReadInt32(),
					Dust = r.ReadInt16(),
					DustRate = Math.Clamp(r.ReadSingle(), 0f, 1f)
				};
				var atlas = new Color[b.AtlasWidth * b.AtlasHeight];
				for (int i = 0; i < atlas.Length; i++)
					atlas[i] = new Color { PackedValue = r.ReadUInt32() };
				pass.Atlas = atlas;
				b.Passes.Add(pass);
			}
			b.Index();
			return b;
		}

		#endregion
	}
}
