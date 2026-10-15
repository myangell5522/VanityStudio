using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace VanityStudio.Core
{
	/// <summary>
	/// Replacement sprites for items, NPCs (bosses included) and projectiles. Works like a personal resource pack:
	/// the entry in TextureAssets is pointed at our own texture, and the original asset is kept so it can be
	/// copied from (that's where resource-pack sprites live) and put back when the retexture is switched off.
	/// Only affects this client.
	/// </summary>
	public class Retextures : ModSystem
	{
		private sealed class Live
		{
			public Design Design;
			public BakedDesign Bake;
			public DesignKind Kind;
			public int Type;
			public Asset<Texture2D> Original;
			public Asset<Texture2D> Replacement;
			public int Revision = -1;
			public int ShownFrame = -1;
		}

		private static readonly Dictionary<(DesignKind, int), Live> live = new();
		public static readonly HashSet<Guid> Enabled = new();
		private static bool ready;

		public override void PostSetupContent() {
			if (Main.dedServ)
				return;
			Main.QueueMainThreadAction(() => {
				ready = true;
				ApplyAll();
			});
			Main.OnPreDraw += Tick;
		}

		public override void Unload() {
			Main.OnPreDraw -= Tick;
			ready = false;
			foreach (Live l in live.Values.ToList())
				Restore(l);
			live.Clear();
		}

		public static bool IsEnabled(Design d) => d != null && Enabled.Contains(d.Id);

		public static void SetEnabled(Design d, bool on) {
			if (d == null || d.IsWearable)
				return;
			if (on) {
				// one retexture per target: switching a new one on turns the old one off
				foreach (Design other in DesignLibrary.All) {
					if (other != d && other.Kind == d.Kind && other.Target == d.Target)
						Enabled.Remove(other.Id);
				}
				Enabled.Add(d.Id);
			}
			else {
				Enabled.Remove(d.Id);
			}
			StudioSettings.Save();
			if (ready)
				ApplyAll();
		}

		public static void Forget(Guid id) {
			if (Enabled.Remove(id)) {
				StudioSettings.Save();
				if (ready)
					ApplyAll();
			}
		}

		// called by the studio while editing so changes show up on the real item / NPC straight away
		public static void Refresh(Design d) {
			if (!ready || !IsEnabled(d))
				return;
			int type = Targets.Resolve(d.Kind, d.Target);
			if (live.TryGetValue((d.Kind, type), out Live l) && l.Design == d)
				l.Revision = -1;
			else
				ApplyAll();
		}

		public static Asset<Texture2D> OriginalOf(DesignKind kind, int type) => live.TryGetValue((kind, type), out Live l) ? l.Original : null;

		public static void ApplyAll() {
			if (!ready)
				return;
			var wanted = new Dictionary<(DesignKind, int), Design>();
			foreach (Design d in DesignLibrary.All) {
				if (d.IsWearable || !Enabled.Contains(d.Id))
					continue;
				int type = Targets.Resolve(d.Kind, d.Target);
				if (type > 0)
					wanted[(d.Kind, type)] = d;
			}
			foreach (var key in live.Keys.ToList()) {
				if (!wanted.TryGetValue(key, out Design d) || live[key].Design != d) {
					Restore(live[key]);
					live.Remove(key);
				}
			}
			foreach (var pair in wanted) {
				if (live.ContainsKey(pair.Key))
					continue;
				try {
					Live l = Install(pair.Value, pair.Key.Item1, pair.Key.Item2);
					if (l != null)
						live[pair.Key] = l;
				}
				catch (Exception e) {
					ModContent.GetInstance<VanityStudio>().Logger.Warn($"Retexture '{pair.Value.Name}' could not be applied: {e.Message}");
				}
			}
		}

		private static Live Install(Design d, DesignKind kind, int type) {
			Asset<Texture2D> original = Targets.OriginalAsset(kind, type);
			if (original == null)
				return null;
			if (!original.IsLoaded)
				original.Wait?.Invoke();
			Texture2D orig = original.Value;
			int sheetH = d.Height * d.Rows;
			// the game slices frames out of the texture by its size, so the replacement must match exactly
			if (orig == null || orig.Width != d.Width || orig.Height != sheetH) {
				ModContent.GetInstance<VanityStudio>().Logger.Warn($"Retexture '{d.Name}' is {d.Width}x{sheetH} but {d.Target} is {orig?.Width}x{orig?.Height}, skipped");
				return null;
			}

			using var png = new MemoryStream();
			SheetIO.WritePng(png, new Color[d.Width * sheetH], d.Width, sheetH);
			png.Position = 0;
			Asset<Texture2D> replacement = Main.Assets.CreateUntracked<Texture2D>(png, $"VanityStudio_{d.Id:N}.png", AssetRequestMode.ImmediateLoad);

			var l = new Live { Design = d, Kind = kind, Type = type, Original = original, Replacement = replacement };
			Swap(kind, type, replacement);
			Update(l);
			return l;
		}

		private static void Restore(Live l) {
			Swap(l.Kind, l.Type, l.Original);
			Asset<Texture2D> rep = l.Replacement;
			l.Bake?.Dispose();
			if (rep != null)
				Main.QueueMainThreadAction(rep.Dispose);
		}

		private static void Swap(DesignKind kind, int type, Asset<Texture2D> asset) {
			Asset<Texture2D>[] arr = kind switch {
				DesignKind.Item => TextureAssets.Item,
				DesignKind.NPC => TextureAssets.Npc,
				DesignKind.Projectile => TextureAssets.Projectile,
				_ => null
			};
			if (arr != null && type > 0 && type < arr.Length)
				arr[type] = asset;
		}

		private static void Tick(GameTime _) {
			if (!ready)
				return;
			try {
				foreach (Live l in live.Values)
					Update(l);
			}
			catch (Exception e) {
				ModContent.GetInstance<VanityStudio>().Logger.Warn("Retexture update failed: " + e.Message);
			}
		}

		private static void Update(Live l) {
			if (l.Revision != l.Design.Revision || l.Bake == null) {
				if (l.Bake == null)
					l.Bake = BakedDesign.From(l.Design);
				else
					l.Bake.Rebake(l.Design);
				l.Revision = l.Design.Revision;
				l.ShownFrame = -1;
			}
			int frame = l.Bake.FrameAt(Main.GlobalTimeWrappedHourly * 1000.0);
			if (frame == l.ShownFrame || !l.Replacement.IsLoaded)
				return;
			l.ShownFrame = frame;
			BakedDesign b = l.Bake;
			int sheetH = b.Height * b.Rows.Length;
			var sheet = new Color[b.Width * sheetH];
			// plain layers go into the texture itself; glowing / dyed ones are drawn on top by the hooks below
			foreach (BakedPass p in b.Passes) {
				if (!p.IsPlain)
					continue;
				var part = new Color[sheet.Length];
				Pixels.Blit(part, b.Width, sheetH, p.Atlas, b.AtlasWidth, b.AtlasHeight, frame * b.Width, 0, b.Width, sheetH, 0, 0);
				Pixels.Composite(sheet, part, 1f);
			}
			l.Replacement.Value.SetData(Pixels.Premultiply(sheet));
		}

		#region overlays

		internal static bool TryOverlay(DesignKind kind, int type, out BakedDesign bake, out int frame) {
			bake = null;
			frame = 0;
			if (!live.TryGetValue((kind, type), out Live l) || l.Bake == null)
				return false;
			bake = l.Bake;
			frame = Math.Max(0, l.ShownFrame);
			foreach (BakedPass p in bake.Passes) {
				if (!p.IsPlain)
					return true;
			}
			return false;
		}

		internal static float PulseAlpha => 0.55f + 0.45f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3.2f);

		/// <summary>Draws every glowing / dyed pass. <paramref name="sheetFrame"/> is the frame rectangle inside the game's texture.</summary>
		internal static void DrawOverlays(SpriteBatch sb, BakedDesign bake, int animFrame, Rectangle sheetFrame, Vector2 pos, Color light, float rotation, Vector2 origin, float scale, SpriteEffects fx, Entity entity, bool world) {
			Rectangle src = new(animFrame * bake.Width + sheetFrame.X, sheetFrame.Y, sheetFrame.Width, sheetFrame.Height);
			foreach (BakedPass p in bake.Passes) {
				if (p.IsPlain)
					continue;
				Texture2D tex = p.GetTexture(bake.AtlasWidth, bake.AtlasHeight);
				if (tex == null)
					continue;
				Color c = p.Glow || p.Pulse ? Color.White : light;
				if (p.Pulse)
					c *= PulseAlpha;
				int shader = p.DyeItem > 0 ? GameShaders.Armor.GetShaderIdFromItemId(p.DyeItem) : 0;
				if (shader > 0 && world) {
					var data = new DrawData(tex, pos, src, c, rotation, origin, scale, fx);
					sb.End();
					sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
					GameShaders.Armor.Apply(shader, entity, data);
					data.Draw(sb);
					sb.End();
					sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.GameViewMatrix.TransformationMatrix);
				}
				else {
					sb.Draw(tex, pos, src, c, rotation, origin, scale, fx, 0f);
				}
			}
		}

		#endregion
	}

	public class RetextureItemGlow : GlobalItem
	{
		public override void PostDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale) {
			if (Retextures.TryOverlay(DesignKind.Item, item.type, out BakedDesign b, out int f))
				Retextures.DrawOverlays(spriteBatch, b, f, frame, position, drawColor, 0f, origin, scale, SpriteEffects.None, item, false);
		}

		public override void PostDrawInWorld(Item item, SpriteBatch spriteBatch, Color lightColor, Color alphaColor, float rotation, float scale, int whoAmI) {
			if (!Retextures.TryOverlay(DesignKind.Item, item.type, out BakedDesign b, out int f))
				return;
			Texture2D tex = TextureAssets.Item[item.type].Value;
			Rectangle frame = Main.itemAnimations[item.type] != null ? Main.itemAnimations[item.type].GetFrame(tex, Main.itemFrameCounter[whoAmI]) : tex.Frame();
			Vector2 origin = frame.Size() / 2f;
			Vector2 pos = item.position - Main.screenPosition + origin + new Vector2(item.width / 2 - frame.Width / 2, item.height - frame.Height);
			Retextures.DrawOverlays(spriteBatch, b, f, frame, pos, alphaColor, rotation, origin, scale, SpriteEffects.None, item, true);
		}
	}

	public class RetextureNpcGlow : GlobalNPC
	{
		public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
			if (!Retextures.TryOverlay(DesignKind.NPC, npc.type, out BakedDesign b, out int f))
				return;
			Texture2D tex = TextureAssets.Npc[npc.type].Value;
			Rectangle frame = npc.frame;
			Vector2 half = frame.Size() / 2f;
			int frames = Math.Max(1, Main.npcFrameCount[npc.type]);
			// same placement vanilla uses for a regular NPC
			var pos = new Vector2(
				npc.position.X - screenPos.X + npc.width / 2f - tex.Width * npc.scale / 2f + half.X * npc.scale,
				npc.position.Y - screenPos.Y + npc.height - tex.Height * npc.scale / frames + 4f + half.Y * npc.scale + Main.NPCAddHeight(npc) + npc.gfxOffY);
			SpriteEffects fx = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Retextures.DrawOverlays(spriteBatch, b, f, frame, pos, npc.GetAlpha(drawColor), npc.rotation, half, npc.scale, fx, npc, true);
		}
	}

	public class RetextureProjectileGlow : GlobalProjectile
	{
		public override void PostDraw(Projectile projectile, Color lightColor) {
			if (!Retextures.TryOverlay(DesignKind.Projectile, projectile.type, out BakedDesign b, out int f))
				return;
			Texture2D tex = TextureAssets.Projectile[projectile.type].Value;
			int frames = Math.Max(1, Main.projFrames[projectile.type]);
			int h = tex.Height / frames;
			var frame = new Rectangle(0, h * Math.Clamp(projectile.frame, 0, frames - 1), tex.Width, h);
			Vector2 pos = projectile.Center - Main.screenPosition + new Vector2(0f, projectile.gfxOffY);
			SpriteEffects fx = projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Retextures.DrawOverlays(Main.spriteBatch, b, f, frame, pos, projectile.GetAlpha(lightColor), projectile.rotation, frame.Size() / 2f, projectile.scale, fx, projectile, true);
		}
	}
}
