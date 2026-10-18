using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;
using VanityStudio.Core;

namespace VanityStudio.Players
{
	/// <summary>
	/// Draws a worn design in the same spot and with the same transforms vanilla uses for the matching armor piece,
	/// so a drawing made on the 40x56 canvas lines up pixel for pixel with the character.
	/// </summary>
	public abstract class OutfitLayer : PlayerDrawLayer
	{
		protected abstract DesignSlot Slot { get; }

		public override bool IsHeadLayer => Slot == DesignSlot.Head;

		public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) {
			if (drawInfo.shadow != 0f || drawInfo.headOnlyRender && Slot != DesignSlot.Head)
				return false;
			Player p = drawInfo.drawPlayer;
			if (p.dead || !p.TryGetModPlayer(out VanityPlayer vp))
				return false;
			if (p.invis && !vp.IsDummy)
				return false;
			return vp.Drawn(Slot) != null;
		}

		protected override void Draw(ref PlayerDrawSet drawInfo) {
			Player p = drawInfo.drawPlayer;
			var vp = p.GetModPlayer<VanityPlayer>();
			BakedDesign bake = vp.Drawn(Slot);
			if (bake == null)
				return;

			Rectangle frameRect = Slot == DesignSlot.Legs ? p.legFrame : p.bodyFrame;
			int pose = frameRect.Height > 0 ? Math.Clamp(frameRect.Y / frameRect.Height, 0, Design.PoseCount - 1) : 0;
			Rectangle src = bake.Source(vp.FrameFor(bake), pose, out bool overridden);

			var basePos = new Vector2(
				(int)(drawInfo.Position.X - Main.screenPosition.X - p.bodyFrame.Width / 2 + p.width / 2),
				(int)(drawInfo.Position.Y - Main.screenPosition.Y + p.height - p.bodyFrame.Height + 4f));

			Vector2 anchor, origin;
			float rotation;
			Color color;
			int shader;
			switch (Slot) {
				case DesignSlot.Head:
					anchor = p.headPosition + drawInfo.headVect + drawInfo.helmetOffset;
					origin = drawInfo.headVect;
					rotation = p.headRotation;
					color = drawInfo.colorArmorHead;
					shader = drawInfo.cHead;
					break;
				case DesignSlot.Legs:
					anchor = p.legPosition + drawInfo.legVect;
					origin = drawInfo.legVect;
					rotation = p.legRotation;
					color = drawInfo.colorArmorLegs;
					shader = drawInfo.cLegs;
					break;
				default:
					anchor = p.bodyPosition + drawInfo.bodyVect;
					origin = drawInfo.bodyVect;
					rotation = p.bodyRotation;
					color = drawInfo.colorArmorBody;
					shader = Slot == DesignSlot.Front ? drawInfo.cFront : Slot == DesignSlot.Back ? drawInfo.cBack : drawInfo.cBody;
					break;
			}

			Vector2 bob = Vector2.Zero;
			if (!overridden && Slots.Bobs(Slot)) {
				bool flipped = drawInfo.playerEffect.HasFlag(SpriteEffects.FlipVertically);
				bob.Y = Slots.BobFor(pose) * (flipped ? -1 : 1);
			}

			Point off = Slots.FrameOffset(Slot);
			origin += new Vector2(off.X, off.Y);

			// a shortsword, guitar or golf club turns the arm freely: draw the sleeve separately so it can follow
			bool freeArms = CompositeArms.Applies(Slot, ref drawInfo);
			int cuts = freeArms ? CompositeArms.Cuts(p) : 0;
			int frame = Math.Clamp(vp.FrameFor(bake), 0, bake.Frames - 1);

			foreach (BakedPass pass in bake.Passes) {
				Texture2D tex = pass.GetTexture(bake.AtlasWidth, bake.AtlasHeight);
				if (tex == null)
					continue;
				Color c = PassColor(pass, color);
				int passShader = PassShader(pass, shader);
				if (!freeArms) {
					drawInfo.DrawDataCache.Add(new DrawData(tex, basePos + anchor + bob, src, c, rotation, origin, 1f, drawInfo.playerEffect) {
						shader = passShader
					});
					continue;
				}
				if (p.compositeBackArm.enabled) {
					Texture2D arm = CompositeArms.BackArm(bake, pass, frame, CompositeArms.StretchRow(drawInfo.compBackArmFrame), drawInfo.skinVar, p.Male);
					Vector2 at = ArmPosition(ref drawInfo, false, out Vector2 armOrigin);
					drawInfo.DrawDataCache.Add(new DrawData(arm, at, null, c, p.bodyRotation + drawInfo.compositeBackArmRotation, armOrigin, 1f, drawInfo.playerEffect) {
						shader = passShader
					});
				}
				Texture2D torso = CompositeArms.Torso(bake, pass, frame, pose, cuts, drawInfo.skinVar, p.Male);
				drawInfo.DrawDataCache.Add(new DrawData(torso, basePos + anchor + bob, null, c, rotation, origin, 1f, drawInfo.playerEffect) {
					shader = passShader
				});
				// the front sleeve goes on in FreeArmOutfitLayer, over the held item like vanilla's arm
			}
		}

		// lighting already includes the immune flicker / potion alpha; glow keeps that alpha but not the darkness
		internal static Color PassColor(BakedPass pass, Color lit) {
			Color c = pass.Glow || pass.Pulse ? Color.White * (lit.A / 255f) : lit;
			if (pass.Pulse)
				c *= Retextures.PulseAlpha;
			return c;
		}

		internal static int PassShader(BakedPass pass, int slotShader) =>
			pass.DyeItem > 0 ? GameShaders.Armor.GetShaderIdFromItemId(pass.DyeItem) : slotShader;

		/// <summary>Same spot and pivot vanilla uses for its own free arms (PlayerDrawLayers' composite arm drawing).</summary>
		internal static Vector2 ArmPosition(ref PlayerDrawSet info, bool front, out Vector2 origin) {
			Player p = info.drawPlayer;
			var at = new Vector2(
				(int)(info.Position.X - Main.screenPosition.X - p.bodyFrame.Width / 2 + p.width / 2),
				(int)(info.Position.Y - Main.screenPosition.Y + p.height - p.bodyFrame.Height + 4f))
				+ p.bodyPosition + new Vector2(p.bodyFrame.Width / 2, p.bodyFrame.Height / 2);
			int row = p.bodyFrame.Height > 0 ? Math.Clamp(p.bodyFrame.Y / p.bodyFrame.Height, 0, Main.OffsetsPlayerHeadgear.Length - 1) : 0;
			Vector2 headgear = Main.OffsetsPlayerHeadgear[row];
			headgear.Y -= 2f;
			bool flipX = info.playerEffect.HasFlag(SpriteEffects.FlipHorizontally);
			bool flipY = info.playerEffect.HasFlag(SpriteEffects.FlipVertically);
			at += headgear * -flipY.ToDirectionInt();
			int sx = flipX ? -1 : 1, sy = flipY ? -1 : 1;
			Vector2 offset;
			if (front) {
				offset = new Vector2(-5 * sx, 0f);
				at += offset;
				if (info.compFrontArmFrame.Width > 0 && info.compFrontArmFrame.X / info.compFrontArmFrame.Width >= 7)
					at += new Vector2(sx, sy);
			}
			else {
				at.Y += info.torsoOffset;
				offset = new Vector2(6 * sx, 2 * sy);
				at += offset;
			}
			origin = info.bodyVect + offset;
			return at;
		}
	}

	/// <summary>The front sleeve while the arm is turned freely, drawn over the held item like vanilla's own arm.</summary>
	public class FreeArmOutfitLayer : PlayerDrawLayer
	{
		public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.ArmOverItem, PlayerDrawLayers.HandOnAcc);

		public override bool GetDefaultVisibility(PlayerDrawSet drawInfo) {
			if (drawInfo.shadow != 0f || drawInfo.headOnlyRender)
				return false;
			Player p = drawInfo.drawPlayer;
			if (p.dead || !p.compositeFrontArm.enabled || !p.TryGetModPlayer(out VanityPlayer vp))
				return false;
			if (p.invis && !vp.IsDummy)
				return false;
			return vp.Drawn(DesignSlot.Body) != null && CompositeArms.Applies(DesignSlot.Body, ref drawInfo);
		}

		protected override void Draw(ref PlayerDrawSet drawInfo) {
			Player p = drawInfo.drawPlayer;
			var vp = p.GetModPlayer<VanityPlayer>();
			BakedDesign bake = vp.Drawn(DesignSlot.Body);
			if (bake == null)
				return;
			int frame = Math.Clamp(vp.FrameFor(bake), 0, bake.Frames - 1);
			int stretch = CompositeArms.StretchRow(drawInfo.compFrontArmFrame);
			Vector2 at = OutfitLayer.ArmPosition(ref drawInfo, true, out Vector2 origin);
			float rotation = p.bodyRotation + drawInfo.compositeFrontArmRotation;
			foreach (BakedPass pass in bake.Passes) {
				if (pass.Atlas == null)
					continue;
				Texture2D arm = CompositeArms.FrontArm(bake, pass, frame, stretch, drawInfo.skinVar, p.Male);
				drawInfo.DrawDataCache.Add(new DrawData(arm, at, null, OutfitLayer.PassColor(pass, drawInfo.colorArmorBody), rotation, origin, 1f, drawInfo.playerEffect) {
					shader = OutfitLayer.PassShader(pass, drawInfo.cBody)
				});
			}
		}
	}

	public class BackOutfitLayer : OutfitLayer
	{
		protected override DesignSlot Slot => DesignSlot.Back;
		public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Wings, PlayerDrawLayers.HairBack);
	}

	public class LegsOutfitLayer : OutfitLayer
	{
		protected override DesignSlot Slot => DesignSlot.Legs;
		public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Shoes, PlayerDrawLayers.Robe);
	}

	public class BodyOutfitLayer : OutfitLayer
	{
		protected override DesignSlot Slot => DesignSlot.Body;
		public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Torso, PlayerDrawLayers.OffhandAcc);
	}

	public class HeadOutfitLayer : OutfitLayer
	{
		protected override DesignSlot Slot => DesignSlot.Head;
		public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Head, PlayerDrawLayers.FinchNest);
	}

	public class FrontOutfitLayer : OutfitLayer
	{
		protected override DesignSlot Slot => DesignSlot.Front;
		public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.BladedGlove, PlayerDrawLayers.ProjectileOverArm);
	}
}
