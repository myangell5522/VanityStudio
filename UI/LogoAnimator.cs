using System;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;

namespace VanityStudio.UI
{
	/// <summary>
	/// icon.png has to be a still image, so while the mod is loaded we swap our entry's icon in the
	/// Mods menu for a texture that plays Assets/LogoAnim (the same loop as the Workshop GIF).
	/// Only reflection on the menu's elements, no detours: if tModLoader renames something the icon just stays still.
	/// </summary>
	public class LogoAnimator : ModSystem
	{
		private const int Frames = 40, FrameMs = 75, Size = 80, Columns = 10;

		private static Color[] atlas;
		private static int atlasWidth;
		private static Texture2D frameTex;
		private static readonly Color[] frameBuf = new Color[Size * Size];
		private static int shownFrame = -1;
		private static readonly Stopwatch clock = new();

		private static WeakReference<UIImage> target = new(null);
		private static UIState searchedState;
		private static int searchCooldown;
		private static FieldInfo modField, iconField;

		public override void Load() {
			if (Main.dedServ)
				return;
			clock.Start();
			Main.OnPreDraw += Animate;
		}

		public override void Unload() {
			Main.OnPreDraw -= Animate;
			Texture2D t = frameTex;
			frameTex = null;
			atlas = null;
			target = new WeakReference<UIImage>(null);
			searchedState = null;
			if (t != null)
				Main.QueueMainThreadAction(t.Dispose);
		}

		private static void Animate(GameTime gameTime) {
			try {
				if (!Main.gameMenu || Main.MenuUI?.CurrentState == null)
					return;
				if (!target.TryGetTarget(out UIImage image) || image.Parent == null || searchedState != Main.MenuUI.CurrentState) {
					// the list is rebuilt whenever the menu opens or filters change, so look again now and then
					if (searchCooldown-- > 0 && searchedState == Main.MenuUI.CurrentState)
						return;
					searchCooldown = 30;
					searchedState = Main.MenuUI.CurrentState;
					image = Find(searchedState);
					target = new WeakReference<UIImage>(image);
					if (image == null)
						return;
					EnsureTextures();
					if (frameTex == null)
						return;
					image.SetImage(frameTex);
					shownFrame = -1;
				}
				int frame = (int)(clock.ElapsedMilliseconds / FrameMs % Frames);
				if (frame == shownFrame || frameTex == null)
					return;
				shownFrame = frame;
				int fx = frame % Columns * Size, fy = frame / Columns * Size;
				for (int y = 0; y < Size; y++)
					Array.Copy(atlas, (fy + y) * atlasWidth + fx, frameBuf, y * Size, Size);
				frameTex.SetData(frameBuf);
			}
			catch (Exception e) {
				// a cosmetic nicety must never take the menu down
				Main.OnPreDraw -= Animate;
				ModContent.GetInstance<VanityStudio>()?.Logger.Warn("Animated mod icon disabled: " + e.Message);
			}
		}

		private static void EnsureTextures() {
			if (atlas == null) {
				Texture2D sheet = ModContent.Request<Texture2D>("VanityStudio/Assets/LogoAnim", AssetRequestMode.ImmediateLoad).Value;
				atlasWidth = sheet.Width;
				atlas = new Color[sheet.Width * sheet.Height];
				sheet.GetData(atlas);
			}
			frameTex ??= new Texture2D(Main.instance.GraphicsDevice, Size, Size);
		}

		private static UIImage Find(UIElement element) {
			Type t = element.GetType();
			if (t.Name == "UIModItem") {
				modField ??= t.GetField("_mod", BindingFlags.Instance | BindingFlags.NonPublic);
				iconField ??= t.GetField("_modIcon", BindingFlags.Instance | BindingFlags.NonPublic);
				object localMod = modField?.GetValue(element);
				string name = localMod?.GetType().GetProperty("Name")?.GetValue(localMod) as string;
				if (name == "VanityStudio")
					return iconField?.GetValue(element) as UIImage;
				return null;
			}
			foreach (UIElement child in element.Children) {
				UIImage found = Find(child);
				if (found != null)
					return found;
			}
			return null;
		}
	}
}
