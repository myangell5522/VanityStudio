using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using VanityStudio.Core;
using VanityStudio.Players;

namespace VanityStudio.UI
{
	internal enum Tool
	{
		Pencil,
		Eraser,
		Fill,
		Picker,
		Line,
		Rect,
		Move
	}

	internal enum PreviewMode
	{
		Auto,
		Idle,
		Walk,
		Jump,
		Fly,
		Use,
		Pose
	}

	internal static class Icons
	{
		public const int Pencil = 0, Eraser = 1, Bucket = 2, Picker = 3, Line = 4, Rect = 5, Move = 6, Mirror = 7, Grid = 8, Onion = 9,
			Eye = 10, EyeOff = 11, Plus = 12, Minus = 13, Up = 14, Down = 15, Left = 16, Right = 17, Play = 18, Pause = 19, Copy = 20,
			Trash = 21, Undo = 22, Redo = 23, Save = 24, Export = 25, Import = 26, Folder = 27, Shirt = 28, Library = 29, Close = 30,
			Flip = 31, Merge = 32, Reference = 33, New = 34, Help = 35, Outline = 36, Filter = 37;
	}

	internal static class L
	{
		public static string Get(string key) => Language.GetTextValue("Mods.VanityStudio.UI." + key);
		public static string Get(string key, params object[] args) => Language.GetTextValue("Mods.VanityStudio.UI." + key, args);
		public static string Slot(DesignSlot s) => Get("Slot" + s);

		public static string Pose(int pose) {
			if (pose < 0)
				return Get("PoseBase");
			if (pose == 0)
				return Get("PoseStand");
			if (pose <= 4)
				return Get("PoseUse", pose);
			if (pose == 5)
				return Get("PoseJump");
			return Get("PoseWalk", pose - 5);
		}
	}

	/// <summary>Everything the open studio is working on. The window classes only read and poke at this.</summary>
	public static class StudioState
	{
		public static bool IsOpen { get; private set; }

		internal static Design Doc;
		internal static int Layer, Frame, Pose = Design.BasePose;
		internal static Tool Tool = Tool.Pencil;
		internal static Color Primary = new(232, 59, 59);
		internal static Color Secondary = Color.Transparent;
		internal static int Brush = 1;
		internal static bool Mirror, Onion, Playing;
		internal static bool LibraryOpen;
		internal static PreviewMode Preview = PreviewMode.Auto;
		internal static int PreviewDirection = 1;
		internal static float ReferenceAlpha = 0.45f;

		internal static Player Dummy;
		internal static BakedDesign PreviewBake;
		private static int bakedRevision = -1, bakedFor;
		private static int bakeDelay;

		private static readonly List<byte[]> undo = new(), redo = new();
		private static string snapshotKey;
		private static int saveTimer;
		private static double playClock;

		internal static string Toast;
		internal static int ToastTime;
		internal static bool ReopenInventory;

		internal static bool CanUndo => undo.Count > 0;
		internal static bool CanRedo => redo.Count > 0;

		internal static VanityPlayer Local => Main.LocalPlayer.GetModPlayer<VanityPlayer>();

		public static void Open() {
			if (Main.dedServ || IsOpen)
				return;
			DesignLibrary.EnsureLoaded();
			if (Doc == null || !DesignLibrary.All.Contains(Doc)) {
				Design last = DesignLibrary.Newest.FirstOrDefault();
				if (last != null)
					Load(last);
				else
					NewDesign(DesignSlot.Body);
			}
			MakeDummy();
			IsOpen = true;
			LibraryOpen = false;
			Gui.ResetInput();
			if (!StudioSettings.TutorialDone)
				Tutorial.Start();
			SoundEngine.PlaySound(SoundID.MenuOpen);
		}

		public static void Close() {
			if (!IsOpen)
				return;
			IsOpen = false;
			Playing = false;
			FlushSave();
			Dummy = null;
			Gui.ResetInput();
			StudioSettings.Save();
			SoundEngine.PlaySound(SoundID.MenuClose);
		}

		// world unload / mod unload: no sounds, no UI
		public static void Shutdown() {
			if (IsOpen)
				FlushSave();
			IsOpen = false;
			Dummy = null;
			Doc = null;
			PreviewBake?.Dispose();
			PreviewBake = null;
			bakedRevision = -1;
			undo.Clear();
			redo.Clear();
		}

		internal static void Load(Design d) {
			if (Doc != null && Doc != d)
				FlushSave();
			Doc = d;
			Layer = Math.Max(0, d.Layers.Count - 1);
			Frame = 0;
			Pose = Design.BasePose;
			Playing = false;
			undo.Clear();
			redo.Clear();
			bakedRevision = -1;
			CanvasView.ResetView = true;
		}

		// the open design was deleted: forget it without saving
		internal static void Forget() {
			Doc = null;
			saveTimer = 0;
			Playing = false;
			undo.Clear();
			redo.Clear();
			bakedRevision = -1;
		}

		internal static Design NewDesign(DesignSlot slot) {
			var d = Design.Create(slot, DesignLibrary.UniqueName(L.Get("NewName", L.Slot(slot))));
			DesignLibrary.Add(d);
			Load(d);
			return d;
		}

		#region history

		internal static void Snapshot() {
			if (Doc == null)
				return;
			undo.Add(Doc.ToBytes());
			if (undo.Count > 80)
				undo.RemoveAt(0);
			redo.Clear();
		}

		// one undo step per drag, no matter how many frames it lasts
		internal static void SnapshotOnce(string key) {
			if (snapshotKey == key)
				return;
			snapshotKey = key;
			Snapshot();
		}

		internal static void Undo() => Step(undo, redo);
		internal static void Redo() => Step(redo, undo);

		private static void Step(List<byte[]> from, List<byte[]> to) {
			if (from.Count == 0 || Doc == null)
				return;
			to.Add(Doc.ToBytes());
			byte[] state = from[^1];
			from.RemoveAt(from.Count - 1);
			Doc.CopyFrom(Design.FromBytes(state));
			ClampSelection();
			Changed();
			SoundEngine.PlaySound(SoundID.MenuTick);
		}

		internal static void ClampSelection() {
			Layer = Math.Clamp(Layer, 0, Doc.Layers.Count - 1);
			Frame = Math.Clamp(Frame, 0, Doc.FrameCount - 1);
		}

		#endregion

		internal static void Changed() {
			Doc.Touch();
			saveTimer = 90;
		}

		internal static void FlushSave() {
			if (Doc == null)
				return;
			if (saveTimer > 0 || Doc.Revision != 0) {
				DesignLibrary.Save(Doc);
				saveTimer = 0;
			}
		}

		internal static void Notify(string text) {
			Toast = text;
			ToastTime = 240;
		}

		internal static void Equip() {
			if (Doc == null)
				return;
			VanityPlayer vp = Local;
			if (vp.IsEquipped(Doc)) {
				vp.Unequip(Doc.Slot);
				SoundEngine.PlaySound(SoundID.Grab);
			}
			else {
				vp.Equip(Doc);
				SoundEngine.PlaySound(SoundID.Item64 with { Volume = 0.6f });
				Notify(L.Get("Equipped", Doc.Name));
			}
		}

		#region dummy / preview

		private static void MakeDummy() {
			try {
				Dummy = Main.LocalPlayer.SerializedClone();
			}
			catch (Exception e) {
				VanityStudio.Instance.Logger.Warn("Preview clone failed, using a blank character: " + e.Message);
				Dummy = new Player();
			}
			Dummy.dead = false;
			Dummy.direction = PreviewDirection;
			var vp = Dummy.GetModPlayer<VanityPlayer>();
			vp.IsDummy = true;
		}

		internal static void SetDummyPose(int legacyFrame) {
			if (Dummy == null)
				return;
			legacyFrame = Math.Clamp(legacyFrame, 0, Design.PoseCount - 1);
			Dummy.bodyFrame.Y = Dummy.legFrame.Y = Dummy.headFrame.Y = legacyFrame * 56;
			Dummy.direction = PreviewDirection;
		}

		internal static int PreviewPoseNow(out bool flying) {
			flying = false;
			float t = Main.GlobalTimeWrappedHourly;
			PreviewMode mode = Preview;
			if (mode == PreviewMode.Auto) {
				float cycle = t % 8f;
				mode = cycle < 1.6f ? PreviewMode.Idle : cycle < 4f ? PreviewMode.Walk : cycle < 4.8f ? PreviewMode.Jump : cycle < 6.8f ? PreviewMode.Fly : PreviewMode.Use;
			}
			switch (mode) {
				case PreviewMode.Walk: return (int)(t / 0.07f) % 14 + 6;
				case PreviewMode.Jump: return 5;
				case PreviewMode.Fly:
					flying = true;
					return 5;
				case PreviewMode.Use: return 1 + (int)(t / 0.09f) % 4;
				case PreviewMode.Pose: return Pose < 0 ? 0 : Pose;
				default: return 0;
			}
		}

		#endregion

		/// <summary>Runs every frame from UpdateUI while the studio is open.</summary>
		internal static void Update() {
			if (Doc == null)
				return;

			if (Playing && Doc.FrameCount > 1) {
				playClock += 1000.0 / 60.0;
				int time = Math.Max(10, Doc.FrameTimes[Frame]);
				while (playClock >= time) {
					playClock -= time;
					Frame = (Frame + 1) % Doc.FrameCount;
					time = Math.Max(10, Doc.FrameTimes[Frame]);
				}
			}
			else {
				playClock = 0;
			}

			if (!Gui.LeftDown && !Gui.RightDown)
				snapshotKey = null;

			// rebake at most every other frame, drawing fast with a big brush shouldn't stall
			if (bakeDelay > 0)
				bakeDelay--;
			if ((Doc.Revision != bakedRevision || bakedFor != Doc.GetHashCode()) && bakeDelay == 0) {
				if (PreviewBake == null)
					PreviewBake = BakedDesign.From(Doc);
				else
					PreviewBake.Rebake(Doc);
				bakedRevision = Doc.Revision;
				bakedFor = Doc.GetHashCode();
				bakeDelay = 2;
				Local.RefreshLive(Doc);
			}

			if (saveTimer > 0 && --saveTimer == 0)
				DesignLibrary.Save(Doc);

			if (ToastTime > 0)
				ToastTime--;

			UpdateDummy();
		}

		private static void UpdateDummy() {
			if (Dummy == null)
				return;
			var vp = Dummy.GetModPlayer<VanityPlayer>();
			VanityPlayer local = Local;
			for (int i = 0; i < Slots.Count; i++)
				vp.Outfit[i] = local.Outfit[i];
			vp.PreviewBake = PreviewBake;
			vp.PreviewFrame = Playing ? -1 : Frame;

			using (new Main.CurrentPlayerOverride(Dummy)) {
				Dummy.ResetEffects();
				Dummy.ResetVisibleAccessories();
				Dummy.UpdateMiscCounter();
				Dummy.UpdateDyes();
				Dummy.PlayerFrame();
				PreviewPoseNow(out bool flying);
				Dummy.velocity = flying ? new Vector2(0f, -2f) : Vector2.Zero;
				Dummy.WingFrame(flying);
			}
		}
	}
}
