using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BazaarActionAxis.Engine;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    public sealed class IconCache
    {
        private readonly string _dir;
        private readonly Dictionary<string, Texture2D> _textures =
            new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _missing = new HashSet<string>();
        private readonly Dictionary<string, byte[]> _invalid = new Dictionary<string, byte[]>();

        public IconCache(string dir)
        {
            _dir = dir;
        }

        public Texture2D Get(string baseItemId)
        {
            return Get(baseItemId, null);
        }

        public Texture2D Get(string baseItemId, string enchantment)
        {
            if (string.IsNullOrEmpty(baseItemId)) return null;
            string key = IconCachePaths.KeyFor(baseItemId, enchantment);
            Texture2D cached;
            if (_textures.TryGetValue(key, out cached)) return cached;
            if (_missing.Contains(key)) return null;

            string path = IconCachePaths.PathFor(_dir, baseItemId, enchantment);
            if (!File.Exists(path))
            {
                _missing.Add(key);
                return null;
            }

            Texture2D texture = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!LoadImage(texture, bytes))
                {
                    _invalid[key] = bytes;
                    _missing.Add(key);
                    return null;
                }
                _textures[key] = texture;
                _invalid.Remove(key);
                Texture2D ready = texture;
                texture = null;
                return ready;
            }
            catch
            {
                _missing.Add(key);
                return null;
            }
            finally
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
        }

        // Opening/recording again retries absent art, including files installed meanwhile.
        public void RefreshMissing()
        {
            _missing.Clear();
        }

        public bool Contains(string baseItemId, string enchantment)
        {
            return Get(baseItemId, enchantment) != null;
        }

        public bool TryStore(string baseItemId, string enchantment, Texture2D artwork)
        {
            if (string.IsNullOrEmpty(baseItemId) || artwork == null) return false;
            string key = IconCachePaths.KeyFor(baseItemId, enchantment);
            string path = IconCachePaths.PathFor(_dir, baseItemId, enchantment);
            if (File.Exists(path))
            {
                byte[] invalid;
                if (!_invalid.TryGetValue(key, out invalid) ||
                    !SameBytes(invalid, File.ReadAllBytes(path))) return false;
                // Preserve the exact broken file, but allow a fresh capture to repair
                // coverage. A file replaced by the artist since loading is untouched.
                File.Move(path, path + ".invalid-" + Guid.NewGuid().ToString("N"));
                _invalid.Remove(key);
            }
            byte[] bytes = EncodePng(artwork);
            if (bytes == null || bytes.Length == 0) return false;
            if (!IconCacheFiles.TryStore(path, bytes)) return false;
            _missing.Remove(key);
            return true;
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private static byte[] EncodePng(Texture2D texture)
        {
            MethodInfo method = typeof(Texture2D).GetMethod("EncodeToPNG", Type.EmptyTypes);
            if (method != null) return method.Invoke(texture, null) as byte[];
            Type conversion = FindImageConversion();
            method = conversion == null ? null : conversion.GetMethod(
                "EncodeToPNG", new Type[] { typeof(Texture2D) });
            return method == null ? null : method.Invoke(null, new object[] { texture }) as byte[];
        }

        private static Type FindImageConversion()
        {
            Type conversion = Type.GetType(
                "UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; conversion == null && i < assemblies.Length; i++)
                conversion = assemblies[i].GetType("UnityEngine.ImageConversion");
            return conversion;
        }

        // Texture2D.LoadImage is a netstandard API whose signature mentions ReadOnlySpan,
        // which the .NET 4.0 csc cannot bind. Reflection keeps the plugin compilable.
        private static MethodInfo _loadImage;
        private static bool _loadImageResolved;

        private static bool LoadImage(Texture2D texture, byte[] bytes)
        {
            if (!_loadImageResolved)
            {
                _loadImageResolved = true;
                _loadImage = typeof(Texture2D).GetMethod(
                    "LoadImage",
                    new Type[] { typeof(byte[]) });
                if (_loadImage == null)
                {
                    Type conversion = FindImageConversion();
                    if (conversion != null)
                    {
                        _loadImage = conversion.GetMethod(
                            "LoadImage",
                            new Type[] { typeof(Texture2D), typeof(byte[]) });
                        if (_loadImage == null)
                        {
                            _loadImage = conversion.GetMethod(
                                "LoadImage",
                                new Type[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                        }
                    }
                }
            }

            if (_loadImage == null) return false;
            try
            {
                ParameterInfo[] parameters = _loadImage.GetParameters();
                object[] args = parameters.Length == 1
                    ? new object[] { bytes }
                    : (parameters.Length == 2
                        ? new object[] { texture, bytes }
                        : new object[] { texture, bytes, false });
                object target = _loadImage.IsStatic ? null : texture;
                object result = _loadImage.Invoke(target, args);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _textures.Values)
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
            }
            _textures.Clear();
            _missing.Clear();
            _invalid.Clear();
        }
    }
}
