using System;
using System.Collections.Generic;
using System.Linq;
using KERNEL.Ledger;
using KSP.Game;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace KERNEL.UI
{
    /// <summary>
    /// Pixel art for bodies on the system map. Stock bodies use the art shipped with the mod; any other body gets
    /// art made the same way from its own scaled-space texture: a sphere lit from its star's side, brightened and
    /// reduced to five colours. Stars without art get a pixel sun. Rings are drawn from each body's ring data, as
    /// a back and a front half so the body sits between them.
    ///
    /// Reading a body's surface means loading its planet model, so that's done during save loading for every
    /// registered body without shipped art (see <see cref="Preload"/>), and otherwise one body per frame, so
    /// opening the map never stalls the game.
    /// </summary>
    internal static class BodyArt
    {
        /// <summary>
        /// Map (canvas) units per art pixel.
        /// </summary>
        public const float Unit = 2f;

        /// <summary>
        /// How far rings are tipped towards the viewer: their height as a fraction of their width.
        /// </summary>
        public const float RingTilt = 0.32f;

        // Stars' art includes a glow around the disc, this much wider than the disc
        private const float GlowFraction = 0.08f;

        private const string ShippedAddress = "KERNEL/Bodies/{0}.png";
        private const string ScaledPrefabAddress = "Celestial.{0}.Scaled.prefab";
        private const string SurfaceTextureProperty = "_MainTex";

        // Light from the left, where the star sits on the map
        private static readonly Vector3 Light = new Vector3(-0.62f, 0.35f, 0.70f).normalized;
        private const float Rotation = 0.25f;

        private static readonly Color32[] SunPalette =
        {
            new(255, 250, 214, 255), new(255, 232, 150, 255), new(255, 204, 92, 255), new(246, 160, 58, 255), new(214, 112, 40, 255)
        };

        private static readonly Color32[] RingPalette = { new(222, 200, 158, 255), new(236, 220, 186, 255), new(196, 170, 128, 255) };

        private static readonly Dictionary<string, Sprite> Cache = new();
        private static readonly Dictionary<string, List<Action<Sprite>>> Pending = new();

        // Each texture body's surface, small and readable, so test or modded bodies sharing one read it once
        private const int SurfaceWidth = 256, SurfaceHeight = 128;
        private static readonly Dictionary<string, Color32[]> Surfaces = new();
        private static readonly Dictionary<string, List<Action<Color32[]>>> SurfaceWaiters = new();

        // Slow work (loading a model, building a sprite) runs one job per frame
        private static readonly Queue<Action<Action>> Jobs = new();
        private static bool _busy;
        private static JobRunner _runner;

        /// <summary>
        /// How much wider than its disc a star's art is, for its glow.
        /// </summary>
        public static float StarArtScale(int discPixels) => (discPixels + 2f * Glow(discPixels)) / discPixels;

        private static int Glow(int discPixels) => Mathf.Max(2, Mathf.RoundToInt(discPixels * GlowFraction));

        public static int Pixels(float size) => Mathf.Max(6, Mathf.RoundToInt(size / Unit));

        /// <summary>
        /// Calls back with the body's art for a disc of the given size, or null if there's none to be had.
        /// </summary>
        public static void Request(BodyEntry body, float size, Action<Sprite> done)
        {
            int pixels = Pixels(size);
            string key = $"{body.BodyName}@{pixels}";
            if (Cache.TryGetValue(key, out Sprite cached))
            {
                done(cached);
                return;
            }

            if (Pending.TryGetValue(key, out List<Action<Sprite>> waiting))
            {
                waiting.Add(done);
                return;
            }

            Pending[key] = new List<Action<Sprite>> { done };
            void Finish(Sprite sprite)
            {
                Cache[key] = sprite;
                if (!Pending.TryGetValue(key, out List<Action<Sprite>> callbacks)) return;
                Pending.Remove(key);
                foreach (Action<Sprite> callback in callbacks) callback(sprite);
            }

            // Everything here is asynchronous: requests can come from inside other Addressables callbacks (e.g. a
            // ledger rebuild after the galaxy loads), where waiting on an operation isn't allowed
            string shipped = string.Format(ShippedAddress, body.BodyName);
            Exists(shipped, hasShipped =>
            {
                if (hasShipped)
                {
                    GameManager.Instance.Assets.Load<Texture2D>(shipped, texture => Finish(texture != null ? ToSprite(texture) : null));
                    return;
                }

                if (body.IsStar)
                {
                    Enqueue(jobDone => Run(jobDone, () => Finish(ToSprite(Sun(pixels, body.BodyName)))));
                    return;
                }

                GetSurface(body.BodyName, surface =>
                {
                    if (surface == null)
                    {
                        Finish(null);
                        return;
                    }

                    Enqueue(jobDone => Run(jobDone, () => Finish(ToSprite(Sphere(surface, pixels)))));
                });
            });
        }

        /// <summary>
        /// Reads the surface of every registered body without shipped art, calling back when they're all ready.
        /// Run as a save-loading step so the map doesn't have to.
        /// </summary>
        public static void Preload(Action done)
        {
            Dictionary<string, KSP.Sim.Definitions.CelestialBodyCore> bodies = GameManager.Instance?.Game?.CelestialBodies?.GetAllBodiesData();
            if (bodies == null || bodies.Count == 0)
            {
                done();
                return;
            }

            int remaining = bodies.Count;
            void OneDone()
            {
                if (--remaining == 0) done();
            }

            foreach (KeyValuePair<string, KSP.Sim.Definitions.CelestialBodyCore> body in bodies)
            {
                string name = body.Key;
                if (body.Value?.data == null || GameManager.Instance.Game.CelestialBodies.GetStarStatus(name))
                {
                    OneDone();
                    continue;
                }

                Exists(string.Format(ShippedAddress, name), hasShipped =>
                {
                    if (hasShipped) OneDone();
                    else GetSurface(name, _ => OneDone());
                });
            }
        }

        // The surface map of a body's scaled-space texture, read once and kept
        private static void GetSurface(string textureBody, Action<Color32[]> done)
        {
            if (Surfaces.TryGetValue(textureBody, out Color32[] cached))
            {
                done(cached);
                return;
            }

            if (SurfaceWaiters.TryGetValue(textureBody, out List<Action<Color32[]>> waiting))
            {
                waiting.Add(done);
                return;
            }

            SurfaceWaiters[textureBody] = new List<Action<Color32[]>> { done };
            void Finish(Color32[] surface)
            {
                Surfaces[textureBody] = surface;
                List<Action<Color32[]>> callbacks = SurfaceWaiters[textureBody];
                SurfaceWaiters.Remove(textureBody);
                foreach (Action<Color32[]> callback in callbacks) callback(surface);
            }

            string address = string.Format(ScaledPrefabAddress, textureBody);
            Exists(address, hasPrefab =>
            {
                if (!hasPrefab)
                {
                    Finish(null);
                    return;
                }

                Enqueue(jobDone => GameManager.Instance.Assets.Load<GameObject>(address, prefab =>
                {
                    Color32[] surface = null;
                    try
                    {
                        Texture texture = SurfaceTexture(prefab);
                        if (texture != null) surface = ReadBack(texture, SurfaceWidth, SurfaceHeight);
                    }
                    finally
                    {
                        if (prefab != null) GameManager.Instance.Assets.ReleaseAsset(prefab);
                        Finish(surface);
                        jobDone();
                    }
                }));
            });
        }

        private static void Run(Action jobDone, Action work)
        {
            try
            {
                work();
            }
            finally
            {
                jobDone();
            }
        }

        private static void Enqueue(Action<Action> job)
        {
            Jobs.Enqueue(job);
            if (_runner != null) return;

            var host = new GameObject("KERNEL Body Art");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            _runner = host.AddComponent<JobRunner>();
        }

        // Starts the next job each frame once the last one has finished
        private class JobRunner : MonoBehaviour
        {
            private void Update()
            {
                if (_busy || Jobs.Count == 0) return;

                _busy = true;
                Action<Action> job = Jobs.Dequeue();
                try
                {
                    job(() => _busy = false);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _busy = false;
                }
            }
        }

        /// <summary>
        /// The back and front halves of a body's rings for a disc of the given size, both centred on the body, and
        /// their size on the map; null if it has none.
        /// </summary>
        public static (Sprite Back, Sprite Front, Vector2 Size)? Rings(BodyEntry body, float size)
        {
            if (body.Rings.Count == 0 || body.Radius <= 0) return null;

            int pixels = Pixels(size);
            string key = $"{body.BodyName}@{pixels}";
            if (Cache.TryGetValue(key + "/rings-back", out Sprite back) && Cache.TryGetValue(key + "/rings-front", out Sprite front))
                return (back, front, new Vector2(back.texture.width, back.texture.height) * Unit);

            float radius = pixels / 2f;
            List<(float Inner, float Outer)> bands = body.Rings
                .Select(ring => ((float)(ring.Inner / body.Radius), (float)(ring.Outer / body.Radius))).ToList();
            float reach = bands.Max(b => b.Outer);
            int width = Even(Mathf.CeilToInt(2f * radius * reach) + 4, pixels);
            int height = Even(Mathf.Max(pixels, Mathf.CeilToInt(2f * radius * reach * RingTilt)) + 4, pixels);

            var backTexture = NewTexture(width, height);
            var frontTexture = NewTexture(width, height);
            var clear = new Color32[width * height];
            var backPixels = (Color32[])clear.Clone();
            var frontPixels = (Color32[])clear.Clone();
            float cx = width / 2f, cy = height / 2f;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int band = RingBand(x, y, cx, cy, radius, bands);
                if (band < 0) continue;
                // Texture rows run bottom up: the lower half passes in front of the body
                Color32 colour = RingPalette[band % RingPalette.Length];
                if (y + 0.5f < cy) frontPixels[y * width + x] = colour;
                else backPixels[y * width + x] = colour;
            }

            backTexture.SetPixels32(backPixels);
            backTexture.Apply();
            frontTexture.SetPixels32(frontPixels);
            frontTexture.Apply();
            back = ToSprite(backTexture);
            front = ToSprite(frontTexture);
            Cache[key + "/rings-back"] = back;
            Cache[key + "/rings-front"] = front;
            return (back, front, new Vector2(width, height) * Unit);
        }

        /// <summary>
        /// How far a body's rings reach, as a multiple of its radius (1 without rings).
        /// </summary>
        public static float RingReach(BodyEntry body) =>
            body.Rings.Count == 0 || body.Radius <= 0 ? 1f : Mathf.Max(1f, (float)(body.Rings.Max(r => r.Outer) / body.Radius));

        // Which ring band a pixel touches, if any; any part of it counts, so thin rings stay one solid pixel wide
        private static int RingBand(int x, int y, float cx, float cy, float radius, List<(float Inner, float Outer)> bands)
        {
            for (int i = 0; i < bands.Count; i++)
            for (int sy = 0; sy < 3; sy++)
            for (int sx = 0; sx < 3; sx++)
            {
                float dx = x + 0.1f + 0.4f * sx - cx;
                float dy = (y + 0.1f + 0.4f * sy - cy) / RingTilt;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                if (d >= bands[i].Inner && d <= bands[i].Outer) return i;
            }

            return -1;
        }

        // Keeps a canvas the same odd/even as the disc, so the disc sits on whole pixels in its middle
        private static int Even(int size, int disc) => (size - disc) % 2 == 0 ? size : size + 1;

        // Whether anything is registered at an address (without loading it or logging a missing key)
        private static void Exists(string address, Action<bool> answer)
        {
            var locations = Addressables.LoadResourceLocationsAsync(address);
            locations.Completed += handle =>
            {
                bool found = handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                             && handle.Result != null && handle.Result.Count > 0;
                Addressables.Release(handle);
                answer(found);
            };
        }

        private static Texture SurfaceTexture(GameObject prefab)
        {
            if (prefab == null) return null;
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null && material.HasProperty(SurfaceTextureProperty) && material.GetTexture(SurfaceTextureProperty) is Texture2D texture)
                    return texture;
            return null;
        }

        // A lit sphere from an equirectangular surface map, brightened and reduced to five colours
        private static Texture2D Sphere(Color32[] map, int pixels)
        {
            const int mapWidth = SurfaceWidth, mapHeight = SurfaceHeight;
            int span = Mathf.Max(1, (int)(mapWidth / (pixels * 2.2f)));

            var lit = new Color32?[pixels * pixels];
            for (int row = 0; row < pixels; row++)
            for (int x = 0; x < pixels; x++)
            {
                float nx = (x + 0.5f) / pixels * 2f - 1f, ny = 1f - (row + 0.5f) / pixels * 2f;
                float d = nx * nx + ny * ny;
                if (d > 1f) continue;

                float nz = Mathf.Sqrt(1f - d);
                float lat = Mathf.Asin(ny), lon = Mathf.Atan2(nx, nz) + Rotation * 2f * Mathf.PI;
                float u = Mathf.Repeat(lon / (2f * Mathf.PI), 1f), v = 0.5f - lat / Mathf.PI;

                // A little of the texture around the point, so small spheres pick up its overall colour
                int cx = (int)(u * mapWidth), cy = Mathf.Min(mapHeight - 1, (int)(v * mapHeight));
                float r = 0, g = 0, b = 0;
                int n = 0;
                for (int oy = -span; oy <= span; oy += span)
                for (int ox = -span; ox <= span; ox += span)
                {
                    int sy = Mathf.Clamp(cy + oy, 0, mapHeight - 1);
                    int sx = ((cx + ox) % mapWidth + mapWidth) % mapWidth;
                    // Read-back rows run bottom up; the map's v runs top down
                    Color32 c = map[(mapHeight - 1 - sy) * mapWidth + sx];
                    r += c.r; g += c.g; b += c.b;
                    n++;
                }

                Color colour = Brighten(new Color(r / n / 255f, g / n / 255f, b / n / 255f));
                float shade = 0.72f + 0.33f * Mathf.Max(0f, Vector3.Dot(new Vector3(nx, ny, nz), Light));
                lit[row * pixels + x] = (Color32)(colour * shade);
            }

            Color32[] palette = FivePalette(lit.Where(c => c.HasValue).Select(c => c.Value).ToList());
            var texture = NewTexture(pixels, pixels);
            var pixelData = new Color32[pixels * pixels];
            for (int row = 0; row < pixels; row++)
            for (int x = 0; x < pixels; x++)
            {
                Color32? c = lit[row * pixels + x];
                if (c == null) continue;
                pixelData[(pixels - 1 - row) * pixels + x] = Nearest(palette, c.Value);
            }

            texture.SetPixels32(pixelData);
            texture.Apply();
            return texture;
        }

        // A pale core warming to an orange edge, with a stepped glow around it
        private static Texture2D Sun(int pixels, string name)
        {
            int glow = Glow(pixels);
            int size = pixels + glow * 2;
            var random = new System.Random(name.GetHashCode());
            var data = new Color32[size * size];
            float c = size / 2f, r = pixels / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                if (d <= r)
                {
                    float t = d / r + ((float)random.NextDouble() - 0.5f) * 0.18f;
                    int index = t < 0.35f ? 0 : t < 0.62f ? 1 : t < 0.84f ? 2 : t < 0.95f ? 3 : 4;
                    data[y * size + x] = SunPalette[Mathf.Clamp(index, 0, 4)];
                }
                else if (d <= r + glow)
                {
                    float f = 1f - (d - r) / glow;
                    Color32 edge = SunPalette[3];
                    edge.a = (byte)(255 * (f > 0.66f ? 0.55f : f > 0.33f ? 0.3f : 0.12f));
                    data[y * size + x] = edge;
                }
            }

            var texture = NewTexture(size, size);
            texture.SetPixels32(data);
            texture.Apply();
            return texture;
        }

        private static Color32[] ReadBack(Texture source, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, target);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Color32[] pixels = copy.GetPixels32();
            Object.Destroy(copy);
            return pixels;
        }

        // More saturated and lighter, for the map's bright pixel style
        private static Color Brighten(Color colour)
        {
            RgbToHls(colour, out float h, out float l, out float s);
            l = Mathf.Min(0.92f, l * 1.25f + 0.06f);
            s = Mathf.Min(1f, s * 1.45f + 0.05f);
            return HlsToRgb(h, l, s);
        }

        // Five colours that best cover the sphere's (k-means, seeded from darkest to lightest)
        private static Color32[] FivePalette(List<Color32> colours)
        {
            const int k = 5;
            if (colours.Count == 0) return new[] { new Color32(128, 128, 128, 255) };

            List<Color32> sorted = colours.OrderBy(c => c.r + c.g + c.b).ToList();
            var centres = new Vector3[k];
            for (int i = 0; i < k; i++)
            {
                Color32 seed = sorted[i * (sorted.Count - 1) / (k - 1)];
                centres[i] = new Vector3(seed.r, seed.g, seed.b);
            }

            for (int iteration = 0; iteration < 12; iteration++)
            {
                var sums = new Vector3[k];
                var counts = new int[k];
                foreach (Color32 c in colours)
                {
                    var p = new Vector3(c.r, c.g, c.b);
                    int best = 0;
                    for (int i = 1; i < k; i++)
                        if ((p - centres[i]).sqrMagnitude < (p - centres[best]).sqrMagnitude) best = i;
                    sums[best] += p;
                    counts[best]++;
                }

                for (int i = 0; i < k; i++)
                    if (counts[i] > 0) centres[i] = sums[i] / counts[i];
            }

            return centres.Select(c => new Color32((byte)c.x, (byte)c.y, (byte)c.z, 255)).ToArray();
        }

        private static Color32 Nearest(Color32[] palette, Color32 colour)
        {
            Color32 best = palette[0];
            int bestDistance = int.MaxValue;
            foreach (Color32 p in palette)
            {
                int dr = p.r - colour.r, dg = p.g - colour.g, db = p.b - colour.b;
                int distance = dr * dr + dg * dg + db * db;
                if (distance >= bestDistance) continue;
                best = p;
                bestDistance = distance;
            }

            return best;
        }

        private static void RgbToHls(Color c, out float h, out float l, out float s)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            l = (max + min) / 2f;
            if (Mathf.Approximately(max, min))
            {
                h = s = 0f;
                return;
            }

            float d = max - min;
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            if (Mathf.Approximately(max, c.r)) h = (c.g - c.b) / d + (c.g < c.b ? 6f : 0f);
            else if (Mathf.Approximately(max, c.g)) h = (c.b - c.r) / d + 2f;
            else h = (c.r - c.g) / d + 4f;
            h /= 6f;
        }

        private static Color HlsToRgb(float h, float l, float s)
        {
            if (s <= 0f) return new Color(l, l, l);
            float q = l < 0.5f ? l * (1f + s) : l + s - l * s, p = 2f * l - q;
            return new Color(Hue(p, q, h + 1f / 3f), Hue(p, q, h), Hue(p, q, h - 1f / 3f));
        }

        private static float Hue(float p, float q, float t)
        {
            t = Mathf.Repeat(t, 1f);
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }

        private static Texture2D NewTexture(int width, int height) =>
            new(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        private static Sprite ToSprite(Texture2D texture)
        {
            texture.filterMode = FilterMode.Point;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
    }
}
