using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    // Called by the panel on the Unity thread. No native data or asset access is
    // performed until Get is called in an existing run; task results are polled.
    public static class SkillIconResolver
    {
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Dictionary<string, Texture2D> Ready = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Fallbacks = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Task> Pending = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> RetryAfter = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Texture2D> Owned = new HashSet<Texture2D>();
        private static readonly Dictionary<Texture2D, Dictionary<string, Texture2D>> Circular =
            new Dictionary<Texture2D, Dictionary<string, Texture2D>>();
        private static float _nextNativeScan;

        public static Texture2D Get(string templateId, string tier, string name, IconCache cache)
        {
            Texture2D artwork = GetArtwork(templateId, tier, name, cache);
            if (artwork == null) return null;
            string frameTier = (tier ?? "Bronze").Trim().ToLowerInvariant();
            Dictionary<string, Texture2D> tiers;
            if (!Circular.TryGetValue(artwork, out tiers))
            {
                tiers = new Dictionary<string, Texture2D>();
                Circular[artwork] = tiers;
            }
            Texture2D icon;
            if (tiers.TryGetValue(frameTier, out icon) && icon != null) return icon;
            float radius = frameTier == "legendary" ? 0.37f :
                frameTier == "silver" || frameTier == "diamond" ? 0.39f : 0.45f;
            icon = CopyTexture(artwork, radius);
            if (icon == null) return artwork;
            icon.Apply(false, true);
            Owned.Add(icon);
            tiers[frameTier] = icon;
            return icon;
        }

        private static Texture2D GetArtwork(string templateId, string tier, string name, IconCache cache)
        {
            string key = (templateId ?? string.Empty).Trim().ToLowerInvariant();
            if (key.Length == 0) return Fallback("unknown:" + (name ?? string.Empty));
            Texture2D icon;
            if (Ready.TryGetValue(key, out icon) && icon != null) return icon;
            // Local artwork is borrowed from IconCache; Dispose must not destroy it.
            if (cache != null)
            {
                icon = cache.Get("skill-" + key);
                if (icon != null) { Ready[key] = icon; return icon; }
            }
            try
            {
                if (!HasRun()) return Fallback(key);
                // One scan for the whole panel, regardless of its skill count.
                if (Time.unscaledTime >= _nextNativeScan)
                {
                    _nextNativeScan = Time.unscaledTime + 0.5f;
                    ScanNative("SkillController", "CardData", cache);
                    ScanNative("TheBazaar.SkillProxyRenderer", "Card", cache);
                }
                if (Ready.TryGetValue(key, out icon) && icon != null) return icon;
                icon = CompletePending(key, cache);
                if (icon != null) return icon;
                float next;
                if (!Pending.ContainsKey(key) && Pending.Count < 4 &&
                    (!RetryAfter.TryGetValue(key, out next) || Time.unscaledTime >= next))
                {
                    RetryAfter[key] = Time.unscaledTime + 5f;
                    StartNativeLoad(key);
                    icon = CompletePending(key, cache);
                    if (icon != null) return icon;
                }
            }
            catch (Exception) { }
            return Fallback(key);
        }

        private static bool HasRun()
        {
            Type data = LiveSkillReader.FindType("TheBazaar.Data");
            if (data == null) return false;
            object run = LiveSkillReader.GetStatic(data, "Run");
            return Read(run, "Player") != null;
        }

        private static void ScanNative(string typeName, string cardMember, IconCache cache)
        {
            Type type = LiveSkillReader.FindType(typeName);
            if (type == null) return;
            foreach (UnityEngine.Object candidate in UnityEngine.Object.FindObjectsOfType(type))
            {
                try
                {
                    Component component = candidate as Component;
                    if (component == null || !component.gameObject.activeInHierarchy) continue;
                    object card = Read(component, cardMember);
                    string id = Text(Read(card, "TemplateId"));
                    if (string.IsNullOrEmpty(id)) continue;
                    Texture2D existing;
                    if (Ready.TryGetValue(id, out existing) && existing != null) continue;
                    Texture texture = Read(component, "CurrentSkillIconTexture") as Texture ??
                        Read(component, "_skillIconCurrentTexture") as Texture;
                    // A native default icon has no skill identity; keep our own
                    // distinguishable placeholder until the real artwork arrives.
                    if (texture == null || texture == Read(component, "_skillIconDefaultTexture") as Texture) continue;
                    Save(id, CopyTexture(texture), cache);
                }
                catch (Exception) { }
            }
        }

        private static void StartNativeLoad(string key)
        {
            Guid id;
            if (!Guid.TryParse(key, out id) || id == Guid.Empty) return;
            Type data = LiveSkillReader.FindType("TheBazaar.Data");
            MethodInfo created = data == null ? null : data.GetMethod("IsManagerCreated", StaticFlags);
            // Do not make GetStatic create or initialise the game database.
            if (created == null || !object.Equals(created.Invoke(null, null), true)) return;
            MethodInfo get = data.GetMethod("GetStatic", StaticFlags);
            object manager = get == null ? null : get.Invoke(null, null);
            Task dataTask = manager as Task;
            if (dataTask != null)
            {
                if (!dataTask.IsCompleted || dataTask.IsFaulted || dataTask.IsCanceled) return;
                manager = Read(dataTask, "Result");
            }
            MethodInfo lookup = manager == null ? null : manager.GetType().GetMethod("GetCardById",
                InstanceFlags, null, new Type[] { typeof(Guid) }, null);
            object template = lookup == null ? null : lookup.Invoke(manager, new object[] { id });
            // Use exactly the game's skill art address, never a generated URL.
            if (!string.Equals(Text(Read(template, "Type")), "Skill", StringComparison.OrdinalIgnoreCase)) return;
            string address = Text(Read(template, "ArtKey"));
            if (string.IsNullOrEmpty(address) || address == "Invalid") return;
            Type assetType = LiveSkillReader.FindType("AssetLoader");
            Type services = LiveSkillReader.FindType("TheBazaar.AppFramework.Services");
            object loader = GetService(services, assetType);
            if (loader == null) return;
            object scope = Read(loader, "CurrentScope");
            if (scope != null && !string.Equals(scope.ToString(), "InRun", StringComparison.Ordinal)) return;
            foreach (MethodInfo method in assetType.GetMethods(InstanceFlags))
            {
                if (method.Name != "LoadAssetAsyncByAddress" || !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 1 || parameters.Length > 2 || parameters[0].ParameterType != typeof(string)) continue;
                object[] arguments;
                if (parameters.Length == 1) arguments = new object[] { address };
                else if (parameters[1].ParameterType == typeof(bool)) arguments = new object[] { address, false };
                else if (Nullable.GetUnderlyingType(parameters[1].ParameterType) != null && parameters[1].IsOptional)
                    arguments = new object[] { address, null };
                else continue;
                Task task = method.MakeGenericMethod(typeof(Texture)).Invoke(loader, arguments) as Task;
                if (task != null) Pending[key] = task;
                return;
            }
        }

        private static object GetService(Type services, Type serviceType)
        {
            if (services == null || serviceType == null) return null;
            foreach (MethodInfo method in services.GetMethods(StaticFlags))
            {
                if (method.Name != "TryGet" || !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsByRef) continue;
                object[] arguments = new object[] { null };
                object result = method.MakeGenericMethod(serviceType).Invoke(null, arguments);
                object service = arguments[0] ?? result;
                return serviceType.IsInstanceOfType(service) ? service : null;
            }
            return null;
        }

        private static Texture2D CompletePending(string key, IconCache cache)
        {
            Task task;
            if (!Pending.TryGetValue(key, out task) || !task.IsCompleted) return null;
            Pending.Remove(key);
            if (task.IsFaulted) { object observed = task.Exception; return null; }
            if (task.IsCanceled) return null;
            return Save(key, CopyTexture(Read(task, "Result") as Texture), cache);
        }

        private static Texture2D Save(string key, Texture2D icon, IconCache cache)
        {
            if (icon == null) return null;
            Task redundant;
            if (Pending.TryGetValue(key, out redundant))
            {
                Pending.Remove(key);
                ObserveFailure(redundant);
            }
            Owned.Add(icon);
            Ready[key] = icon;
            if (cache != null)
            {
                try { cache.TryStore("skill-" + key, null, icon); }
                catch (Exception) { }
            }
            icon.Apply(false, true);
            return icon;
        }

        private static Texture2D CopyTexture(Texture source, float maskRadius = 0f)
        {
            if (source == null || source.width < 2 || source.height < 2) return null;
            float ratio = Mathf.Min(1f, 96f / Mathf.Max(source.width, source.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * ratio));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * ratio));
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.name = "ActionAxis_Skill_" + source.name;
                copy.filterMode = FilterMode.Bilinear;
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                if (maskRadius > 0)
                {
                    Color[] pixels = copy.GetPixels();
                    float cx = (width - 1) * 0.5f, cy = (height - 1) * 0.5f;
                    // Fill the native frame's opening and overlap its inner
                    // bevel slightly, without square art leaking past the rim.
                    float radius = Math.Min(width, height) * maskRadius;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            float dx = x - cx, dy = y - cy;
                            float alpha = Mathf.Clamp01(radius - (float)Math.Sqrt(dx * dx + dy * dy) + 0.5f);
                            pixels[y * width + x].a *= alpha;
                        }
                    copy.SetPixels(pixels);
                }
                copy.Apply(false, false);
                return copy;
            }
            catch (Exception)
            {
                if (copy != null) UnityEngine.Object.Destroy(copy);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Texture2D Fallback(string key)
        {
            Texture2D icon;
            if (Fallbacks.TryGetValue(key, out icon) && icon != null) return icon;
            uint hash = 2166136261;
            foreach (char value in key) hash = unchecked((hash ^ value) * 16777619);
            const int size = 48;
            Color[] pixels = new Color[size * size];
            int motif = (int)(hash % 6);
            Color ink = new Color(0.80f, 0.66f, 0.39f, 0.95f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f) / size * 2f - 1f, py = (y + 0.5f) / size * 2f - 1f;
                    float ax = Mathf.Abs(px), ay = Mathf.Abs(py), r2 = px * px + py * py;
                    bool mark = motif == 0 ? Mathf.Abs(ax + ay - 0.65f) < 0.09f :
                        motif == 1 ? (ax < 0.10f && ay < 0.62f || ay < 0.10f && ax < 0.62f) :
                        motif == 2 ? (Mathf.Abs(ax - ay) < 0.09f && ax < 0.47f) :
                        motif == 3 ? (r2 > 0.20f && r2 < 0.39f) :
                        motif == 4 ? (Mathf.Abs(ax + py * 0.55f - 0.30f) < 0.08f && ay < 0.56f) :
                        (Mathf.Abs(ax - 0.32f) < 0.08f && ay < 0.51f || Mathf.Abs(ay - 0.45f) < 0.08f && ax < 0.36f);
                    // Small satellite marks retain an identity even when two
                    // fallback icons happen to share the same central motif.
                    for (int bit = 0; bit < 4; bit++)
                    {
                        if ((hash & (1u << (bit + 8))) == 0) continue;
                        float dx = px - (bit % 2 == 0 ? -0.69f : 0.69f);
                        float dy = py - (bit < 2 ? -0.69f : 0.69f);
                        if (dx * dx + dy * dy < 0.009f) mark = true;
                    }
                    pixels[y * size + x] = mark ? ink : Color.clear;
                }
            icon = new Texture2D(size, size, TextureFormat.RGBA32, false);
            icon.name = "ActionAxis_SkillPlaceholder_" + hash.ToString("x8");
            icon.filterMode = FilterMode.Bilinear;
            icon.wrapMode = TextureWrapMode.Clamp;
            icon.SetPixels(pixels);
            icon.Apply(false, true);
            Fallbacks[key] = icon;
            Owned.Add(icon);
            return icon;
        }

        public static void Dispose()
        {
            foreach (Texture2D icon in Owned) if (icon != null) UnityEngine.Object.Destroy(icon);
            foreach (Task task in Pending.Values) ObserveFailure(task);
            Owned.Clear(); Ready.Clear(); Fallbacks.Clear(); Pending.Clear(); RetryAfter.Clear();
            Circular.Clear();
            _nextNativeScan = 0f;
        }

        private static void ObserveFailure(Task task)
        {
            task.ContinueWith(delegate(Task completed) { object observed = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static object Read(object owner, string member)
        {
            try { return owner == null ? null : LiveSkillReader.GetMember(owner, member); }
            catch (Exception) { return null; }
        }
        private static string Text(object value) { return value == null ? null : value.ToString(); }
    }
}
