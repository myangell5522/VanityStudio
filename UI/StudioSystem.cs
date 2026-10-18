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
			"Vanilla: Mouse Text", "Vanilla: Achievement Complete Popups"
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
			Players.CompositeArms.Unload();
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

		// Inside the studio the pointer is our own (StudioCursor), so every cursor layer goes quiet: vanilla's, and the ones
		// custom-cursor mods add. Other mods' mouse-text layers stay, so their tooltips still show.
		private static bool Keep(string name) {
			if (name.Contains("cursor", StringComparison.OrdinalIgnoreCase))
				return false;
			if (keepLayers.Contains(name))
				return true;
			return !name.StartsWith("Vanilla: ") && name.Contains("mouse", StringComparison.OrdinalIgnoreCase);
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
			if (!StudioState.IsOpen)
				return;
			int cursorAt = layers.FindIndex(l => l.Name == "Vanilla: Cursor");
			foreach (GameInterfaceLayer layer in layers) {
				if (!Keep(layer.Name))
					layer.Active = false;
			}
			// the pointer goes where vanilla's was (above the tooltips), or on top of everything if that layer is gone
			layers.Insert(cursorAt < 0 ? layers.Count : cursorAt + 1, new LegacyGameInterfaceLayer("VanityStudio: Cursor", () => {
				if (StudioState.IsOpen) {
					try {
						StudioCursor.Draw();
					}
					catch (Exception e) {
						Gui.Fail("Cursor", e);
					}
				}
				return true;
			}, InterfaceScaleType.UI));

			// under the tooltips and the pointer, wherever other mods have moved those
			int at = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text" || l.Name == "VanityStudio: Cursor" || (!l.Name.StartsWith("Vanilla: ") && l.Active && Keep(l.Name)));
			if (at < 0)
				at = layers.Count;
			layers.Insert(at, new LegacyGameInterfaceLayer("VanityStudio: Studio", () => {
				// returning false or throwing here would stop vanilla from drawing the layers after ours
				if (StudioState.IsOpen) {
					try {
						StudioWindow.Draw();
					}
					catch (Exception e) {
						Gui.Fail("Studio", e);
					}
				}
				return true;
			}, InterfaceScaleType.UI));
		}
	}
}
