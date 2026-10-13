using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.Cil;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;
using VanityStudio.Core;

namespace VanityStudio.UI
{
	/// <summary>
	/// Takes over the achievement-advisor spot in the open inventory. A little tumbler underneath flips between
	/// the studio button and the vanilla advisor, which is left completely alone when it's showing.
	/// </summary>
	public class InventoryButton : ModSystem
	{
		private static Asset<Texture2D> mascot, track, knob, rim;
		private static Asset<Texture2D> border, hoverThin, hoverFat;
		private static Texture2D wheel;
		private static readonly Color[] wheelPixels = new Color[64 * 64];
		private static uint wheelTick = uint.MaxValue;

		private static float knobPos;
		private static bool switchHovered;

		public override void Load() {
			if (Main.dedServ)
				return;
			StudioSettings.Load();
			mascot = ModContent.Request<Texture2D>("VanityStudio/Assets/Mascot");
			track = ModContent.Request<Texture2D>("VanityStudio/Assets/SwitchTrack");
			knob = ModContent.Request<Texture2D>("VanityStudio/Assets/SwitchKnob");
			rim = ModContent.Request<Texture2D>("VanityStudio/Assets/SwitchHover");
			border = Main.Assets.Request<Texture2D>("Images/UI/Achievement_Borders");
			hoverThin = Main.Assets.Request<Texture2D>("Images/UI/Achievement_Borders_MouseHoverThin");
			hoverFat = Main.Assets.Request<Texture2D>("Images/UI/Achievement_Borders_MouseHover");
			knobPos = StudioSettings.ShowStudioButton ? 1f : 0f;

			On_AchievementAdvisor.DrawOneAchievement += DrawAdvisorSpot;
			IL_Main.DrawInventory += SkipCoinAmmoLabels;
		}

		public override void Unload() {
			Texture2D w = wheel;
			wheel = null;
			if (w != null)
				Main.QueueMainThreadAction(w.Dispose);
		}

		public override void UpdateUI(GameTime gameTime) {
			if (!Main.playerInventory)
				switchHovered = false;
		}

		private static void DrawAdvisorSpot(On_AchievementAdvisor.orig_DrawOneAchievement orig, AchievementAdvisor self, SpriteBatch spriteBatch, Vector2 position, bool large) {
			// same numbers vanilla uses for the advisor card
			float scale = large ? 0.75f : 0.35f;
			Vector2 card = position + new Vector2(8f) * scale;

			if (StudioSettings.ShowStudioButton)
				DrawStudioButton(spriteBatch, card, scale);
			else
				orig(self, spriteBatch, position, large);

			DrawSwitch(spriteBatch, card, 64f * scale, large);
		}

		private static void DrawStudioButton(SpriteBatch sb, Vector2 card, float scale) {
			float size = 64f * scale;
			bool hovered = !PlayerInput.IgnoreMouseInterface && Main.MouseScreen.Between(card, card + new Vector2(size));
			Color tint = hovered ? Color.White : new Color(220, 220, 220, 220);

			UpdateWheel();
			sb.Draw(wheel, card, null, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

			Texture2D m = mascot.Value;
			float ms = 60f / Math.Max(m.Width, m.Height) * scale;
			float hop = hovered ? Math.Abs((float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f)) * -2f * scale : 0f;
			sb.Draw(m, card + new Vector2(size / 2f, size / 2f + 1f + hop), null, tint, 0f, m.Size() / 2f, ms, SpriteEffects.None, 0f);

			sb.Draw(border.Value, card + new Vector2(-4f) * scale, null, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			if (!hovered)
				return;

			bool thin = scale > 0.5f;
			sb.Draw(thin ? hoverThin.Value : hoverFat.Value, card + new Vector2(thin ? -5f : -8f) * scale, null, Main.OurFavoriteColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
			Main.LocalPlayer.mouseInterface = true;
			Main.hoverItemName = L.Get("ButtonTip");
			if (Main.mouseLeft && Main.mouseLeftRelease) {
				Main.mouseLeftRelease = false;
				StudioState.Open();
			}
		}

		private static void DrawSwitch(SpriteBatch sb, Vector2 card, float cardSize, bool large) {
			Texture2D t = track.Value, k = knob.Value, outline = rim.Value;
			float s = large ? 1f : 0.75f;
			// Sits where the Coins/Ammo captions used to be, a couple of pixels below the bottom edge of the
			// hover outline (not the card), so the gold outline never slides under the tumbler.
			float cardScale = cardSize / 64f;
			bool thin = cardScale > 0.5f;
			Texture2D hoverTex = thin ? hoverThin.Value : hoverFat.Value;
			float outlineBottom = card.Y + (thin ? -5f : -8f) * cardScale + hoverTex.Height * cardScale;
			float top = Math.Max(card.Y + cardSize + 4f, outlineBottom + 2f);
			var pos = new Vector2(card.X + cardSize / 2f - t.Width * s / 2f, top).Floor();
			var hit = new Rectangle((int)pos.X - 2, (int)pos.Y - 2, (int)(t.Width * s) + 4, (int)(t.Height * s) + 4);
			bool hovered = !PlayerInput.IgnoreMouseInterface && hit.Contains(Main.MouseScreen.ToPoint());
			if (hovered && !switchHovered)
				SoundEngine.PlaySound(SoundID.MenuTick);
			switchHovered = hovered;

			float target = StudioSettings.ShowStudioButton ? 1f : 0f;
			knobPos = MathHelper.Lerp(knobPos, target, 0.3f);
			if (Math.Abs(knobPos - target) < 0.01f)
				knobPos = target;

			Color trackColor = Color.Lerp(new Color(98, 94, 112), new Color(82, 176, 74), knobPos);
			if (hovered)
				sb.Draw(outline, pos - new Vector2(2f * s), null, Main.OurFavoriteColor, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
			sb.Draw(t, pos, null, trackColor, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);
			float kx = pos.X + (t.Width - k.Width) * s * knobPos;
			sb.Draw(k, new Vector2((int)kx, pos.Y), null, Color.White, 0f, Vector2.Zero, s, SpriteEffects.None, 0f);

			if (!hovered)
				return;
			Main.LocalPlayer.mouseInterface = true;
			Main.hoverItemName = StudioSettings.ShowStudioButton ? L.Get("SwitchToAchievements") : L.Get("SwitchToStudio");
			if (Main.mouseLeft && Main.mouseLeftRelease) {
				Main.mouseLeftRelease = false;
				StudioSettings.ShowStudioButton = !StudioSettings.ShowStudioButton;
				StudioSettings.Save();
				SoundEngine.PlaySound(SoundID.Mech);
			}
		}

		// Drawn at 32x32 and doubled so it has the same chunky pixels as the rest of the inventory art.
		private static void UpdateWheel() {
			wheel ??= new Texture2D(Main.instance.GraphicsDevice, 64, 64);
			if (wheelTick == Main.GameUpdateCount)
				return;
			wheelTick = Main.GameUpdateCount;

			float spin = Main.GlobalTimeWrappedHourly * 0.11f;
			var outline = new Color(24, 18, 32);
			for (int ay = 0; ay < 32; ay++) {
				for (int ax = 0; ax < 32; ax++) {
					float dx = ax - 15.5f, dy = ay - 15.5f;
					float r = MathF.Sqrt(dx * dx + dy * dy);
					Color c;
					if (r > 15.6f)
						c = Color.Transparent;
					else if (r > 14.4f)
						c = outline;
					else {
						float angle = MathF.Atan2(dy, dx) / MathHelper.TwoPi + 0.5f;
						// 24 wedges in space, but the colours slide continuously
						float hue = (MathF.Floor(angle * 24f) / 24f + spin) % 1f;
						float light = 0.5f + (1f - r / 14.4f) * 0.22f;
						if (dx + dy < -14f)
							light += 0.08f;
						c = Main.hslToRgb(hue, 0.82f, Math.Min(light, 0.85f));
					}
					// premultiplied, so transparent stays (0,0,0,0)
					wheelPixels[ay * 2 * 64 + ax * 2] = c;
					wheelPixels[ay * 2 * 64 + ax * 2 + 1] = c;
					wheelPixels[(ay * 2 + 1) * 64 + ax * 2] = c;
					wheelPixels[(ay * 2 + 1) * 64 + ax * 2 + 1] = c;
				}
			}
			wheel.SetData(wheelPixels);
		}

		private static void SkipCoinAmmoLabels(ILContext il) {
			var c = new ILCursor(il);
			MethodInfo replacement = typeof(InventoryButton).GetMethod(nameof(DrawCaption), BindingFlags.NonPublic | BindingFlags.Static);
			FieldInfo inter = typeof(Lang).GetField(nameof(Lang.inter));
			foreach (int id in new[] { 26, 27 }) {
				// each caption is read twice (measure, then draw); the DrawString we want follows the second read
				for (int pass = 0; pass < 2; pass++) {
					if (!c.TryGotoNext(MoveType.After, i => i.MatchLdsfld(inter), i => i.MatchLdcI4(id))) {
						ModContent.GetInstance<VanityStudio>().Logger.Warn($"Inventory caption {id} not found, it will stay visible");
						return;
					}
				}
				if (!c.TryGotoNext(i => i.MatchCall(out var m) && m.Name == "DrawString" && m.DeclaringType.Name == nameof(DynamicSpriteFontExtensionMethods) && m.Parameters.Count == 10)) {
					ModContent.GetInstance<VanityStudio>().Logger.Warn($"DrawString for caption {id} not found");
					return;
				}
				c.Next.Operand = il.Import(replacement);
			}
		}

		// same signature as the DynamicSpriteFontExtensionMethods.DrawString overload it replaces
		private static void DrawCaption(SpriteBatch sb, DynamicSpriteFont font, string text, Vector2 position, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth) {
			// the tumbler lives where the "Coins" and "Ammo" captions are, only when the advisor spot is above the coins
			if (Main.AchievementAdvisor.CanDrawAboveCoins)
				return;
			sb.DrawString(font, text, position, color, rotation, origin, scale, effects, layerDepth);
		}
	}
}
