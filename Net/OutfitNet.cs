using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using VanityStudio.Core;
using VanityStudio.Players;

namespace VanityStudio.Net
{
	/// <summary>
	/// Outfits can be a few hundred KB once someone animates every pose, and a ModPacket tops out at 64KB,
	/// so each outfit slot is sent as a numbered series of chunks and stitched back together.
	/// The server keeps the last copy of everyone's outfit so late joiners get it too.
	/// </summary>
	public static class OutfitNet
	{
		private enum Msg : byte
		{
			OutfitChunk,
			RequestAll
		}

		private const int ChunkSize = 60000;
		private const int MaxBlob = 8 * 1024 * 1024;

		private sealed class Transfer
		{
			public int Id;
			public byte[][] Parts;
			public int Received;
		}

		private static readonly Dictionary<(int who, int slot), byte[]> serverCopy = new();
		private static readonly Dictionary<(int who, int slot), Transfer> incoming = new();
		private static int transferCounter;

		private static Mod Mod => ModContent.GetInstance<VanityStudio>();

		public static void SendSlot(int slot, BakedDesign bake) {
			if (Main.netMode != NetmodeID.MultiplayerClient)
				return;
			byte[] blob = bake?.Serialize() ?? Array.Empty<byte>();
			SendBlob(-1, -1, Main.myPlayer, slot, blob);
		}

		public static void RequestOthers() {
			if (Main.netMode != NetmodeID.MultiplayerClient)
				return;
			ModPacket p = Mod.GetPacket(1);
			p.Write((byte)Msg.RequestAll);
			p.Send();
		}

		public static void Forget(int who) {
			foreach (var key in serverCopy.Keys.Where(k => k.who == who).ToList())
				serverCopy.Remove(key);
			foreach (var key in incoming.Keys.Where(k => k.who == who).ToList())
				incoming.Remove(key);
		}

		public static void Clear() {
			serverCopy.Clear();
			incoming.Clear();
		}

		private static void SendBlob(int toClient, int ignoreClient, int who, int slot, byte[] blob) {
			if (blob.Length > MaxBlob)
				return;
			int id = ++transferCounter;
			int count = Math.Max(1, (blob.Length + ChunkSize - 1) / ChunkSize);
			for (int i = 0; i < count; i++) {
				int start = i * ChunkSize;
				int len = Math.Min(ChunkSize, blob.Length - start);
				ModPacket p = Mod.GetPacket(len + 16);
				p.Write((byte)Msg.OutfitChunk);
				p.Write((byte)who);
				p.Write((byte)slot);
				p.Write(id);
				p.Write((ushort)i);
				p.Write((ushort)count);
				p.Write(len);
				if (len > 0)
					p.Write(blob, start, len);
				p.Send(toClient, ignoreClient);
			}
		}

		public static void Receive(BinaryReader r, int from) {
			var msg = (Msg)r.ReadByte();
			switch (msg) {
				case Msg.OutfitChunk:
					ReceiveChunk(r, from);
					break;
				case Msg.RequestAll:
					if (Main.netMode == NetmodeID.Server) {
						foreach (var pair in serverCopy) {
							if (pair.Key.who != from && Main.player[pair.Key.who].active)
								SendBlob(from, -1, pair.Key.who, pair.Key.slot, pair.Value);
						}
					}
					break;
			}
		}

		private static void ReceiveChunk(BinaryReader r, int from) {
			int who = r.ReadByte();
			int slot = r.ReadByte();
			int id = r.ReadInt32();
			int index = r.ReadUInt16();
			int count = r.ReadUInt16();
			int len = r.ReadInt32();
			if (len < 0 || len > ChunkSize)
				return;
			byte[] part = r.ReadBytes(len);

			// never trust the sender about who they are
			if (Main.netMode == NetmodeID.Server)
				who = from;
			if (who < 0 || who >= Main.maxPlayers || slot >= Slots.Count || count == 0 || count > MaxBlob / ChunkSize + 1 || index >= count)
				return;

			var key = (who, slot);
			if (!incoming.TryGetValue(key, out Transfer t) || t.Id != id || t.Parts.Length != count) {
				// a newer transfer replaces an unfinished older one, nobody wants the stale outfit
				t = new Transfer { Id = id, Parts = new byte[count][] };
				incoming[key] = t;
			}
			if (t.Parts[index] == null) {
				t.Parts[index] = part;
				t.Received++;
			}
			if (t.Received < count)
				return;
			incoming.Remove(key);

			byte[] blob = t.Parts.Length == 1 ? t.Parts[0] : t.Parts.SelectMany(x => x).ToArray();
			if (Main.netMode == NetmodeID.Server) {
				if (blob.Length == 0)
					serverCopy.Remove(key);
				else
					serverCopy[key] = blob;
				SendBlob(-1, from, who, slot, blob);
				return;
			}

			if (who == Main.myPlayer)
				return;
			Apply(who, slot, blob);
		}

		private static void Apply(int who, int slot, byte[] blob) {
			Player player = Main.player[who];
			if (player == null || !player.TryGetModPlayer(out VanityPlayer vp))
				return;
			BakedDesign bake = null;
			if (blob.Length > 0) {
				try {
					bake = BakedDesign.Deserialize(blob);
					if ((int)bake.Slot != slot)
						bake = null;
				}
				catch (Exception e) {
					Mod.Logger.Warn($"Dropped a broken outfit from player {who}: {e.Message}");
					bake = null;
				}
			}
			vp.Outfit[slot]?.Dispose();
			vp.Outfit[slot] = bake;
		}
	}
}
