using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;

namespace VanityStudio.UI
{
	/// <summary>
	/// Dresses up Vanity Studio's own entry in the Mods menu while the mod is loaded: the icon plays the animated logo,
	/// a slow colour wheel and sheen sit behind everything, and the name is redrawn in a soft palette gradient.
	/// Only reflection on the menu's elements, no detours: if tModLoader renames something the card simply stays plain.
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

		private static WeakReference<UIElement> card = new(null);
		private static UIState searchedState;
		private static int searchCooldown;

		public static float Time => (float)clock.Elapsed.TotalSeconds;

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
			card = new WeakReference<UIElement>(null);
			searchedState = null;
			if (t != null)
				Main.QueueMainThreadAction(t.Dispose);
		}

		private static void Animate(GameTime gameTime) {
			try {
				if (!Main.gameMenu || Main.MenuUI?.CurrentState == null)
					return;
				UIState state = Main.MenuUI.CurrentState;
				if (!card.TryGetTarget(out UIElement item) || item.Parent == null || searchedState != state) {
					// the list is rebuilt whenever the menu opens or filters change, so look again now and then
					if (searchCooldown-- > 0 && searchedState == state)
						return;
					searchCooldown = 30;
					searchedState = state;
					item = Find(state);
					card = new WeakReference<UIElement>(item);
					if (item == null)
						return;
					Dress(item);
					shownFrame = -1;
				}
				Restyle(item);
				AdvanceIcon();
			}
			catch (Exception e) {
				// a cosmetic nicety must never take the menu down
				Main.OnPreDraw -= Animate;
				ModContent.GetInstance<VanityStudio>()?.Logger.Warn("Animated mod card disabled: " + e.Message);
			}
		}

		private static UIElement Find(UIElement element) {
			Type t = element.GetType();
			if (t.Name == "UIModItem") {
				object localMod = Field(t, "_mod")?.GetValue(element);
				string name = localMod?.GetType().GetProperty("Name")?.GetValue(localMod) as string;
				return name == "VanityStudio" ? element : null;
			}
			foreach (UIElement child in element.Children) {
				UIElement found = Find(child);
				if (found != null)
					return found;
			}
			return null;
		}

		private static FieldInfo Field(Type t, string name) => t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

		private static void Dress(UIElement item) {
			Type t = item.GetType();
			EnsureTextures();
			if (Field(t, "_modIcon")?.GetValue(item) is UIImage icon && frameTex != null)
				icon.SetImage(frameTex);

			var elements = typeof(UIElement).GetField("Elements", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(item) as List<UIElement>;
			if (elements == null || elements.Exists(e => e is CardBackdrop))
				return;
			object adjustRaw = Field(t, "_modIconAdjust")?.GetValue(item);
			float adjust = adjustRaw is IConvertible ? Convert.ToSingle(adjustRaw) : 85f;
			var back = new CardBackdrop(adjust);
			back.Width.Set(0f, 1f);
			back.Height.Set(0f, 1f);
			back.IgnoresMouseInteraction = true;
			// inserted first so it draws above the panel but under the icon and the buttons
			elements.Insert(0, back);
			typeof(UIElement).GetProperty("Parent")?.SetValue(back, item);

			if (Field(t, "_modName")?.GetValue(item) is UIText name) {
				var title = new CardTitle(name);
				title.Width.Set(0f, 1f);
				title.Height.Set(0f, 1f);
				title.IgnoresMouseInteraction = true;
				item.Append(title);
				name.TextColor = Color.Transparent;
				name.ShadowColor = Color.Transparent;
			}
			item.Recalculate();
		}

		private static void Restyle(UIElement item) {
			if (item is not UIPanel panel)
				return;
			float t = Time;
			// a slow drift between the studio's purple and blue, a touch brighter on hover
			Color a = new(88, 58, 150), b = new(46, 70, 150);
			Color bg = Color.Lerp(a, b, 0.5f + 0.5f * (float)Math.Sin(t * 0.7f));
			panel.BackgroundColor = item.IsMouseHovering ? Color.Lerp(bg, Color.White, 0.12f) : bg * 0.9f;
			panel.BorderColor = item.IsMouseHovering ? Main.OurFavoriteColor : Color.Lerp(new Color(130, 100, 220), new Color(89, 116, 213), 0.5f + 0.5f * (float)Math.Sin(t * 1.3f));
		}

		private static void AdvanceIcon() {
			if (frameTex == null)
				return;
			int frame = (int)(clock.ElapsedMilliseconds / FrameMs % Frames);
			if (frame == shownFrame)
				return;
			shownFrame = frame;
			int fx = frame % Columns * Size, fy = frame / Columns * Size;
			for (int y = 0; y < Size; y++)
				Array.Copy(atlas, (fy + y) * atlasWidth + fx, frameBuf, y * Size, Size);
			frameTex.SetData(frameBuf);
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

		/// <summary>
		/// The card's background: a soft vertical shade, a slow sheen sweeping across, a pixel colour wheel peeking in
		/// from the right edge, and the divider under the name tinted like a palette. Kept dim so the text stays the star.
		/// </summary>
		private sealed class CardBackdrop : UIElement
		{
			private readonly float iconAdjust;

			public CardBackdrop(float iconAdjust) {
				this.iconAdjust = iconAdjust;
			}

			protected override void DrawSelf(SpriteBatch sb) {
				CalculatedStyle box = Parent.GetInnerDimensions();
				float t = Time;
				Texture2D px = TextureAssets.MagicPixel.Value;
				var area = new Rectangle((int)(box.X + iconAdjust), (int)box.Y, (int)(box.Width - iconAdjust), (int)box.Height);
				if (area.Width <= 8 || area.Height <= 8)
					return;
				bool hover = Parent.IsMouseHovering;

				Shade(sb, px, area);
				Wheel(sb, px, area, t, hover);
				Sheen(sb, px, area, t);
				Twinkles(sb, px, area, t);
				Divider(sb, px, box, t);
			}

			// darker towards the bottom and the far right, so the card reads as a lit surface instead of a flat fill
			private static void Shade(SpriteBatch sb, Texture2D px, Rectangle area) {
				for (int y = 0; y < area.Height; y += 2) {
					float k = y / (float)area.Height;
					sb.Draw(px, new Rectangle(area.X, area.Y + y, area.Width, 2), Color.Black * (0.04f + 0.16f * k * k));
				}
				sb.Draw(px, new Rectangle(area.X, area.Y, area.Width, 1), Color.White * 0.06f);
			}

			// two rings of 2x2 pixels turning in opposite directions, centred just past the right edge
			private static void Wheel(SpriteBatch sb, Texture2D px, Rectangle area, float t, bool hover) {
				var centre = new Vector2(area.Right + 6, area.Center.Y);
				float strength = hover ? 0.32f : 0.2f;
				Ring(sb, px, area, centre, 44f, t * 0.25f, strength, 96);
				Ring(sb, px, area, centre, 33f, -t * 0.35f, strength * 0.7f, 72);
				Ring(sb, px, area, centre, 23f, t * 0.5f, strength * 0.45f, 48);
			}

			private static void Ring(SpriteBatch sb, Texture2D px, Rectangle area, Vector2 centre, float radius, float spin, float alpha, int dots) {
				for (int i = 0; i < dots; i++) {
					float a = i / (float)dots * MathHelper.TwoPi;
					int x = (int)(centre.X + Math.Cos(a + spin) * radius);
					int y = (int)(centre.Y + Math.Sin(a + spin) * radius);
					if (x < area.X || x + 2 > area.Right || y < area.Y + 1 || y + 2 > area.Bottom)
						continue;
					// fade out towards the card's left so the wheel melts in rather than ending in a hard arc
					float fade = MathHelper.Clamp((x - (area.Right - 70)) / 40f, 0f, 1f);
					Color c = Main.hslToRgb(i / (float)dots, 0.8f, 0.65f) * (alpha * fade);
					sb.Draw(px, new Rectangle(x, y, 2, 2), c);
				}
			}

			// a faint band of light every seven seconds, leaning like light on glass
			private static void Sheen(SpriteBatch sb, Texture2D px, Rectangle area, float t) {
				float cycle = t % 7f / 2.2f;
				if (cycle > 1f)
					return;
				float mid = area.X - 40 + cycle * (area.Width + 80);
				for (int y = 0; y < area.Height; y += 2) {
					float lean = (area.Height - y) * 0.45f;
					for (int dx = -18; dx <= 18; dx += 2) {
						int x = (int)(mid + lean + dx);
						if (x < area.X || x + 2 > area.Right)
							continue;
						float a = 1f - Math.Abs(dx) / 18f;
						sb.Draw(px, new Rectangle(x, area.Y + y, 2, 2), Color.White * (0.045f * a * a));
					}
				}
			}

			// three quiet sparkles in the empty right half, taking turns
			private static void Twinkles(SpriteBatch sb, Texture2D px, Rectangle area, float t) {
				for (int i = 0; i < 3; i++) {
					float phase = (t * 0.35f + i / 3f) % 1f;
					if (phase > 0.3f)
						continue;
					float a = (float)Math.Sin(phase / 0.3f * Math.PI) * 0.55f;
					int x = area.Right - 110 + i * 31;
					int y = area.Y + 44 + i * 13 % Math.Max(1, area.Height - 50);
					if (x < area.X || y + 3 > area.Bottom)
						continue;
					Color c = Color.Lerp(Color.White, Main.hslToRgb(i * 0.3f, 0.8f, 0.8f), 0.4f) * a;
					sb.Draw(px, new Rectangle(x, y - 2, 1, 5), c);
					sb.Draw(px, new Rectangle(x - 2, y, 5, 1), c);
					sb.Draw(px, new Rectangle(x, y, 1, 1), Color.White * a);
				}
			}

			// the vanilla divider under the name, washed with the palette and a bead of light running along it
			private void Divider(SpriteBatch sb, Texture2D px, CalculatedStyle box, float t) {
				int x0 = (int)(box.X + 5 + iconAdjust), y = (int)box.Y + 31;
				int w = (int)(box.Width - 10 - iconAdjust);
				if (w <= 4)
					return;
				for (int x = 0; x < w; x += 2) {
					float k = x / (float)w;
					float edge = Math.Min(1f, Math.Min(k, 1f - k) * 8f);
					Color c = Main.hslToRgb((k * 0.8f + t * 0.05f) % 1f, 0.75f, 0.65f) * (0.45f * edge);
					sb.Draw(px, new Rectangle(x0 + x, y, 2, 1), c);
				}
				float bead = t * 0.3f % 1.6f;
				if (bead <= 1f) {
					int bx = x0 + (int)(bead * w);
					for (int d = -12; d <= 12; d++) {
						int x = bx + d;
						if (x < x0 || x >= x0 + w)
							continue;
						float a = 1f - Math.Abs(d) / 12f;
						sb.Draw(px, new Rectangle(x, y, 1, 1), Color.White * (0.6f * a * a));
					}
				}
			}
		}

		/// <summary>The mod name in a soft palette gradient that slowly slides along, with a gold version number.</summary>
		private sealed class CardTitle : UIElement
		{
			private readonly UIText source;

			public CardTitle(UIText source) {
				this.source = source;
			}

			protected override void DrawSelf(SpriteBatch sb) {
				string text = source.Text;
				if (string.IsNullOrEmpty(text))
					return;
				CalculatedStyle dims = source.GetDimensions();
				DynamicSpriteFont font = FontAssets.MouseText.Value;
				float t = Time;
				int split = text.LastIndexOf(" v", StringComparison.Ordinal);
				string name = split > 0 ? text[..split] : text;
				string version = split > 0 ? text[split..] : "";

				var pos = new Vector2(dims.X, dims.Y);
				int count = Math.Max(1, name.Length - 1);
				for (int i = 0; i < name.Length; i++) {
					string s = name[i].ToString();
					float k = i / (float)count;
					// violet through pink to warm gold, drifting a little so it never sits still
					float hue = 0.76f + k * 0.32f + (float)Math.Sin(t * 0.6f) * 0.04f;
					Color c = Main.hslToRgb(hue % 1f, 0.75f, 0.74f);
					float wave = (float)Math.Sin(t * 2.5f - i * 0.45f);
					var at = pos + new Vector2(0f, (float)Math.Round(wave));
					Utils.DrawBorderString(sb, s, at, c);
					float glint = t * 0.5f % 4f * 10f - i;
					if (glint > 0f && glint < 1.5f)
						Utils.DrawBorderString(sb, s, at, Color.White * (0.7f * (1.5f - glint) / 1.5f));
					pos.X += font.MeasureString(s).X;
				}
				if (version.Length > 0)
					Utils.DrawBorderString(sb, version, pos, new Color(255, 214, 110), 0.85f);
			}
		}
	}
}
