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

			// lighting already includes the immune flicker / potion alpha; glow keeps that alpha but not the darkness
			float alpha = color.A / 255f;
			foreach (BakedPass pass in bake.Passes) {
				Texture2D tex = pass.GetTexture(bake.AtlasWidth, bake.AtlasHeight);
				if (tex == null)
					continue;
				Color c = pass.Glow || pass.Pulse ? Color.White * alpha : color;
				if (pass.Pulse)
					c *= Retextures.PulseAlpha;
				int passShader = pass.DyeItem > 0 ? GameShaders.Armor.GetShaderIdFromItemId(pass.DyeItem) : shader;
				drawInfo.DrawDataCache.Add(new DrawData(tex, basePos + anchor + bob, src, c, rotation, origin, 1f, drawInfo.playerEffect) {
					shader = passShader
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
