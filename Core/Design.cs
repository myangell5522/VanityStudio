using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Microsoft.Xna.Framework;

namespace VanityStudio.Core
{
	public readonly record struct CelKey(int Frame, int Pose);

	public sealed class DesignLayer
	{
		public string Name = "Layer";
		public float Opacity = 1f;
		public bool Visible = true;
		public readonly Dictionary<CelKey, Color[]> Cels = new();

		public Color[] Get(int frame, int pose) => Cels.TryGetValue(new CelKey(frame, pose), out var c) ? c : null;

		public DesignLayer Clone() {
			var copy = new DesignLayer { Name = Name, Opacity = Opacity, Visible = Visible };
			foreach (var pair in Cels)
				copy.Cels[pair.Key] = (Color[])pair.Value.Clone();
			return copy;
		}
	}

	public sealed class Design
	{
		public const int BasePose = -1;
		public const int PoseCount = 20;
		public const int MaxFrames = 24;
		public const int MaxLayers = 24;
		private const int FileVersion = 1;

		public Guid Id = Guid.NewGuid();
		public string Name = "Untitled";
		public DesignSlot Slot;
		public bool HideVanilla;
		public int Width = 40, Height = 56;
		public List<int> FrameTimes = new() { 120 };
		public List<DesignLayer> Layers = new();
		public long Modified = DateTime.UtcNow.Ticks;

		// bumped on every edit, never saved
		public int Revision;

		public int FrameCount => FrameTimes.Count;

		public static Design Create(DesignSlot slot, string name) {
			Point size = Slots.CanvasSize(slot);
			var d = new Design { Name = name, Slot = slot, Width = size.X, Height = size.Y };
			d.Layers.Add(new DesignLayer { Name = "Layer 1" });
			return d;
		}

		public void Touch() {
			Revision++;
			Modified = DateTime.UtcNow.Ticks;
		}

		public Color[] NewBuffer() => new Color[Width * Height];

		public Color[] GetOrCreate(int layer, int frame, int pose) {
			DesignLayer l = Layers[layer];
			var key = new CelKey(frame, pose);
			if (l.Cels.TryGetValue(key, out var cel))
				return cel;
			// a fresh pose override starts as the base drawing, already bobbed, since that's what the user was looking at
			Color[] start = pose >= 0 ? LayerPixels(l, frame, pose) : null;
			cel = start != null ? (Color[])start.Clone() : NewBuffer();
			l.Cels[key] = cel;
			return cel;
		}

		public bool HasOverride(int frame, int pose) {
			if (pose < 0)
				return false;
			var key = new CelKey(frame, pose);
			return Layers.Any(l => l.Cels.ContainsKey(key));
		}

		public bool PoseUsed(int pose) => Layers.Any(l => l.Cels.Keys.Any(k => k.Pose == pose));

		public void ClearOverride(int frame, int pose) {
			if (pose < 0)
				return;
			var key = new CelKey(frame, pose);
			foreach (var l in Layers)
				l.Cels.Remove(key);
		}

		public Color[] Flatten(int frame, int pose, int skipLayer = -1) {
			var result = NewBuffer();
			for (int i = 0; i < Layers.Count; i++) {
				DesignLayer l = Layers[i];
				if (!l.Visible || i == skipLayer)
					continue;
				Pixels.Composite(result, LayerPixels(l, frame, pose), l.Opacity);
			}
			return result;
		}

		/// <summary>
		/// What a layer looks like in a given pose: its own drawing for that pose, or the base drawing moved by the walk bob,
		/// which is exactly how it ends up on the character.
		/// </summary>
		public Color[] LayerPixels(DesignLayer l, int frame, int pose) {
			if (pose < 0)
				return l.Get(frame, BasePose);
			Color[] own = l.Get(frame, pose);
			if (own != null)
				return own;
			Color[] b = l.Get(frame, BasePose);
			if (b == null)
				return null;
			int bob = Slots.Bobs(Slot) ? Slots.BobFor(pose) : 0;
			return bob == 0 ? b : Pixels.Shifted(b, Width, Height, 0, bob);
		}

		// The image the player will actually wear for a legacy frame. Base drawings get the walk bob applied.
		public Color[] WornFrame(int frame, int pose) {
			if (HasOverride(frame, pose))
				return Flatten(frame, pose);
			Color[] flat = Flatten(frame, BasePose);
			int bob = Slots.Bobs(Slot) ? Slots.BobFor(pose) : 0;
			return bob == 0 ? flat : Pixels.Shifted(flat, Width, Height, 0, bob);
		}

		public void ChangeSlot(DesignSlot slot) {
			if (slot == Slot)
				return;
			Point size = Slots.CanvasSize(slot);
			if (size.X != Width || size.Y != Height) {
				int dx = (size.X - Width) / 2, dy = (size.Y - Height) / 2;
				foreach (var l in Layers) {
					foreach (var key in l.Cels.Keys.ToList()) {
						var dst = new Color[size.X * size.Y];
						Pixels.Blit(dst, size.X, size.Y, l.Cels[key], Width, Height, 0, 0, Width, Height, dx, dy);
						l.Cels[key] = dst;
					}
				}
				Width = size.X;
				Height = size.Y;
			}
			Slot = slot;
		}

		#region frames

		public void InsertFrame(int at, int copyFrom) {
			if (FrameCount >= MaxFrames)
				return;
			at = Math.Clamp(at, 0, FrameCount);
			if (copyFrom >= FrameCount)
				copyFrom = -1;
			int time = copyFrom >= 0 ? FrameTimes[copyFrom] : FrameTimes[Math.Clamp(at - 1, 0, FrameCount - 1)];
			RemapFrames(f => f >= at ? f + 1 : f);
			FrameTimes.Insert(at, time);
			if (copyFrom >= 0) {
				int src = copyFrom >= at ? copyFrom + 1 : copyFrom;
				foreach (var l in Layers) {
					foreach (var pair in l.Cels.Where(p => p.Key.Frame == src).ToList())
						l.Cels[new CelKey(at, pair.Key.Pose)] = (Color[])pair.Value.Clone();
				}
			}
		}

		public void RemoveFrame(int index) {
			if (FrameCount <= 1)
				return;
			foreach (var l in Layers) {
				foreach (var key in l.Cels.Keys.Where(k => k.Frame == index).ToList())
					l.Cels.Remove(key);
			}
			FrameTimes.RemoveAt(index);
			RemapFrames(f => f > index ? f - 1 : f);
		}

		public void SwapFrames(int a, int b) {
			if (a < 0 || b < 0 || a >= FrameCount || b >= FrameCount || a == b)
				return;
			RemapFrames(f => f == a ? b : f == b ? a : f);
			(FrameTimes[a], FrameTimes[b]) = (FrameTimes[b], FrameTimes[a]);
		}

		private void RemapFrames(Func<int, int> map) {
			foreach (var l in Layers) {
				var moved = l.Cels.ToList();
				l.Cels.Clear();
				foreach (var pair in moved)
					l.Cels[new CelKey(map(pair.Key.Frame), pair.Key.Pose)] = pair.Value;
			}
		}

		#endregion

		#region layers

		public void MergeDown(int index) {
			if (index <= 0 || index >= Layers.Count)
				return;
			DesignLayer top = Layers[index], below = Layers[index - 1];
			var merged = new Dictionary<CelKey, Color[]>();
			// work from the untouched layers: writing a merged base first would leak into the bobbed pose overrides
			foreach (var key in top.Cels.Keys.Union(below.Cels.Keys)) {
				Color[] dst = LayerPixels(below, key.Frame, key.Pose);
				dst = dst != null ? (Color[])dst.Clone() : NewBuffer();
				if (below.Opacity < 1f) {
					for (int i = 0; i < dst.Length; i++)
						dst[i].A = (byte)(dst[i].A * below.Opacity);
				}
				if (top.Visible)
					Pixels.Composite(dst, LayerPixels(top, key.Frame, key.Pose), top.Opacity);
				merged[key] = dst;
			}
			foreach (var pair in merged)
				below.Cels[pair.Key] = pair.Value;
			below.Opacity = 1f;
			Layers.RemoveAt(index);
		}

		#endregion

		#region io

		public void Write(BinaryWriter w) {
			w.Write(FileVersion);
			w.Write(Id.ToByteArray());
			w.Write(Name ?? "");
			w.Write((byte)Slot);
			w.Write(HideVanilla);
			w.Write((ushort)Width);
			w.Write((ushort)Height);
			w.Write(Modified);
			w.Write((byte)FrameCount);
			foreach (int t in FrameTimes)
				w.Write((ushort)Math.Clamp(t, 10, 60000));
			w.Write((byte)Layers.Count);
			foreach (var l in Layers) {
				w.Write(l.Name ?? "");
				w.Write(l.Opacity);
				w.Write(l.Visible);
				w.Write((ushort)l.Cels.Count);
				foreach (var pair in l.Cels) {
					w.Write((byte)pair.Key.Frame);
					w.Write((sbyte)pair.Key.Pose);
					WritePixels(w, pair.Value);
				}
			}
		}

		public static Design Read(BinaryReader r) {
			int version = r.ReadInt32();
			if (version < 1 || version > FileVersion)
				throw new InvalidDataException($"Unsupported design version {version}");
			var d = new Design {
				Id = new Guid(r.ReadBytes(16)),
				Name = r.ReadString(),
				Slot = (DesignSlot)r.ReadByte(),
				HideVanilla = r.ReadBoolean(),
				Width = r.ReadUInt16(),
				Height = r.ReadUInt16(),
				Modified = r.ReadInt64()
			};
			if (d.Width <= 0 || d.Height <= 0 || d.Width > 256 || d.Height > 256)
				throw new InvalidDataException("Bad canvas size");
			if ((byte)d.Slot >= Slots.Count)
				d.Slot = DesignSlot.Body;
			int frames = r.ReadByte();
			d.FrameTimes = new List<int>();
			for (int i = 0; i < frames; i++)
				d.FrameTimes.Add(r.ReadUInt16());
			if (d.FrameTimes.Count == 0)
				d.FrameTimes.Add(120);
			int layers = r.ReadByte();
			for (int i = 0; i < layers; i++) {
				var l = new DesignLayer { Name = r.ReadString(), Opacity = Math.Clamp(r.ReadSingle(), 0f, 1f), Visible = r.ReadBoolean() };
				int cels = r.ReadUInt16();
				for (int c = 0; c < cels; c++) {
					int frame = r.ReadByte();
					int pose = r.ReadSByte();
					Color[] px = ReadPixels(r, d.Width * d.Height);
					if (frame < d.FrameCount && pose >= BasePose && pose < PoseCount)
						l.Cels[new CelKey(frame, pose)] = px;
				}
				d.Layers.Add(l);
			}
			if (d.Layers.Count == 0)
				d.Layers.Add(new DesignLayer { Name = "Layer 1" });
			return d;
		}

		private static void WritePixels(BinaryWriter w, Color[] px) {
			foreach (Color c in px) {
				if (c.A == 0) {
					w.Write(0u);
					continue;
				}
				w.Write(c.R);
				w.Write(c.G);
				w.Write(c.B);
				w.Write(c.A);
			}
		}

		private static Color[] ReadPixels(BinaryReader r, int count) {
			byte[] raw = r.ReadBytes(count * 4);
			if (raw.Length != count * 4)
				throw new EndOfStreamException();
			var px = new Color[count];
			for (int i = 0; i < count; i++)
				px[i] = new Color(raw[i * 4], raw[i * 4 + 1], raw[i * 4 + 2], raw[i * 4 + 3]);
			return px;
		}

		public byte[] ToBytes() {
			using var ms = new MemoryStream();
			using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true))
			using (var w = new BinaryWriter(z))
				Write(w);
			return ms.ToArray();
		}

		public static Design FromBytes(byte[] data) {
			using var ms = new MemoryStream(data);
			using var z = new DeflateStream(ms, CompressionMode.Decompress);
			using var r = new BinaryReader(z);
			return Read(r);
		}

		// Used by undo: swap the contents in place so every reference to this design stays valid.
		public void CopyFrom(Design other) {
			Name = other.Name;
			Slot = other.Slot;
			HideVanilla = other.HideVanilla;
			Width = other.Width;
			Height = other.Height;
			FrameTimes = new List<int>(other.FrameTimes);
			Layers = other.Layers.Select(l => l.Clone()).ToList();
			Touch();
		}

		public Design Duplicate(string newName) {
			Design copy = FromBytes(ToBytes());
			copy.Id = Guid.NewGuid();
			copy.Name = newName;
			copy.Touch();
			return copy;
		}

		#endregion
	}
}
