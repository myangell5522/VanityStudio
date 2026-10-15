using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Steamworks;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Social;

namespace VanityStudio.Core
{
	public sealed class WorkshopEntry
	{
		[JsonPropertyName("id")] public string Id { get; set; } = "";
		[JsonPropertyName("name")] public string Name { get; set; } = "";
		[JsonPropertyName("description")] public string Description { get; set; } = "";
		[JsonPropertyName("kind")] public string Kind { get; set; } = "Wearable";
		[JsonPropertyName("slot")] public string Slot { get; set; } = "";
		[JsonPropertyName("target")] public string Target { get; set; } = "";
		[JsonPropertyName("basedOn")] public string BasedOn { get; set; } = "";
		[JsonPropertyName("authorId")] public string AuthorId { get; set; } = "";
		[JsonPropertyName("authorName")] public string AuthorName { get; set; } = "";
		[JsonPropertyName("path")] public string Path { get; set; } = "";
		[JsonPropertyName("created")] public string Created { get; set; } = "";
		[JsonPropertyName("frames")] public int Frames { get; set; } = 1;
		[JsonPropertyName("width")] public int Width { get; set; }
		[JsonPropertyName("height")] public int Height { get; set; }

		public DesignKind KindValue => Enum.TryParse(Kind, out DesignKind k) ? k : DesignKind.Wearable;
		public DesignSlot SlotValue => Enum.TryParse(Slot, out DesignSlot s) ? s : DesignSlot.Body;
	}

	internal sealed class WorkshopIndex
	{
		[JsonPropertyName("items")] public List<WorkshopEntry> Items { get; set; } = new();
	}

	internal sealed class WorkshopConfig
	{
		[JsonPropertyName("relay")] public string Relay { get; set; } = "";
	}

	/// <summary>
	/// The Studio Workshop lives in a public GitHub repository: index.json lists everything, and each creation sits in
	/// items/&lt;steam id&gt;/&lt;id&gt;/. Reading needs nothing but HTTPS. Publishing goes through a small relay (see the repo's
	/// relay/ folder) that holds the GitHub token, so no secret ever ships inside the mod.
	/// All network work runs on background tasks; textures are made on the main thread.
	/// </summary>
	public static class WorkshopClient
	{
		public const string Repo = "myangell5522/VanityStudioWorkshop";
		public const string Branch = "main";
		public static string RawBase => $"https://raw.githubusercontent.com/{Repo}/{Branch}/";
		public static string RepoUrl => $"https://github.com/{Repo}";

		private static readonly HttpClient http = MakeClient();

		public static List<WorkshopEntry> Entries = new();
		public static bool Loading;
		public static string Error;
		public static string Relay = "";
		public static DateTime LastFetch;

		public static string PublishStatus;
		public static bool Publishing;
		public static bool PublishOk;

		private static readonly ConcurrentQueue<Action> mainThread = new();
		private static readonly Dictionary<string, Texture2D> textures = new();
		private static readonly HashSet<string> requested = new();
		private static readonly HashSet<string> downloading = new();

		private static HttpClient MakeClient() {
			var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
			c.DefaultRequestHeaders.UserAgent.ParseAdd("VanityStudio-tModLoader/1.0");
			return c;
		}

		private static string CacheDir => System.IO.Path.Combine(DesignLibrary.Root, "WorkshopCache");

		/// <summary>Run queued main-thread work (texture uploads, library changes). Called from the studio's update.</summary>
		public static void Pump() {
			int n = 0;
			while (n++ < 8 && mainThread.TryDequeue(out Action a)) {
				try {
					a();
				}
				catch (Exception e) {
					ModContent.GetInstance<VanityStudio>()?.Logger.Warn("Workshop: " + e.Message);
				}
			}
		}

		public static void Refresh(bool force = false) {
			if (Loading || !force && Entries.Count > 0 && (DateTime.UtcNow - LastFetch).TotalMinutes < 5)
				return;
			Loading = true;
			Error = null;
			Task.Run(async () => {
				try {
					// a cache-buster: raw.githubusercontent caches for a few minutes and the list should feel live
					string bust = "?t=" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
					try {
						string cfg = await http.GetStringAsync(RawBase + "config.json" + bust);
						var config = JsonSerializer.Deserialize<WorkshopConfig>(cfg);
						Relay = (config?.Relay ?? "").Trim().TrimEnd('/');
					}
					catch (Exception) {
						Relay = "";
					}
					string json = await http.GetStringAsync(RawBase + "index.json" + bust);
					var index = JsonSerializer.Deserialize<WorkshopIndex>(json);
					var items = index?.Items?.Where(Valid).ToList() ?? new List<WorkshopEntry>();
					mainThread.Enqueue(() => {
						Entries = items;
						LastFetch = DateTime.UtcNow;
						Loading = false;
					});
				}
				catch (Exception e) {
					mainThread.Enqueue(() => {
						Error = e is HttpRequestException h && h.StatusCode == System.Net.HttpStatusCode.NotFound ? "empty" : e.Message;
						Loading = false;
					});
				}
			});
		}

		private static bool Valid(WorkshopEntry e) =>
			e != null && IsHex(e.Id, 32) && e.AuthorId.All(char.IsDigit) && !string.IsNullOrEmpty(e.Path) && !e.Path.Contains("..");

		private static bool IsHex(string s, int len) => s != null && s.Length == len && s.All(Uri.IsHexDigit);

		#region images

		public static Texture2D Thumb(WorkshopEntry e) => Image("thumb:" + e.Id, e.Path.TrimEnd('/') + "/thumb.png");

		public static Texture2D Avatar(string authorId) => string.IsNullOrEmpty(authorId) ? null : Image("avatar:" + authorId, $"authors/{authorId}/avatar.png");

		private static Texture2D Image(string key, string path) {
			if (textures.TryGetValue(key, out Texture2D t))
				return t;
			if (!requested.Add(key))
				return null;
			Task.Run(async () => {
				try {
					byte[] bytes = await http.GetByteArrayAsync(RawBase + path);
					Color[] px = Decode(bytes, out int w, out int h);
					if (px == null || w > 512 || h > 512)
						return;
					mainThread.Enqueue(() => {
						var tex = new Texture2D(Main.instance.GraphicsDevice, w, h);
						tex.SetData(Pixels.Premultiply(px));
						textures[key] = tex;
					});
				}
				catch (Exception) {
					// a missing avatar or thumbnail just shows the placeholder
				}
			});
			return null;
		}

		private static Color[] Decode(byte[] png, out int w, out int h) {
			w = h = 0;
			using var src = new MemoryStream(png);
			using var raw = new MemoryStream();
			if (!ImageIO.ToRaw(src, raw))
				return null;
			byte[] bytes = ImageIO.ReadRaw(new MemoryStream(raw.ToArray()), out w, out h);
			var px = new Color[w * h];
			for (int i = 0; i < px.Length; i++)
				px[i] = new Color(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
			return px;
		}

		public static void Unload() {
			foreach (Texture2D t in textures.Values)
				BakedDesign.DisposeLater(t);
			textures.Clear();
			requested.Clear();
			Entries = new List<WorkshopEntry>();
		}

		#endregion

		#region download

		public static bool IsDownloading(WorkshopEntry e) => downloading.Contains(e.Id);

		public static Design Owned(WorkshopEntry e) => DesignLibrary.All.FirstOrDefault(d => d.WorkshopId == e.Id);

		public static void Download(WorkshopEntry e, Action<Design> done) {
			if (!downloading.Add(e.Id))
				return;
			Task.Run(async () => {
				try {
					byte[] bytes = await http.GetByteArrayAsync(RawBase + e.Path.TrimEnd('/') + "/design.vsd");
					string tmp = System.IO.Path.Combine(CacheDir, e.Id + ".vsd");
					Directory.CreateDirectory(CacheDir);
					await File.WriteAllBytesAsync(tmp, bytes);
					mainThread.Enqueue(() => {
						downloading.Remove(e.Id);
						Design d = DesignLibrary.TryLoadFile(tmp);
						if (d == null) {
							done(null);
							return;
						}
						Design copy = d.Duplicate(DesignLibrary.UniqueName(string.IsNullOrWhiteSpace(e.Name) ? d.Name : e.Name));
						copy.WorkshopId = e.Id;
						copy.AuthorName = e.AuthorName;
						copy.AuthorId = ulong.TryParse(e.AuthorId, out ulong a) ? a : 0;
						copy.Description = e.Description;
						DesignLibrary.Add(copy);
						done(copy);
					});
				}
				catch (Exception ex) {
					mainThread.Enqueue(() => {
						downloading.Remove(e.Id);
						ModContent.GetInstance<VanityStudio>()?.Logger.Warn($"Workshop download of {e.Id} failed: {ex.Message}");
						done(null);
					});
				}
			});
		}

		#endregion

		#region identity

		/// <summary>Steam name and id of whoever is playing, or the character name when Steam isn't around.</summary>
		public static (ulong id, string name) Me() {
			try {
				if (SocialAPI.Mode == SocialMode.Steam) {
					CSteamID id = SteamUser.GetSteamID();
					return (id.m_SteamID, SteamFriends.GetPersonaName());
				}
			}
			catch (Exception) {
			}
			return (0, Main.LocalPlayer?.name ?? "Player");
		}

		/// <summary>The Steam avatar as a PNG, if Steam has it loaded (it usually does by the time you're in a world).</summary>
		public static byte[] MyAvatarPng() {
			try {
				if (SocialAPI.Mode != SocialMode.Steam)
					return null;
				int handle = SteamFriends.GetMediumFriendAvatar(SteamUser.GetSteamID());
				if (handle <= 0 || !SteamUtils.GetImageSize(handle, out uint w, out uint h) || w == 0 || h == 0 || w > 256 || h > 256)
					return null;
				var rgba = new byte[w * h * 4];
				if (!SteamUtils.GetImageRGBA(handle, rgba, rgba.Length))
					return null;
				var px = new Color[w * h];
				for (int i = 0; i < px.Length; i++)
					px[i] = new Color(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
				return SheetIO.PngBytes(px, (int)w, (int)h);
			}
			catch (Exception) {
				return null;
			}
		}

		#endregion

		#region publish

		public static bool CanPublish => !string.IsNullOrEmpty(Relay);

		public static void Publish(Design d, string name, string description) {
			if (Publishing)
				return;
			if (!CanPublish) {
				PublishOk = false;
				PublishStatus = "norelay";
				return;
			}
			Publishing = true;
			PublishStatus = null;

			var (authorId, authorName) = Me();
			byte[] avatar = MyAvatarPng();
			byte[] design;
			Design snapshot = Design.FromBytes(d.ToBytes());
			snapshot.Id = d.Id;
			snapshot.Name = name;
			snapshot.Description = description;
			snapshot.AuthorId = authorId;
			snapshot.AuthorName = authorName;
			using (var ms = new MemoryStream()) {
				ms.Write(Encoding.ASCII.GetBytes("VSD1"));
				ms.Write(snapshot.ToBytes());
				design = ms.ToArray();
			}
			byte[] thumb = ThumbPng(d);

			var payload = new Dictionary<string, object> {
				["meta"] = new Dictionary<string, object> {
					["id"] = d.Id.ToString("N"),
					["name"] = name,
					["description"] = description,
					["kind"] = d.Kind.ToString(),
					["slot"] = d.IsWearable ? d.Slot.ToString() : "",
					["target"] = d.Target ?? "",
					["basedOn"] = d.BasedOn ?? "",
					["authorId"] = authorId.ToString(),
					["authorName"] = authorName,
					["frames"] = d.FrameCount,
					["width"] = d.Width,
					["height"] = d.Height,
					["modVersion"] = ModContent.GetInstance<VanityStudio>().Version.ToString()
				},
				["design"] = Convert.ToBase64String(design),
				["thumb"] = Convert.ToBase64String(thumb),
				["avatar"] = avatar != null ? Convert.ToBase64String(avatar) : null
			};
			string body = JsonSerializer.Serialize(payload);
			string url = Relay + "/publish";
			Task.Run(async () => {
				string status;
				bool ok = false;
				try {
					using var content = new StringContent(body, Encoding.UTF8, "application/json");
					HttpResponseMessage res = await http.PostAsync(url, content);
					string text = await res.Content.ReadAsStringAsync();
					string message = "";
					try {
						using JsonDocument doc = JsonDocument.Parse(text);
						if (doc.RootElement.TryGetProperty("message", out JsonElement m))
							message = m.GetString();
					}
					catch (JsonException) {
						message = text.Length > 200 ? text[..200] : text;
					}
					ok = res.IsSuccessStatusCode;
					status = message;
				}
				catch (Exception e) {
					status = e.Message;
				}
				mainThread.Enqueue(() => {
					Publishing = false;
					PublishOk = ok;
					PublishStatus = status ?? "";
				});
			});
		}

		public static byte[] ThumbPng(Design d) {
			Color[] px = d.IsWearable ? d.Flatten(0, Design.BasePose) : d.Flatten(0, 0);
			int scale = Math.Max(1, Math.Min(3, 128 / Math.Max(d.Width, d.Height)));
			int w = d.Width * scale, h = d.Height * scale;
			var big = new Color[w * h];
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
					big[y * w + x] = px[y / scale * d.Width + x / scale];
			return SheetIO.PngBytes(big, w, h);
		}

		#endregion
	}
}
