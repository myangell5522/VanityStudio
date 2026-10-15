using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;
using Terraria.UI;
using VanityStudio.Core;
using VanityStudio.Net;

namespace VanityStudio.UI
{
	public class StudioSystem : ModSystem
	{
		// layers that keep running while the studio covers the screen
		private static readonly string[] keepLayers = {
			"Vanilla: Interface Logic 1", "Vanilla: Interface Logic 2", "Vanilla: Interface Logic 3", "Vanilla: Interface Logic 4",
			"Vanilla: Mouse Text", "Vanilla: Cursor", "Vanilla: Achievement Complete Popups"
		};

		public override void Load() {
			if (Main.dedServ)
				return;
			StudioSettings.Load();
			Gui.LoadAssets();
		}

		public override void Unload() {
			StudioState.Shutdown();
			Main.QueueMainThreadAction(() => {
				CanvasView.Unload();
				LibraryView.Unload();
				WorkshopClient.Unload();
			});
			TargetPicker.Unload();
			Targets.Unload();
			DesignLibrary.Unload();
			OutfitNet.Clear();
		}

		public override void OnWorldUnload() {
			StudioState.Shutdown();
			OutfitNet.Clear();
		}

		public override void PreSaveAndQuit() {
			StudioState.FlushSave();
			StudioSettings.Save();
		}

		public override void PostUpdateInput() {
			if (!StudioState.IsOpen)
				return;
			// The studio reads the keyboard itself; keep the same keys from also jumping, healing, opening the map...
			Block(PlayerInput.Triggers.Current.KeyStatus);
			Block(PlayerInput.Triggers.JustPressed.KeyStatus);
		}

		private static void Block(Dictionary<string, bool> keys) {
			foreach (string key in keys.Keys.ToList()) {
				if (!key.StartsWith("Mouse"))
					keys[key] = false;
			}
		}

		public override void UpdateUI(GameTime gameTime) {
			WorkshopClient.Pump();
			if (StudioState.ReopenInventory && !StudioState.IsOpen) {
				StudioState.ReopenInventory = false;
				Main.playerInventory = true;
			}
			if (!StudioState.IsOpen)
				return;
			if (Main.LocalPlayer.dead || Main.gameMenu) {
				StudioState.Close();
				return;
			}
			StudioState.Update();
		}

		// Other mods' cursor layers stay on too: plenty of mods (custom cursors, QoL packs) hide the vanilla
		// pointer and draw their own, and switching theirs off left people with no mouse at all inside the studio.
		private static bool Keep(string name) {
			if (keepLayers.Contains(name))
				return true;
			if (name.StartsWith("Vanilla: "))
				return false;
			return name.Contains("cursor", StringComparison.OrdinalIgnoreCase) || name.Contains("mouse", StringComparison.OrdinalIgnoreCase);
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
			if (!StudioState.IsOpen)
				return;
			foreach (GameInterfaceLayer layer in layers) {
				if (!Keep(layer.Name))
					layer.Active = false;
			}
			// under the tooltips and under every cursor, wherever other mods have moved those
			int at = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text" || l.Name == "Vanilla: Cursor" || (!l.Name.StartsWith("Vanilla: ") && Keep(l.Name)));
			if (at < 0)
				at = layers.Count;
			layers.Insert(at, new LegacyGameInterfaceLayer("VanityStudio: Studio", () => {
				if (StudioState.IsOpen)
					StudioWindow.Draw();
				return true;
			}, InterfaceScaleType.UI));
		}
	}
}
