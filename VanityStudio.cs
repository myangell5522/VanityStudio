using System.IO;
using Terraria.ModLoader;
using VanityStudio.Net;

namespace VanityStudio
{
	public class VanityStudio : Mod
	{
		public static VanityStudio Instance { get; private set; }

		public override void Load() {
			Instance = this;
		}

		public override void Unload() {
			Instance = null;
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI) {
			OutfitNet.Receive(reader, whoAmI);
		}
	}
}
