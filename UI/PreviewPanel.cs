using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using VanityStudio.Core;
using VanityStudio.Players;

namespace VanityStudio.UI
{
	internal static class PreviewPanel
	{
		public const int Height = 260;

		private static readonly (PreviewMode mode, int icon, string key)[] modes = {
			(PreviewMode.Auto, Icons.Auto, "PvAuto"),
			(PreviewMode.Idle, Icons.FigStand, "PvIdle"),
			(PreviewMode.Walk, Icons.FigWalk, "PvWalk"),
			(PreviewMode.Jump, Icons.FigJump, "PvJump"),
			(PreviewMode.Fly, Icons.FigFly, "PvFly"),
			(PreviewMode.Use, Icons.FigSwing, "PvUse"),
			(PreviewMode.Pose, Icons.Reference, "PvPose")
		};

		public static void Draw(Rectangle r, Design d) {
			Gui.Panel(r);
			var stage = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, 206);
			bool night = StudioSettings.PreviewNight;
			Gui.Rect(stage, night ? new Color(8, 10, 24) : new Color(22, 26, 52));
			for (int i = 0; i < 6; i++)
				Gui.Rect(new Rectangle(stage.X, stage.Y + i * 12, stage.Width, 12), (night ? new Color(40, 40, 110) : new Color(70, 90, 160)) * (0.22f - i * 0.035f));
			if (night) {
				// a few stars
				for (int i = 0; i < 14; i++) {
					int sx = stage.X + (int)((i * 97 + 13) % stage.Width), sy = stage.Y + (int)((i * 53 + 7) % 90);
					float tw = 0.4f + 0.6f * (float)Math.Abs(Math.Sin(Main.GlobalTimeWrappedHourly * 1.3f + i));
					Gui.Rect(new Rectangle(sx, sy, 2, 2), Color.White * tw * 0.7f);
				}
			}
			Gui.Rect(new Rectangle(stage.X, stage.Bottom - 20, stage.Width, 20), night ? new Color(16, 14, 12) : new Color(40, 34, 30));
			Gui.Rect(new Rectangle(stage.X, stage.Bottom - 20, stage.Width, 3), night ? new Color(34, 56, 26) : new Color(94, 140, 60));
			Gui.Outline(stage, Color.Black * 0.7f);

			if (d.IsWearable)
				DrawCharacter(stage);
			else
				DrawSprite(stage, d);

			if (Gui.IconButton(new Rectangle(stage.Right - 30, stage.Y + 4, 26, 26), Icons.Flip, "turn", L.Get("TipTurn")))
				StudioState.PreviewDirection = -StudioState.PreviewDirection;
			if (Gui.IconButton(new Rectangle(stage.X + 4, stage.Y + 4, 26, 26), Icons.Moon, "night", L.Get("TipNight"), night)) {
				StudioSettings.PreviewNight = !night;
				StudioSettings.Save();
			}
			if (Gui.Free && Gui.Hover(stage)) {
				Gui.Tip ??= L.Get("TipPreview", StudioSettings.PreviewScale);
				if (Gui.Scroll != 0) {
					StudioSettings.PreviewScale = Math.Clamp(StudioSettings.PreviewScale + Math.Sign(Gui.Scroll), 1, 4);
					Gui.Scroll = 0;
				}
			}

			int count = d.IsWearable ? modes.Length : 2;
			int bw = (r.Width - 16 - (count - 1) * 3) / count;
			for (int i = 0; i < count; i++) {
				var (mode, icon, key) = d.IsWearable ? modes[i] : i == 0 ? modes[0] : modes[6];
				var b = new Rectangle(r.X + 8 + i * (bw + 3), stage.Bottom + 6, bw, 30);
				string tip = L.Get(key) + (mode == PreviewMode.Pose ? "\n" + L.Get("TipPvPose") : "");
				if (d.IsWearable ? Gui.IconButton(b, icon, "pv" + i, tip, StudioState.Preview == mode)
					: Gui.IconTextButton(b, icon, L.Get(key), "pv" + i, StudioState.Preview == mode, tip))
					StudioState.Preview = mode;
			}
		}

		private static void DrawCharacter(Rectangle stage) {
			Player dummy = StudioState.Dummy;
			if (dummy == null)
				return;
			int pose = StudioState.PreviewPoseNow(out bool flying);
			StudioState.SetDummyPose(pose);
			float lift = flying ? (float)Math.Sin(Main.GlobalTimeWrappedHourly * 3f) * 4f + 12f : pose == 5 ? 14f : 0f;
			var feet = new Vector2(stage.Center.X, stage.Bottom - 20 - lift);
			int scale = Math.Max(2, StudioSettings.PreviewScale);
			if (StudioState.DummyBroken) {
				DrawOutfitOnly(stage, dummy, feet, scale, pose);
				return;
			}

			Gui.BeginClip(stage, immediate: true);
			Gui.DrawPlayer(dummy, feet, scale);
			Gui.EndClip();
		}

		// fallback when the character can't be drawn: the worn pieces alone, stacked the way the game layers them
		private static readonly DesignSlot[] drawOrder = { DesignSlot.Back, DesignSlot.Legs, DesignSlot.Body, DesignSlot.Front, DesignSlot.Head };

		private static void DrawOutfitOnly(Rectangle stage, Player dummy, Vector2 feet, int scale, int pose) {
			var vp = dummy.GetModPlayer<VanityPlayer>();
			var fx = StudioState.PreviewDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Gui.BeginClip(stage);
			foreach (DesignSlot slot in drawOrder) {
				BakedDesign b = vp.Drawn(slot);
				if (b == null)
					continue;
				Rectangle src = b.Source(vp.FrameFor(b), pose, out bool overridden);
				Point off = Slots.FrameOffset(slot);
				int bob = !overridden && Slots.Bobs(slot) ? Slots.BobFor(pose) : 0;
				// the 40x56 player frame sits with its bottom 2px below the feet, like vanilla's renderer
				var topLeft = new Vector2(feet.X - 20 * scale - off.X * scale, feet.Y - 54 * scale - off.Y * scale + bob * scale);
				if (fx == SpriteEffects.FlipHorizontally)
					topLeft.X = feet.X + 20 * scale + off.X * scale - src.Width * scale;
				foreach (BakedPass p in b.Passes) {
					Texture2D tex = p.GetTexture(b.AtlasWidth, b.AtlasHeight);
					if (tex == null)
						continue;
					Color c = p.Pulse ? Color.White * Retextures.PulseAlpha : Color.White;
					Gui.Sb.Draw(tex, topLeft, src, c, 0f, Vector2.Zero, scale, fx, 0f);
				}
			}
			Gui.EndClip();
		}

		// retextures: the sprite itself, cycling through its sheet in Auto, or the frame being edited
		private static void DrawSprite(Rectangle stage, Design d) {
			BakedDesign b = StudioState.PreviewBake;
			if (b == null || b.Passes.Count == 0)
				return;
			int row = StudioState.Preview == PreviewMode.Auto && d.Rows > 1
				? (int)(Main.GlobalTimeWrappedHourly * 8f) % d.Rows
				: Math.Clamp(StudioState.Pose, 0, d.Rows - 1);
			int frame = StudioState.Playing ? b.FrameAt(Main.GlobalTimeWrappedHourly * 1000.0) : StudioState.Frame;
			Rectangle src = b.Source(frame, row, out _);
			int fit = Math.Max(1, Math.Min((stage.Width - 20) / Math.Max(1, d.Width), (stage.Height - 40) / Math.Max(1, d.Height)));
			int scale = Math.Clamp(Math.Min(fit, StudioSettings.PreviewScale + 1), 1, 6);
			var dst = new Rectangle(stage.Center.X - d.Width * scale / 2, stage.Bottom - 22 - d.Height * scale, d.Width * scale, d.Height * scale);
			if (dst.Y < stage.Y + 4)
				dst.Y = stage.Center.Y - dst.Height / 2;
			var fx = StudioState.PreviewDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			bool night = StudioSettings.PreviewNight;
			Gui.BeginClip(stage);
			foreach (BakedPass p in b.Passes) {
				Texture2D tex = p.GetTexture(b.AtlasWidth, b.AtlasHeight);
				if (tex == null)
					continue;
				Color c = p.Glow || p.Pulse ? Color.White : night ? new Color(70, 70, 80) : Color.White;
				if (p.Pulse)
					c *= Retextures.PulseAlpha;
				Gui.Sb.Draw(tex, dst, src, c, 0f, Vector2.Zero, fx, 0f);
			}
			Gui.EndClip();
		}
	}
}
