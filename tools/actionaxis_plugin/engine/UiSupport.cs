using System;
using System.IO;
using System.Text;

namespace BazaarActionAxis.Engine
{
    public static class IconCachePaths
    {
        public static string PathFor(string dir, string baseItemId)
        {
            return PathFor(dir, baseItemId, null);
        }

        public static string NormalizeEnchantment(string enchantment)
        {
            string value = (enchantment ?? string.Empty).Trim().ToLowerInvariant();
            return value == "none" || value == "unenchanted"
                ? string.Empty : value;
        }

        public static string KeyFor(string baseItemId, string enchantment)
        {
            return (baseItemId ?? string.Empty).ToLowerInvariant() + "|" +
                NormalizeEnchantment(enchantment);
        }

        public static string PathFor(string dir, string baseItemId, string enchantment)
        {
            string name = string.IsNullOrEmpty(baseItemId) ? "_unknown" : baseItemId;
            string variant = NormalizeEnchantment(enchantment);
            if (variant.Length > 0) name += "--enchant-" + VariantName(variant);
            return Path.Combine(dir, SafeName(name) + ".png").Replace('\\', '/');
        }

        private static string SafeName(string name)
        {
            StringBuilder safe = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') || c == '-')
                    safe.Append(c);
                else
                    safe.Append('_');
            }
            return safe.ToString();
        }

        private static string VariantName(string variant)
        {
            string safe = SafeName(variant);
            if (safe == variant) return safe;
            // Future enchantment names must not collide after filename sanitization.
            byte[] bytes = Encoding.UTF8.GetBytes(variant);
            StringBuilder encoded = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++) encoded.Append(bytes[i].ToString("x2"));
            return safe + "-" + encoded;
        }
    }

    public static class IconCacheFiles
    {
        public static bool TryStore(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || File.Exists(path)) return false;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try
                {
                    // Move is atomic and refuses to replace an existing artist asset,
                    // including a file created after the first existence check.
                    File.Move(temporary, path);
                    return true;
                }
                catch (IOException)
                {
                    if (File.Exists(path)) return false;
                    throw;
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public static class ThemeKeys
    {
        public static string TierColorKey(string tier)
        {
            if (tier == "Silver") return "silver";
            if (tier == "Gold") return "gold";
            if (tier == "Diamond") return "diamond";
            if (tier == "Legendary") return "legendary";
            return "bronze";
        }
    }

    public static class PathConfig
    {
        public static string ResolveDataDir(string pluginPath, string configured)
        {
            string result;
            if (string.IsNullOrEmpty(configured))
                result = Path.Combine(pluginPath, "BazaarActionAxis");
            else if (Path.IsPathRooted(configured))
                result = configured;
            else
                result = Path.Combine(pluginPath, configured);
            return result.Replace('\\', '/');
        }
    }
}
