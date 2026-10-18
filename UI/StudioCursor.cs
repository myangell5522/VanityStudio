using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ModLoader;

namespace VanityStudio.UI
{
	/// <summary>
	/// The studio's own pointer, in the logo's violet. Drawn in its own interface layer instead of relying on vanilla's,
	/// so nothing the studio (or another mod) does to the sprite batch can make the cursor vanish over the canvas.
	/// </summary>
	internal static class StudioCursor
	{
		// the arrow's tip, in texture pixels (there's a 1px outline around it)
		private static readonly Vector2 hotspot = new(1f, 1f);

		public static void Draw() {
			if (Main.gameMenu)
				return;
			Texture2D tex = ModContent.Request<Texture2D>("VanityStudio/Assets/StudioCursor", AssetRequestMode.ImmediateLoad).Value;
			SpriteBatch sb = Main.spriteBatch;
			// point sampling like vanilla's cursor, so the pixel art stays crisp at any cursor size
			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);

			var at = new Vector2(Main.mouseX, Main.mouseY);
			float scale = Main.cursorScale;
			// a shade darker while a button is held, like it's being pressed
			Color tint = Main.mouseLeft || Main.mouseRight ? new Color(215, 205, 230) : Color.White;
			sb.Draw(tex, at + new Vector2(1f, 1f) * scale, null, Color.Black * 0.35f, 0f, hotspot, scale * 1.05f, SpriteEffects.None, 0f);
			sb.Draw(tex, at, null, tint, 0f, hotspot, scale, SpriteEffects.None, 0f);
		}
	}
}
