using Microsoft.Xna.Framework;

namespace VanityStudio.Core
{
	public enum DesignSlot : byte
	{
		Head,
		Body,
		Legs,
		Front,
		Back
	}

	public static class Slots
	{
		public const int Count = 5;

		// Vanilla player frames are 40x56. Back pieces (capes, wings) get extra room
		// on every side so they can stick out past the body.
		public static Point CanvasSize(DesignSlot slot) => slot == DesignSlot.Back ? new Point(80, 72) : new Point(40, 56);

		public static Point FrameOffset(DesignSlot slot) {
			Point size = CanvasSize(slot);
			return new Point((size.X - 40) / 2, (size.Y - 56) / 2);
		}

		// Everything except trousers moves up and down with the torso while walking.
		public static bool Bobs(DesignSlot slot) => slot != DesignSlot.Legs;

		// Main.OffsetsPlayerHeadgear minus its resting 2px: the torso rises by 2 on these walk frames.
		private static readonly int[] bob = { 0, 0, 0, 0, 0, 0, 0, -2, -2, -2, 0, 0, 0, 0, -2, -2, -2, 0, 0, 0 };

		public static int BobFor(int pose) => pose >= 0 && pose < bob.Length ? bob[pose] : 0;

		public static string Key(DesignSlot slot) => slot.ToString();
	}
}
