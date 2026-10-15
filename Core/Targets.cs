using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace VanityStudio.Core
{
	/// <summary>
	/// Names for items, NPCs and projectiles that survive mod reloads ("Terraria/Zombie", "CalamityMod/Yharon"),
	/// plus everything needed to find their sprite. Runtime ids shift whenever the mod list changes, names don't.
	/// </summary>
	public static class Targets
	{
		private const string Vanilla = "Terraria/";

		public static string Key(DesignKind kind, int type) {
			if (type <= 0)
				return "";
			switch (kind) {
				case DesignKind.Item:
					return type < ItemID.Count ? Vanilla + ItemID.Search.GetName(type) : ItemLoader.GetItem(type)?.FullName ?? "";
				case DesignKind.NPC:
					return type < NPCID.Count ? Vanilla + NPCID.Search.GetName(type) : NPCLoader.GetNPC(type)?.FullName ?? "";
				case DesignKind.Projectile:
					return type < ProjectileID.Count ? Vanilla + ProjectileID.Search.GetName(type) : ProjectileLoader.GetProjectile(type)?.FullName ?? "";
			}
			return "";
		}

		public static int Resolve(DesignKind kind, string key) {
			if (string.IsNullOrEmpty(key))
				return 0;
			try {
				if (key.StartsWith(Vanilla)) {
					string name = key[Vanilla.Length..];
					return kind switch {
						DesignKind.Item => ItemID.Search.TryGetId(name, out int i) ? i : 0,
						DesignKind.NPC => NPCID.Search.TryGetId(name, out int n) ? n : 0,
						DesignKind.Projectile => ProjectileID.Search.TryGetId(name, out int p) ? p : 0,
						_ => 0
					};
				}
				return kind switch {
					DesignKind.Item => ModContent.TryFind(key, out ModItem mi) ? mi.Type : 0,
					DesignKind.NPC => ModContent.TryFind(key, out ModNPC mn) ? mn.Type : 0,
					DesignKind.Projectile => ModContent.TryFind(key, out ModProjectile mp) ? mp.Type : 0,
					_ => 0
				};
			}
			catch (Exception) {
				return 0;
			}
		}

		public static int ResolveItem(string key) => string.IsNullOrEmpty(key) ? 0 : Resolve(DesignKind.Item, key);

		public static int Count(DesignKind kind) => kind switch {
			DesignKind.Item => ItemLoader.ItemCount,
			DesignKind.NPC => NPCLoader.NPCCount,
			DesignKind.Projectile => ProjectileLoader.ProjectileCount,
			_ => 0
		};

		public static string DisplayName(DesignKind kind, int type) {
			try {
				return kind switch {
					DesignKind.Item => Lang.GetItemNameValue(type),
					DesignKind.NPC => Lang.GetNPCNameValue(type),
					DesignKind.Projectile => Lang.GetProjectileName(type).Value,
					_ => ""
				};
			}
			catch (Exception) {
				return "#" + type;
			}
		}

		public static bool IsBoss(int npcType) {
			if (npcType <= 0 || !ContentSamples.NpcsByNetId.TryGetValue(npcType, out NPC n))
				return false;
			return n.boss || NPCID.Sets.ShouldBeCountedAsBoss[npcType];
		}

		/// <summary>The sprite as the game currently has it, before any of our own retextures (so resource packs count).</summary>
		public static Asset<Texture2D> OriginalAsset(DesignKind kind, int type) {
			if (type <= 0 || type >= Count(kind))
				return null;
			Asset<Texture2D> ours = Retextures.OriginalOf(kind, type);
			if (ours != null)
				return ours;
			switch (kind) {
				case DesignKind.Item:
					Main.instance.LoadItem(type);
					return TextureAssets.Item[type];
				case DesignKind.NPC:
					Main.instance.LoadNPC(type);
					return TextureAssets.Npc[type];
				case DesignKind.Projectile:
					Main.instance.LoadProjectile(type);
					return TextureAssets.Projectile[type];
			}
			return null;
		}

		public static int SheetRows(DesignKind kind, int type) {
			switch (kind) {
				case DesignKind.Item:
					return Math.Max(1, Main.itemAnimations[type]?.FrameCount ?? 1);
				case DesignKind.NPC:
					return Math.Max(1, Main.npcFrameCount[type]);
				case DesignKind.Projectile:
					return Math.Max(1, Main.projFrames[type]);
			}
			return 1;
		}

		private static List<int> dyes;

		/// <summary>Every item that is a dye, in id order (vanilla first, then modded).</summary>
		public static List<int> Dyes {
			get {
				if (dyes == null) {
					dyes = new List<int>();
					for (int i = 1; i < ItemLoader.ItemCount; i++) {
						if (ContentSamples.ItemsByType.TryGetValue(i, out Item it) && it.dye > 0)
							dyes.Add(i);
					}
				}
				return dyes;
			}
		}

		public static void Unload() => dyes = null;

		// A short list reads better than 300 numbered dust types. Vanilla ids, so they're the same for everyone.
		public static readonly (int dust, string key)[] DustPresets = {
			(DustID.GoldFlame, "DustGold"),
			(DustID.Torch, "DustFire"),
			(DustID.IceTorch, "DustFrost"),
			(DustID.CursedTorch, "DustCursed"),
			(DustID.PurpleTorch, "DustArcane"),
			(DustID.Shadowflame, "DustShadow"),
			(DustID.Enchanted_Pink, "DustSparkle"),
			(DustID.RainbowMk2, "DustRainbow"),
			(DustID.GemDiamond, "DustGem"),
			(DustID.Electric, "DustElectric"),
			(DustID.Smoke, "DustSmoke"),
			(DustID.Blood, "DustBlood"),
			(DustID.Water, "DustWater"),
			(DustID.GlowingMushroom, "DustMushroom"),
			(DustID.Confetti, "DustConfetti"),
			(DustID.ShimmerSpark, "DustShimmer")
		};
	}
}
