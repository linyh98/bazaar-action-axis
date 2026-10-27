using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    // Unity thread only. Copy the exact native portrait sprite, including its atlas
    // region, instead of taking screen pixels or showing the last opponent's art.
    public static class PortraitResolver
    {
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly Dictionary<string, Texture2D> Ready = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> Fallbacks = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Task> Pending = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, float> RetryAfter = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public static Texture2D GetPlayer(string hero)
        {
            string key = "hero:" + (hero ?? "player");
            Texture2D cached;
            if (Ready.TryGetValue(key, out cached) && cached != null) return cached;
            if (CanRetry(key))
            {
                Texture2D portrait = FindNativePlayer();
                if (portrait == null) portrait = PollProvider(key,
                    "BazaarPlusPlus.GameInterop.HeroPortraits.HeroPortraitSpriteProvider",
                    "LoadDefaultPortraitAsync", hero);
                if (portrait != null) { Ready[key] = portrait; return portrait; }
            }
            return Fallback(key, false);
        }

        public static Texture2D GetMonster(string monsterId, string name)
        {
            return GetMonster(monsterId, name, null);
        }

        public static Texture2D GetMonster(string monsterId, string name, string encounterInstanceId)
        {
            string key = "monster:" + (monsterId ?? name ?? "unknown");
            Texture2D cached;
            if (Ready.TryGetValue(key, out cached) && cached != null) return cached;
            if (CanRetry(key))
            {
                string encounterTemplateId;
                Texture2D portrait = FindNativeMonster(monsterId, encounterInstanceId, out encounterTemplateId);
                if (portrait == null && string.IsNullOrEmpty(encounterTemplateId))
                    encounterTemplateId = VerifiedEncounterTemplate(monsterId, encounterInstanceId);
                if (portrait == null && !string.IsNullOrEmpty(encounterTemplateId))
                    portrait = PollProvider(key,
                        "BazaarPlusPlus.GameInterop.EncounterPortraits.EncounterPortraitSpriteProvider",
                        "LoadPortraitAsync", encounterTemplateId);
                // A previous asynchronous request may finish after the native
                // encounter has been hidden by the game's own presentation.
                if (portrait == null) portrait = CompletePending(key);
                if (portrait != null) { Ready[key] = portrait; return portrait; }
            }
            return Fallback(key, true);
        }

        private static bool CanRetry(string key)
        {
            float next;
            if (RetryAfter.TryGetValue(key, out next) && Time.unscaledTime < next) return false;
            RetryAfter[key] = Time.unscaledTime + 0.5f;
            return true;
        }

        private static Texture2D FindNativePlayer()
        {
            Type type = LiveSkillReader.FindType("EncounterController");
            if (type == null) return null;
            foreach (UnityEngine.Object candidate in UnityEngine.Object.FindObjectsOfType(type))
            {
                Component controller = candidate as Component;
                if (controller == null || !controller.gameObject.activeInHierarchy ||
                    !True(Read(controller, "IsHeroPortrait")) || !True(Read(controller, "IsPlayerBoard"))) continue;
                Texture2D portrait = CopyControllerPortrait(controller);
                if (portrait != null) return portrait;
            }
            return null;
        }

        private static Texture2D FindNativeMonster(string monsterId, string encounterInstanceId, out string encounterTemplateId)
        {
            encounterTemplateId = null;
            if (string.IsNullOrEmpty(monsterId)) return null;
            Type type = LiveSkillReader.FindType("EncounterController");
            if (type == null) return null;
            foreach (UnityEngine.Object candidate in UnityEngine.Object.FindObjectsOfType(type))
            {
                Component controller = candidate as Component;
                if (controller == null || !controller.gameObject.activeInHierarchy) continue;
                object card = Read(controller, "CardData");
                string id = Text(Read(Read(Read(card, "Template"), "CombatantType"), "MonsterTemplateId"));
                if (!string.Equals(id, monsterId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(encounterInstanceId))
                {
                    object instance = Read(card, "InstanceId");
                    string instanceId = Text(Read(instance, "Value")) ?? Text(instance);
                    // CurrentEncounterId can be an entity instance or the actual
                    // encounter template GUID, depending on the native run state.
                    if (!string.Equals(instanceId, encounterInstanceId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(Text(Read(card, "TemplateId")), encounterInstanceId, StringComparison.OrdinalIgnoreCase)) continue;
                }
                encounterTemplateId = Text(Read(card, "TemplateId"));
                Texture2D portrait = CopyControllerPortrait(controller);
                if (portrait != null) return portrait;
            }
            return null;
        }

        private static string VerifiedEncounterTemplate(string monsterId, string encounterIdentity)
        {
            Guid encounterId;
            if (string.IsNullOrEmpty(monsterId) || !Guid.TryParse(encounterIdentity, out encounterId)) return null;
            try
            {
                Type dataType = LiveSkillReader.FindType("TheBazaar.Data");
                MethodInfo get = dataType == null ? null : dataType.GetMethod("GetStatic", StaticFlags);
                object manager = get == null ? null : get.Invoke(null, null);
                Task task = manager as Task;
                if (task != null)
                {
                    if (!task.IsCompleted || task.IsFaulted || task.IsCanceled) return null;
                    manager = Read(task, "Result");
                }
                MethodInfo lookup = manager == null ? null : manager.GetType().GetMethod("GetCardById",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new Type[] { typeof(Guid) }, null);
                object template = lookup == null ? null : lookup.Invoke(manager, new object[] { encounterId });
                // A GUID alone is insufficient: never send a monster template or
                // an old unrelated encounter to BPP's encounter-art provider.
                string mappedMonster = Text(Read(Read(template, "CombatantType"), "MonsterTemplateId"));
                return string.Equals(mappedMonster, monsterId, StringComparison.OrdinalIgnoreCase)
                    ? encounterId.ToString() : null;
            }
            catch (Exception) { return null; }
        }

        private static Texture2D CopyControllerPortrait(Component controller)
        {
            try
            {
                // CurrentEncounterDataObject also serves animated native portraits
                // whose visible renderer no longer holds a static sprite.
                object data = Read(controller, "CurrentEncounterDataObject");
                Sprite sprite = Read(data, "portraitTextureReference") as Sprite;
                if (sprite == null)
                {
                    Component renderer = Read(controller, "portrait") as Component;
                    sprite = Read(renderer, "sprite") as Sprite;
                }
                return CopySprite(sprite);
            }
            catch (Exception) { return null; }
        }

        private static Texture2D PollProvider(string key, string providerName, string methodName, string identity)
        {
            if (Pending.ContainsKey(key)) return CompletePending(key);
            if (string.IsNullOrEmpty(identity)) return null;
            try
            {
                Type type = LiveSkillReader.FindType(providerName);
                MethodInfo method = type == null ? null : type.GetMethod(methodName, StaticFlags);
                if (method == null) return null;
                Type argumentType = method.GetParameters()[0].ParameterType;
                object argument;
                if (argumentType == typeof(Guid))
                {
                    Guid guid;
                    if (!Guid.TryParse(identity, out guid)) return null;
                    argument = guid;
                }
                else if (argumentType.IsEnum) argument = Enum.Parse(argumentType, identity, true);
                else return null;
                Task task = method.Invoke(null, new object[] { argument }) as Task;
                if (task != null) Pending[key] = task;
                return CompletePending(key);
            }
            catch (Exception) { return null; }
        }

        private static Texture2D CompletePending(string key)
        {
            Task task;
            if (!Pending.TryGetValue(key, out task) || !task.IsCompleted) return null;
            Pending.Remove(key);
            if (task.IsFaulted) { object observed = task.Exception; return null; }
            if (task.IsCanceled) return null;
            try { return CopySprite(Read(Read(task, "Result"), "Sprite") as Sprite); }
            catch (Exception) { return null; }
        }

        private static Texture2D CopySprite(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            Texture2D source = sprite.texture;
            Rect area = sprite.textureRect;
            if (area.width <= 1 || area.height <= 1) return null;
            const int extent = 192;
            float ratio = Mathf.Min(1f, extent / Mathf.Max(area.width, area.height));
            int width = Mathf.Max(1, Mathf.RoundToInt(area.width * ratio));
            int height = Mathf.Max(1, Mathf.RoundToInt(area.height * ratio));
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D copy = null;
            try
            {
                // GPU copy works for native textures imported with Read/Write off.
                Graphics.Blit(source, target, new Vector2(area.width / source.width, area.height / source.height),
                    new Vector2(area.x / source.width, area.y / source.height));
                RenderTexture.active = target;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.name = "ActionAxis_Portrait_" + sprite.name;
                copy.filterMode = FilterMode.Bilinear;
                copy.wrapMode = TextureWrapMode.Clamp;
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply(false, true);
                return copy;
            }
            catch
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

        private static Texture2D Fallback(string key, bool monster)
        {
            Texture2D result;
            if (Fallbacks.TryGetValue(key, out result) && result != null) return result;
            const int size = 96;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + 0.5f) / size * 2f - 1f, py = (y + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(px * px + py * py);
                    Color color = radius < 0.92f ? new Color(0.07f, 0.065f, 0.05f, 1f) : Color.clear;
                    bool head = px * px / 0.10f + (py - 0.20f) * (py - 0.20f) / 0.14f < 1f;
                    bool shoulders = px * px / 0.39f + (py + 0.57f) * (py + 0.57f) / 0.20f < 1f;
                    bool horns = monster && py > 0.30f && py < 0.70f &&
                        Mathf.Abs(px) > 0.20f + (py - 0.3f) * 0.3f && Mathf.Abs(px) < 0.50f - (py - 0.3f) * 0.15f;
                    if (head || shoulders || horns) color = new Color(0.57f, 0.46f, 0.27f, 1f);
                    if (monster && Mathf.Abs(py - 0.24f) < 0.025f && Mathf.Abs(px) > 0.07f && Mathf.Abs(px) < 0.21f)
                        color = new Color(0.99f, 0.80f, 0.39f, 1f);
                    if (radius > 0.86f && radius < 0.90f) color = new Color(0.47f, 0.38f, 0.22f, 1f);
                    pixels[y * size + x] = color;
                }
            result = new Texture2D(size, size, TextureFormat.RGBA32, false);
            result.name = "ActionAxis_PortraitFallback_" + key;
            result.filterMode = FilterMode.Bilinear;
            result.SetPixels(pixels);
            result.Apply(false, true);
            Fallbacks[key] = result;
            return result;
        }

        public static void Dispose()
        {
            foreach (Texture2D texture in Ready.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            foreach (Texture2D texture in Fallbacks.Values) if (texture != null) UnityEngine.Object.Destroy(texture);
            Ready.Clear(); Fallbacks.Clear(); Pending.Clear(); RetryAfter.Clear();
        }

        private static object Read(object owner, string name)
        {
            try { return owner == null ? null : LiveSkillReader.GetMember(owner, name); }
            catch (Exception) { return null; }
        }
        private static bool True(object value) { return value is bool && (bool)value; }
        private static string Text(object value) { return value == null ? null : value.ToString(); }
    }
}
