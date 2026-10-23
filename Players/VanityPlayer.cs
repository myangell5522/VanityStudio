using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using VanityStudio.Core;
using VanityStudio.Net;
using VanityStudio.UI;

namespace VanityStudio.Players
{
	public class VanityPlayer : ModPlayer
	{
		// What this character has equipped (only meaningful for the local player, saved with the character).
		public readonly Guid[] Equipped = new Guid[Slots.Count];

		// What actually gets drawn on this player. For other players this comes from the network.
		public readonly BakedDesign[] Outfit = new BakedDesign[Slots.Count];

		// Set on the studio's preview clone only.
		public bool IsDummy;
		public BakedDesign PreviewBake;
		public int PreviewFrame = -1;
		public bool SuppressDesigns;
		public bool PreviewNight;

		private readonly bool[] netDirty = new bool[Slots.Count];
		private int netCooldown;

		public BakedDesign Drawn(DesignSlot slot) {
			if (SuppressDesigns)
				return null;
			if (IsDummy && PreviewBake != null && PreviewBake.Slot == slot)
				return PreviewBake;
			return Outfit[(int)slot];
		}

		public int FrameFor(BakedDesign b) {
			if (IsDummy && PreviewFrame >= 0 && b == PreviewBake)
				return PreviewFrame;
			return b.FrameAt(Main.GlobalTimeWrappedHourly * 1000.0);
		}

		public bool IsEquipped(Design d) => d != null && Equipped[(int)d.Slot] == d.Id;

		public void Equip(Design d) {
			int s = (int)d.Slot;
			// one design per slot, so unequip whatever this design used to be equipped as
			for (int i = 0; i < Slots.Count; i++) {
				if (i != s && Equipped[i] == d.Id)
					SetSlot(i, Guid.Empty, null);
			}
			SetSlot(s, d.Id, BakedDesign.From(d));
		}

		public void Unequip(DesignSlot slot) => SetSlot((int)slot, Guid.Empty, null);

		// Called by the studio while drawing so the worn copy (and everyone else's view of it) stays live.
		public void RefreshLive(Design d) {
			int s = (int)d.Slot;
			for (int i = 0; i < Slots.Count; i++) {
				if (i != s && Equipped[i] == d.Id) {
					// slot was changed in the editor, follow it
					SetSlot(i, Guid.Empty, null);
					SetSlot(s, d.Id, BakedDesign.From(d));
					return;
				}
			}
			if (Equipped[s] != d.Id)
				return;
			if (Outfit[s] == null)
				Outfit[s] = BakedDesign.From(d);
			else
				Outfit[s].Rebake(d);
			netDirty[s] = true;
		}

		public void Forget(Guid id) {
			for (int i = 0; i < Slots.Count; i++) {
				if (Equipped[i] == id)
					SetSlot(i, Guid.Empty, null);
			}
		}

		private void SetSlot(int s, Guid id, BakedDesign bake) {
			Equipped[s] = id;
			Outfit[s]?.Dispose();
			Outfit[s] = bake;
			netDirty[s] = true;
		}

		public void RebuildLocalOutfit() {
			for (int i = 0; i < Slots.Count; i++) {
				Outfit[i]?.Dispose();
				Outfit[i] = null;
				Design d = DesignLibrary.Find(Equipped[i]);
				if (d != null && (int)d.Slot == i)
					Outfit[i] = BakedDesign.From(d);
				else
					Equipped[i] = Guid.Empty;
				netDirty[i] = true;
			}
		}

		public override void SaveData(TagCompound tag) {
			var list = new List<string>();
			for (int i = 0; i < Slots.Count; i++) {
				if (Equipped[i] != Guid.Empty)
					list.Add(i + ":" + Equipped[i].ToString("N"));
			}
			if (list.Count > 0)
				tag["equipped"] = list;
		}

		public override void LoadData(TagCompound tag) {
			Array.Fill(Equipped, Guid.Empty);
			foreach (string entry in tag.GetList<string>("equipped")) {
				int colon = entry.IndexOf(':');
				if (colon > 0 && int.TryParse(entry[..colon], out int slot) && slot >= 0 && slot < Slots.Count && Guid.TryParse(entry[(colon + 1)..], out Guid id))
					Equipped[slot] = id;
			}
		}

		public override void OnEnterWorld() {
			RebuildLocalOutfit();
			if (Main.netMode == NetmodeID.MultiplayerClient) {
				for (int i = 0; i < Slots.Count; i++)
					netDirty[i] = true;
				netCooldown = 0;
				SendDirty();
				OutfitNet.RequestOthers();
			}
		}

		public override void PostUpdate() {
			if (!Main.dedServ)
				EmitDust();
			if (Player.whoAmI != Main.myPlayer || Main.netMode != NetmodeID.MultiplayerClient)
				return;
			if (netCooldown > 0) {
				netCooldown--;
				return;
			}
			SendDirty();
		}

		// Particles come off the actual pixels of the layer that has them, so a glowing gem sparkles and the cloth doesn't.
		private void EmitDust() {
			if (Player.dead || Player.invis || !Player.active || Main.gamePaused)
				return;
			for (int s = 0; s < Slots.Count; s++) {
				BakedDesign b = Outfit[s];
				if (b == null)
					continue;
				foreach (BakedPass pass in b.Passes) {
					if (!pass.EmitsDust || !Main.rand.NextBool(Math.Max(1, (int)(1f / pass.DustRate))))
						continue;
					var slot = (DesignSlot)s;
					Rectangle frameRect = slot == DesignSlot.Legs ? Player.legFrame : Player.bodyFrame;
					int pose = frameRect.Height > 0 ? Math.Clamp(frameRect.Y / frameRect.Height, 0, Design.PoseCount - 1) : 0;
					int frame = FrameFor(b);
					Rectangle src = b.Source(frame, pose, out bool overridden);
					int[] pts = pass.EmitPoints(src.X * 1000 + src.Y, src, b.AtlasWidth);
					if (pts.Length == 0)
						continue;
					int pick = pts[Main.rand.Next(pts.Length)];
					int x = pick % b.Width, y = pick / b.Width;
					Point off = Slots.FrameOffset(slot);
					int bob = !overridden && Slots.Bobs(slot) ? Slots.BobFor(pose) : 0;
					if (Player.direction < 0)
						x = b.Width - 1 - x;
					var topLeft = new Vector2(Player.position.X + Player.width / 2f - 20 - off.X, Player.position.Y + Player.height - 56 + 4 - off.Y + bob);
					Dust d = Dust.NewDustPerfect(topLeft + new Vector2(x + 0.5f, y + 0.5f), pass.Dust, Player.velocity * 0.2f + Main.rand.NextVector2Circular(0.4f, 0.4f));
					d.noGravity = true;
					d.scale = Main.rand.NextFloat(0.7f, 1.1f);
					d.noLight = !pass.Glow;
				}
			}
		}

		private void SendDirty() {
			bool sent = false;
			for (int i = 0; i < Slots.Count; i++) {
				if (!netDirty[i])
					continue;
				netDirty[i] = false;
				OutfitNet.SendSlot(i, Outfit[i]);
				sent = true;
			}
			// a few updates per second is plenty for "watch me draw", and keeps big animated outfits from flooding the server
			if (sent)
				netCooldown = 12;
		}

		public override void PlayerDisconnect() {
			OutfitNet.Forget(Player.whoAmI);
			for (int i = 0; i < Slots.Count; i++) {
				Outfit[i]?.Dispose();
				Outfit[i] = null;
			}
		}

		public override void SetControls() {
			if (Player.whoAmI != Main.myPlayer || !StudioState.IsOpen)
				return;
			Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
			Player.controlJump = Player.controlUseItem = Player.controlUseTile = Player.controlThrow = false;
			Player.controlHook = Player.controlMount = Player.controlSmart = Player.controlTorch = false;
			Player.controlQuickHeal = Player.controlQuickMana = Player.controlInv = Player.controlMap = false;
		}

		public override void FrameEffects() {
			for (int i = 0; i < Slots.Count; i++) {
				BakedDesign b = Drawn((DesignSlot)i);
				if (b == null || !b.HideVanilla)
					continue;
				switch ((DesignSlot)i) {
					case DesignSlot.Head: Player.head = -1; break;
					case DesignSlot.Body: Player.body = -1; break;
					case DesignSlot.Legs: Player.legs = -1; break;
					case DesignSlot.Front: Player.front = -1; break;
					case DesignSlot.Back: Player.back = -1; break;
				}
			}
		}

		public override void HideDrawLayers(PlayerDrawSet drawInfo) {
			BakedDesign back = Drawn(DesignSlot.Back);
			if (back != null && back.HideVanilla)
				PlayerDrawLayers.Wings.Hide();
			if (!IsDummy)
				return;
			// The preview is a copy floating in the UI, not a real player in the world. Other mods' extra layers
			// (Calamity's among them) expect the real thing and have broken the studio before, so the preview
			// shows vanilla plus our own layers only.
			foreach (PlayerDrawLayer layer in PlayerDrawLayerLoader.Layers) {
				if (layer.Mod != null && layer.Mod != Mod && layer.Mod.Name != "ModLoader")
					layer.Hide();
			}
		}

		public override void ModifyDrawInfo(ref PlayerDrawSet drawInfo) {
			if (!IsDummy)
				return;
			// The preview clone floats somewhere on screen; world lighting there means nothing, so light it like the character menu.
			float a = 1f - drawInfo.shadow;
			float k = PreviewNight ? 0.3f : 1f;
			// darken without making it see-through, so glowing layers stand out like they would at night
			Color Shade(Color c) {
				c *= a;
				return new Color((int)(c.R * k), (int)(c.G * k), (int)(c.B * k), c.A);
			}
			Player p = drawInfo.drawPlayer;
			drawInfo.colorEyeWhites = Shade(Color.White);
			drawInfo.colorEyes = Shade(p.eyeColor);
			drawInfo.colorHair = Shade(p.GetHairColor(useLighting: false));
			drawInfo.colorHead = Shade(p.skinColor);
			drawInfo.colorBodySkin = Shade(p.skinColor);
			drawInfo.colorLegs = Shade(p.skinColor);
			drawInfo.colorShirt = Shade(p.shirtColor);
			drawInfo.colorUnderShirt = Shade(p.underShirtColor);
			drawInfo.colorPants = Shade(p.pantsColor);
			drawInfo.colorShoes = Shade(p.shoeColor);
			drawInfo.colorArmorHead = Shade(Color.White);
			drawInfo.colorArmorBody = Shade(Color.White);
			drawInfo.colorArmorLegs = Shade(Color.White);
			drawInfo.colorMount = Shade(Color.White);
			drawInfo.colorDisplayDollSkin = Shade(p.skinColor);
		}
	}
}
