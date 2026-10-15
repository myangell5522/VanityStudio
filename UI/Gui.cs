using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ReLogic.Content;
using ReLogic.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;

namespace VanityStudio.UI
{
	/// <summary>
	/// Small immediate-mode toolkit for the studio. Everything is drawn with vanilla textures
	/// (panel backs, inventory slots, colour bars) so resource packs re-skin the studio along with the rest of the UI.
	/// </summary>
	internal static class Gui
	{
		public static readonly Color PanelBlue = new(63, 82, 151);
		public static readonly Color PanelDark = new Color(33, 43, 79) * 0.9f;
		public static readonly Color ButtonIdle = new Color(63, 82, 151) * 0.8f;
		public static readonly Color ButtonHover = new(73, 94, 171);
		public static readonly Color ButtonOn = new(89, 116, 213);
		public static readonly Color Gold = Main.OurFavoriteColor;
		public static readonly Color Muted = new(170, 172, 196);

		public static SpriteBatch Sb => Main.spriteBatch;

		public static Vector2 Mouse;
		public static Point MousePoint => new((int)Mouse.X, (int)Mouse.Y);
		public static bool LeftDown, LeftPressed, LeftReleased;
		public static bool RightDown, RightPressed, RightReleased;
		public static bool MiddleDown;
		public static int Scroll;

		// whoever grabbed the mouse (slider, canvas stroke...) keeps it until release
		public static string Active;
		public static string Focus;
		public static string Tip;

		private static bool prevLeft, prevRight;
		private static string hovered, lastHovered;
		private static KeyboardState keys, oldKeys;
		private static int clickedTextTick;

		private static Asset<Texture2D> panelBack, panelBorder, icons;
		public static Texture2D Pixel => TextureAssets.MagicPixel.Value;

		public static void LoadAssets() {
			panelBack = Main.Assets.Request<Texture2D>("Images/UI/PanelBackground");
			panelBorder = Main.Assets.Request<Texture2D>("Images/UI/PanelBorder");
			icons = ModContent.Request<Texture2D>("VanityStudio/Assets/Icons");
		}

		public static void BeginFrame() {
			Mouse = new Vector2(Main.mouseX, Main.mouseY);
			LeftDown = Main.mouseLeft;
			RightDown = Main.mouseRight;
			MiddleDown = Main.mouseMiddle;
			LeftPressed = LeftDown && !prevLeft;
			LeftReleased = !LeftDown && prevLeft;
			RightPressed = RightDown && !prevRight;
			RightReleased = !RightDown && prevRight;
			prevLeft = LeftDown;
			prevRight = RightDown;
			Scroll = PlayerInput.ScrollWheelDeltaForUI;
			if (Scroll != 0)
				PlayerInput.ScrollWheelDeltaForUI = 0;
			oldKeys = keys;
			keys = Main.keyState;
			Tip = null;
			hovered = null;
			if (!LeftDown && !RightDown && !MiddleDown)
				Active = null;
		}

		private static readonly System.Collections.Generic.List<Action> overlays = new();

		/// <summary>Queue something to draw on top of the whole window this frame (hover previews and the like).</summary>
		public static void Overlay(Action draw) => overlays.Add(draw);

		public static void FlushOverlays() {
			foreach (Action a in overlays)
				a();
			overlays.Clear();
		}

		public static void EndFrame() {
			FlushOverlays();
			if (hovered != null && hovered != lastHovered)
				SoundEngine.PlaySound(SoundID.MenuTick);
			lastHovered = hovered;
			if (!string.IsNullOrEmpty(Tip))
				Main.instance.MouseText(Tip);
		}

		public static void ResetInput() {
			Locked = false;
			Active = null;
			Focus = null;
			prevLeft = prevRight = true;
			lastHovered = null;
		}

		public static bool Typing => Focus != null;
		public static bool Ctrl => keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
		public static bool Shift => keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
		public static bool Alt => keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
		public static bool KeyDown(Keys k) => keys.IsKeyDown(k);
		public static bool KeyPressed(Keys k) => !Typing && !Locked && keys.IsKeyDown(k) && !oldKeys.IsKeyDown(k);
		public static bool EscapePressed => keys.IsKeyDown(Keys.Escape) && !oldKeys.IsKeyDown(Keys.Escape);

		// set while drawing inside a scrolled/clipped area so hidden widgets can't be clicked
		public static Rectangle? InputClip;

		// while the walkthrough is up, everything except its own panel ignores the mouse
		public static bool Locked;

		public static bool Hover(Rectangle r) => !Locked && r.Contains(MousePoint) && (InputClip == null || InputClip.Value.Contains(MousePoint));
		public static bool Free => Active == null;

		public static void MarkHover(string id, string tip = null) {
			hovered = id;
			if (tip != null)
				Tip = tip;
		}

		#region drawing

		public static void Rect(Rectangle r, Color c) => Sb.Draw(Pixel, r, c);

		public static void Outline(Rectangle r, Color c, int t = 1) {
			Rect(new Rectangle(r.X, r.Y, r.Width, t), c);
			Rect(new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
			Rect(new Rectangle(r.X, r.Y, t, r.Height), c);
			Rect(new Rectangle(r.Right - t, r.Y, t, r.Height), c);
		}

		public static void Panel(Rectangle r, Color bg, Color border) {
			int w = Math.Max(r.Width, 24), h = Math.Max(r.Height, 24);
			Utils.DrawSplicedPanel(Sb, panelBack.Value, r.X, r.Y, w, h, 12, 12, 12, 12, bg);
			Utils.DrawSplicedPanel(Sb, panelBorder.Value, r.X, r.Y, w, h, 12, 12, 12, 12, border);
		}

		public static void Panel(Rectangle r) => Panel(r, PanelDark, Color.Black);

		// the inventory-style background used by chests and NPC chat; follows the resource pack's inventory art
		public static void InvPanel(Rectangle r) => Utils.DrawInvBG(Sb, r, new Color(63, 65, 151, 255) * 0.785f);

		public static void Slot(Rectangle r, bool selected, bool hover) {
			Texture2D tex = selected ? TextureAssets.InventoryBack14.Value : TextureAssets.InventoryBack.Value;
			Color c = selected ? Color.White : hover ? Color.Lerp(Main.inventoryBack, Color.White, 0.35f) : Main.inventoryBack;
			// slot art has a rim, so slice it like a panel instead of stretching
			Utils.DrawSplicedPanel(Sb, tex, r.X, r.Y, Math.Max(r.Width, 20), Math.Max(r.Height, 20), 10, 10, 10, 10, c);
		}

		public static void Icon(int index, Vector2 center, Color? tint = null, float scale = 1f) {
			Texture2D tex = icons.Value;
			var src = new Rectangle(index % 12 * 20, index / 12 * 20, 20, 20);
			Sb.Draw(tex, center, src, tint ?? Color.White, 0f, new Vector2(10f), scale, SpriteEffects.None, 0f);
		}

		public static DynamicSpriteFont Font => FontAssets.MouseText.Value;

		public static Vector2 Measure(string s, float scale = 0.8f) => Font.MeasureString(s ?? "") * scale;

		public static void Text(string s, Vector2 pos, Color? c = null, float scale = 0.8f, float anchorX = 0f, float anchorY = 0f) {
			if (string.IsNullOrEmpty(s))
				return;
			Utils.DrawBorderString(Sb, s, pos, c ?? Color.White, scale, anchorX, anchorY);
		}

		public static string Fit(string s, float width, float scale = 0.8f) {
			if (Measure(s, scale).X <= width)
				return s;
			while (s.Length > 1 && Measure(s + "...", scale).X > width)
				s = s[..^1];
			return s + "...";
		}

		public static void Checker(Rectangle r, int cell) {
			Color a = new(54, 52, 70), b = new(66, 64, 84);
			Rect(r, a);
			if (cell <= 0)
				return;
			for (int y = 0; y * cell < r.Height; y++) {
				for (int x = (y & 1); x * cell < r.Width; x += 2) {
					var c = new Rectangle(r.X + x * cell, r.Y + y * cell, cell, cell);
					c = Rectangle.Intersect(c, r);
					if (c.Width > 0 && c.Height > 0)
						Rect(c, b);
				}
			}
		}

		#endregion

		#region batches

		private static readonly Item blankItem = new();
		private static readonly RasterizerState scissor = new() { CullMode = CullMode.None, ScissorTestEnable = true };
		private static bool clipping;

		/// <summary>Restart the batch clipped to a UI-space rectangle. Point sampling, since everything clipped here is pixel art.</summary>
		public static void BeginClip(Rectangle uiRect, bool immediate = false) {
			Sb.End();
			GraphicsDevice dev = Main.instance.GraphicsDevice;
			float s = Main.UIScale;
			var r = new Rectangle((int)(uiRect.X * s), (int)(uiRect.Y * s), (int)Math.Ceiling(uiRect.Width * s), (int)Math.Ceiling(uiRect.Height * s));
			r = Rectangle.Intersect(r, new Rectangle(0, 0, dev.PresentationParameters.BackBufferWidth, dev.PresentationParameters.BackBufferHeight));
			if (r.Width <= 0 || r.Height <= 0)
				r = new Rectangle(0, 0, 1, 1);
			dev.ScissorRectangle = r;
			clipping = true;
			Sb.Begin(immediate ? SpriteSortMode.Immediate : SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, scissor, null, Main.UIScaleMatrix);
		}

		public static void EndClip() {
			if (!clipping)
				return;
			clipping = false;
			Sb.End();
			Sb.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Main.UIScaleMatrix);
		}

		/// <summary>
		/// Draws a player so the bottom-centre of its hitbox lands on <paramref name="feet"/> (UI space), scaled around that point,
		/// which is exactly how vanilla's renderer scales. With an integer scale the 40x56 frame lands on whole pixels.
		/// </summary>
		public static void DrawPlayer(Player p, Vector2 feet, float scale, float shadow = 0f) {
			Vector2 saved = Main.screenPosition;
			Main.screenPosition = saved.Floor();
			try {
				Vector2 topLeft = Main.screenPosition + new Vector2((int)feet.X - p.width / 2, (int)feet.Y - p.height);
				p.position = topLeft;
				Item held = p.inventory[p.selectedItem];
				p.inventory[p.selectedItem] = blankItem;
				using (new Main.CurrentPlayerOverride(p))
					Main.PlayerRenderer.DrawPlayer(Main.Camera, p, topLeft, 0f, Vector2.Zero, shadow, scale);
				p.inventory[p.selectedItem] = held;
			}
			finally {
				Main.screenPosition = saved;
			}
		}

		#endregion

		#region widgets

		public static bool Button(Rectangle r, string label, string id, bool selected = false, bool enabled = true, string tip = null) {
			bool hover = enabled && Free && Hover(r);
			Color bg = selected ? ButtonOn : hover ? ButtonHover : ButtonIdle;
			Panel(r, bg, hover ? Gold : Color.Black);
			Text(Fit(label, r.Width - 10), new Vector2(r.Center.X, r.Center.Y + 2), enabled ? Color.White : Color.Gray, 0.8f, 0.5f, 0.5f);
			if (!hover)
				return false;
			MarkHover(id, tip);
			if (LeftPressed) {
				SoundEngine.PlaySound(SoundID.MenuTick);
				LeftPressed = false;
				return true;
			}
			return false;
		}

		public static bool IconButton(Rectangle r, int icon, string id, string tip, bool selected = false, bool enabled = true) {
			bool hover = enabled && Free && Hover(r);
			Slot(r, selected, hover);
			Icon(icon, r.Center.ToVector2(), enabled ? Color.White : Color.White * 0.35f);
			if (!hover)
				return false;
			MarkHover(id, tip);
			if (LeftPressed) {
				SoundEngine.PlaySound(SoundID.MenuTick);
				LeftPressed = false;
				return true;
			}
			return false;
		}

		/// <summary>Panel button with an icon on the left and a label, the way vanilla menus pair art with text.</summary>
		public static bool IconTextButton(Rectangle r, int icon, string label, string id, bool selected = false, string tip = null, bool enabled = true, float textScale = 0.8f) {
			bool hover = enabled && Free && Hover(r);
			Color bg = selected ? ButtonOn : hover ? ButtonHover : ButtonIdle;
			Panel(r, bg, hover ? Gold : selected ? Gold * 0.6f : Color.Black);
			Icon(icon, new Vector2(r.X + 16, r.Center.Y), enabled ? Color.White : Color.White * 0.4f, 0.9f);
			Text(Fit(label, r.Width - 36, textScale), new Vector2(r.X + 30, r.Center.Y + 2), enabled ? Color.White : Color.Gray, textScale, 0f, 0.5f);
			if (!hover)
				return false;
			MarkHover(id, tip);
			if (LeftPressed) {
				SoundEngine.PlaySound(SoundID.MenuTick);
				LeftPressed = false;
				return true;
			}
			return false;
		}

		public static int IconTextWidth(string label, float scale = 0.8f) => (int)Measure(label, scale).X + 44;

		public static bool Toggle(Rectangle r, string label, string id, ref bool value, string tip = null) {
			if (Button(r, label, id, value, true, tip)) {
				value = !value;
				return true;
			}
			return false;
		}

		public static bool Checkbox(Rectangle r, string label, string id, ref bool value) {
			bool hover = Free && Hover(r);
			var box = new Rectangle(r.X, r.Center.Y - 9, 18, 18);
			Slot(box, value, hover);
			if (value)
				Icon(Icons.Close, box.Center.ToVector2(), Gold, 0.7f);
			Text(label, new Vector2(box.Right + 8, r.Center.Y + 2), hover ? Color.White : Muted, 0.75f, 0f, 0.5f);
			if (!hover)
				return false;
			MarkHover(id);
			if (LeftPressed) {
				LeftPressed = false;
				value = !value;
				SoundEngine.PlaySound(SoundID.MenuTick);
				return true;
			}
			return false;
		}

		/// <summary>The character-creation colour bar: vanilla frame, painted gradient, vanilla slider knob.</summary>
		public static bool ColorBar(Vector2 pos, ref float value, string id, Func<float, Color> gradient) {
			Texture2D bar = gradient == null ? TextureAssets.Hue.Value : TextureAssets.ColorBar.Value;
			var area = new Rectangle((int)pos.X, (int)pos.Y, bar.Width, bar.Height);
			Sb.Draw(bar, pos, Color.White);
			if (gradient != null) {
				int inner = bar.Width - 11;
				for (int k = 0; k <= inner; k++)
					Sb.Draw(TextureAssets.ColorBlip.Value, new Vector2(pos.X + k + 5, pos.Y + 4), gradient(k / (float)inner));
			}
			var grab = new Rectangle(area.X - 4, area.Y - 4, area.Width + 8, area.Height + 8);
			bool hover = (Free && Hover(grab)) || Active == id;
			if (hover)
				Sb.Draw(TextureAssets.ColorHighlight.Value, pos, Gold);
			Texture2D knob = TextureAssets.ColorSlider.Value;
			Sb.Draw(knob, new Vector2(pos.X + (bar.Width - 2) * value - knob.Width / 2f, pos.Y - knob.Height / 2f + bar.Height / 2f), Color.White);
			if (Free && Hover(grab) && LeftPressed) {
				Active = id;
				LeftPressed = false;
			}
			if (Active == id && LeftDown) {
				float v = Math.Clamp((Mouse.X - pos.X) / bar.Width, 0f, 1f);
				bool changed = v != value;
				value = v;
				return changed;
			}
			return false;
		}

		public static int BarWidth => TextureAssets.Hue.Width();
		public static int BarHeight => TextureAssets.Hue.Height();

		/// <summary>Single-line text field. Returns true on the frame editing finishes.</summary>
		public static bool TextBox(Rectangle r, string id, ref string value, int maxLength, string placeholder = null, Func<char, bool> allow = null) {
			bool focused = Focus == id;
			bool hover = Free && Hover(r);
			Panel(r, focused ? new Color(40, 52, 98) : new Color(28, 36, 66), focused || hover ? Gold : Color.Black);
			if (hover && !focused)
				MarkHover(id);

			bool done = false;
			if (!focused && hover && LeftPressed) {
				Focus = id;
				focused = true;
				LeftPressed = false;
				clickedTextTick = (int)Main.GameUpdateCount;
				Main.clrInput();
				SoundEngine.PlaySound(SoundID.MenuTick);
			}
			else if (focused) {
				PlayerInput.WritingText = true;
				Main.instance.HandleIME();
				string edited = Main.GetInputText(value ?? "");
				if (allow != null) {
					var sb = new System.Text.StringBuilder();
					foreach (char ch in edited) {
						if (allow(ch))
							sb.Append(ch);
					}
					edited = sb.ToString();
				}
				if (edited.Length > maxLength)
					edited = edited[..maxLength];
				value = edited;
				bool clickedAway = (LeftPressed || RightPressed) && !hover && (int)Main.GameUpdateCount != clickedTextTick;
				if (Main.inputTextEnter || Main.inputTextEscape || clickedAway) {
					Focus = null;
					done = true;
					Main.inputTextEscape = false;
					Main.inputTextEnter = false;
				}
			}

			string shown = value;
			Color col = Color.White;
			if (string.IsNullOrEmpty(shown) && !focused && placeholder != null) {
				shown = placeholder;
				col = Muted;
			}
			string fitted = Fit(shown ?? "", r.Width - 16);
			if (focused && Main.GlobalTimeWrappedHourly % 1f < 0.5f)
				fitted += "|";
			Text(fitted, new Vector2(r.X + 8, r.Center.Y + 2), col, 0.8f, 0f, 0.5f);
			return done;
		}

		#endregion
	}
}
