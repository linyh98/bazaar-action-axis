using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin.Ui;
using UnityEngine;
using UnityEngine.Rendering;

namespace BazaarActionAxis.Plugin
{
    public sealed class ArtworkCaptureResult
    {
        public int Saved;
        public int Existing;
        public int Missing;
        public string Error;
    }

    // Draws the actual native illustration mesh and material, including enchantment
    // shader keywords. Its Forward pass writes only that mesh to a render target:
    // no screen pixels, gems, game board, tooltip or Action Axis UI can enter a PNG.
    public static class NativeArtworkCapture
    {
        public static ArtworkCaptureResult CaptureMissing(IList<LiveCardEntry> cards, IconCache cache)
        {
            ArtworkCaptureResult result = new ArtworkCaptureResult();
            if (cards == null || cache == null) return result;
            cache.RefreshMissing();
            Type controllerType = FindType("ItemController");
            UnityEngine.Object[] controllers = controllerType == null
                ? new UnityEngine.Object[0] : UnityEngine.Object.FindObjectsOfType(controllerType);
            HashSet<string> handled = new HashSet<string>();
            List<string> missingReasons = new List<string>();

            foreach (LiveCardEntry card in cards)
            {
                if (card == null || string.IsNullOrEmpty(card.TemplateId)) continue;
                string key = IconCachePaths.KeyFor(card.TemplateId, card.Enchantment);
                if (!handled.Add(key)) continue;
                if (cache.Contains(card.TemplateId, card.Enchantment))
                {
                    result.Existing++;
                    continue;
                }

                bool saved = false;
                string lastReason = controllerType == null ? "ItemController type unavailable" :
                    "no matching native instance among " + controllers.Length + " controllers";
                foreach (UnityEngine.Object candidate in controllers)
                {
                    Texture2D artwork = null;
                    try
                    {
                        Component controller = candidate as Component;
                        Renderer source;
                        Material material;
                        string reason;
                        if (!TryGetArtwork(controller, card, out source, out material, out reason))
                        {
                            if (reason != null) lastReason = reason;
                            continue;
                        }
                        artwork = RenderArtwork(source, material, out reason);
                        if (artwork != null && cache.TryStore(card.TemplateId, card.Enchantment, artwork))
                        {
                            saved = true;
                            result.Saved++;
                            Debug.Log("[ActionAxis] artwork cached " + key + "; " + DescribeMaterial(material) +
                                "; " + DescribeIllustration(source, material));
                            break;
                        }
                        lastReason = artwork == null ? reason : "PNG encoding/cache store declined";
                    }
                    catch (Exception error)
                    {
                        lastReason = error.GetBaseException().GetType().Name + ": " +
                            error.GetBaseException().Message;
                    }
                    finally
                    {
                        if (artwork != null) UnityEngine.Object.Destroy(artwork);
                    }
                }
                // A loading/hidden/animating card is retried on the next recording.
                if (!saved)
                {
                    result.Missing++;
                    missingReasons.Add(key + ": " + lastReason);
                }
            }
            if (missingReasons.Count > 0)
                result.Error = "controllers=" + controllers.Length + "; saved=" + result.Saved +
                    "; existing=" + result.Existing + "; missing=" + result.Missing + "; " +
                    string.Join(" | ", missingReasons.ToArray());
            return result;
        }

        private static bool TryGetArtwork(
            Component controller, LiveCardEntry expected, out Renderer renderer, out Material material,
            out string reason)
        {
            renderer = null;
            material = null;
            reason = null;
            if (controller == null) return false;
            if (string.IsNullOrEmpty(expected.InstanceId))
            { reason = "snapshot instance ID missing"; return false; }
            object card = Read(controller, "CardData");
            object instance = Read(card, "InstanceId");
            string instanceId = Text(Read(instance, "Value"));
            if (string.IsNullOrEmpty(instanceId)) instanceId = Text(instance);
            if (!string.Equals(instanceId, expected.InstanceId, StringComparison.Ordinal)) return false;
            if (!controller.gameObject.activeInHierarchy)
            { reason = "matching controller inactive"; return false; }
            if (!string.Equals(Text(Read(card, "TemplateId")), expected.TemplateId,
                    StringComparison.OrdinalIgnoreCase))
            { reason = "native template changed since snapshot"; return false; }
            if (IconCachePaths.NormalizeEnchantment(Text(Read(card, "Enchantment"))) !=
                IconCachePaths.NormalizeEnchantment(expected.Enchantment))
            { reason = "native enchantment changed since snapshot"; return false; }

            // Locked upgrade previews can display another tier/enchantment without
            // changing CardData; never persist their temporary material as this card.
            if (IsTrue(Read(controller, "_isInUpgradePreview")) ||
                Read(controller, "_activePreviewTier") != null ||
                Read(controller, "_activePreviewEnchantment") != null)
            { reason = "upgrade/enchantment preview active"; return false; }
            if (IsTrue(Read(controller, "IsDragging")))
            { reason = "card dragging"; return false; }
            if (!IsTrue(Read(controller, "IsCardVisible")))
            { reason = "IsCardVisible false or unavailable"; return false; }
            if (!IsTrue(Read(controller, "PositionedInSocket")))
            { reason = "PositionedInSocket false or unavailable"; return false; }

            object visuals = Read(controller, "VisualsController");
            if (visuals == null) { reason = "VisualsController unavailable"; return false; }
            if (IsTrue(Read(visuals, "_backVisible")))
            { reason = "card back visible"; return false; }
            object tier = Read(visuals, "currentTier");
            if (tier == null || !string.Equals(Text(tier), expected.Tier,
                StringComparison.OrdinalIgnoreCase))
            { reason = "visual tier " + Text(tier) + " differs from snapshot " + expected.Tier; return false; }
            renderer = Read(visuals, "cardIllustrationRenderer") as Renderer;
            material = Read(visuals, "materialInstance") as Material;
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                material == null || renderer.sharedMaterial != material)
            { reason = "art renderer/material unavailable, hidden or not bound to native instance"; return false; }

            object animator = Read(controller, "Animator");
            if (animator != null)
            {
                Type type = animator.GetType();
                MethodInfo getState = type.GetMethod("GetCurrentAnimatorStateInfo", new Type[] { typeof(int) });
                MethodInfo isTransition = type.GetMethod("IsInTransition", new Type[] { typeof(int) });
                if (getState == null || isTransition == null ||
                    IsTrue(isTransition.Invoke(animator, new object[] { 0 })))
                { reason = "animator transitioning or API unavailable"; return false; }
                object state = getState.Invoke(animator, new object[] { 0 });
                MethodInfo isName = state.GetType().GetMethod("IsName", new Type[] { typeof(string) });
                if (isName == null || !IsTrue(isName.Invoke(state, new object[] { "Card_Idle_Faceup_A" })))
                {
                    reason = "animator not in Card_Idle_Faceup_A; state=" + Text(Read(state, "shortNameHash"));
                    return false;
                }
            }

            // PremiumCard also contains shared FX/mask textures. Only the actual
            // illustration in _MainTex establishes that this card's art loaded.
            if (material.shader != null && material.shader.name == "The Bazaar/PremiumCard")
            {
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Texture illustration = properties.GetTexture("_MainTex");
                if (illustration == null) illustration = material.GetTexture("_MainTex");
                if (illustration != null && illustration.width > 16 && illustration.height > 16) return true;
                reason = "native _MainTex illustration not loaded; " + DescribeMaterial(material);
                return false;
            }
            // Art that has not finished loading must not permanently claim coverage.
            string[] textures = material.GetTexturePropertyNames();
            for (int i = 0; i < textures.Length; i++)
            {
                Texture texture = material.GetTexture(textures[i]);
                if (texture != null && texture.width > 16 && texture.height > 16) return true;
            }
            reason = "native material has no loaded artwork texture over 16px";
            return false;
        }

        private static Texture2D RenderArtwork(Renderer source, Material nativeMaterial, out string reason)
        {
            reason = null;
            MeshFilter filter = source.GetComponent<MeshFilter>();
            Mesh mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null || mesh.subMeshCount != 1)
            { reason = "art mesh unavailable or has multiple submeshes"; return null; }
            Mesh ownedMesh = null;
            Vector3[] vertices;
            Vector2[] uv;
            int[] triangles;
            if (mesh.isReadable)
            {
                vertices = mesh.vertices;
                uv = mesh.uv;
                triangles = mesh.triangles;
            }
            else
            {
                // GPU-only imported meshes cannot be read back through vertices/uv.
                // Use a quad only to measure camera framing. The GPU still draws
                // the original native mesh, retaining its exact UVs and art region.
                ownedMesh = MakeArtworkQuad(mesh.bounds);
                vertices = ownedMesh.vertices;
                uv = ownedMesh.uv;
                triangles = ownedMesh.triangles;
            }
            try
            {
                return RenderMesh(source, nativeMaterial, mesh, vertices, uv, triangles, out reason);
            }
            finally
            {
                if (ownedMesh != null) UnityEngine.Object.Destroy(ownedMesh);
            }
        }

        private static Texture2D RenderMesh(Renderer source, Material nativeMaterial, Mesh mesh,
            Vector3[] vertices, Vector2[] uv, int[] triangles, out string reason)
        {
            reason = null;
            Vector3 right;
            Vector3 up;
            if (!TryGetArtAxes(vertices, uv, triangles, out right, out up))
            { reason = "art mesh has no usable UV axes"; return null; }
            Matrix4x4 objectToWorld = source.localToWorldMatrix;
            right = objectToWorld.MultiplyVector(right).normalized;
            up = objectToWorld.MultiplyVector(up).normalized;
            up = (up - right * Vector3.Dot(up, right)).normalized;

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = objectToWorld.MultiplyPoint3x4(vertices[i]);
                float x = Vector3.Dot(world, right);
                float y = Vector3.Dot(world, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float bodyWidth = maxX - minX;
            float bodyHeight = maxY - minY;
            if (!(bodyWidth > 0.001f && bodyHeight > 0.001f))
            { reason = "art plane bounds degenerate"; return null; }
            float aspect = bodyWidth / bodyHeight;
            if (aspect < 0.15f || aspect > 4f)
            { reason = "art plane aspect outside capture range: " + aspect; return null; }
            int height = 384;
            int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 64, 1536);
            Vector3 normal = Vector3.Cross(up, right).normalized;
            Vector3 center = right * ((minX + maxX) * 0.5f) + up * ((minY + maxY) * 0.5f) +
                normal * Vector3.Dot(objectToWorld.MultiplyPoint3x4(mesh.bounds.center), normal);
            float distance = Mathf.Max(bodyWidth, bodyHeight) * 2f + 1f;
            Vector3 position = center + normal * distance;
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            Texture2D capture = null;
            Material material = null;
            CommandBuffer commands = null;
            try
            {
                target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                material = new Material(nativeMaterial);
                // The native PremiumCard asset has a Forward colour pass and
                // separate ShadowCaster/DepthOnly/DepthNormals passes. Running a
                // fresh URP camera here selects a depth-only attachment in this
                // game. Draw the actual Forward pass directly instead, preserving
                // its exact shader keywords, textures and per-renderer properties.
                int pass = material.FindPass("Forward");
                if (pass < 0) pass = material.FindPass("UniversalForward");
                if (pass < 0) pass = material.FindPass("UniversalForwardOnly");
                if (pass < 0)
                { reason = "native colour pass unavailable; " + DescribeMaterial(material); return null; }
                if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
                if (material.HasProperty("_CullMode")) material.SetInt("_CullMode", 0);
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                source.GetPropertyBlock(properties);

                Matrix4x4 view = Matrix4x4.identity;
                view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, position)));
                view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, position)));
                view.SetRow(2, new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, position)));
                Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(
                    -bodyWidth * .5f, bodyWidth * .5f, -bodyHeight * .5f, bodyHeight * .5f,
                    .01f, distance * 2f), true);
                Matrix4x4 previousView = Shader.GetGlobalMatrix("unity_MatrixV");
                Matrix4x4 previousProjection = Shader.GetGlobalMatrix("glstate_matrix_projection");
                Matrix4x4 previousVp = Shader.GetGlobalMatrix("unity_MatrixVP");
                Vector4 previousCamera = Shader.GetGlobalVector("_WorldSpaceCameraPos");
                Vector4 previousScreen = Shader.GetGlobalVector("_ScreenParams");
                commands = new CommandBuffer();
                commands.name = "Action Axis native artwork Forward pass";
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(true, true, Color.clear);
                commands.SetViewport(new Rect(0, 0, width, height));
                commands.SetViewProjectionMatrices(view, projection);
                // URP shaders read unity_MatrixVP directly. SetViewProjectionMatrices
                // alone only updates the built-in pipeline's matrix state.
                commands.SetGlobalMatrix("unity_MatrixVP", projection * view);
                commands.SetGlobalMatrix("unity_MatrixV", view);
                commands.SetGlobalMatrix("glstate_matrix_projection", projection);
                commands.SetGlobalVector("_WorldSpaceCameraPos", new Vector4(position.x, position.y, position.z, 1));
                commands.SetGlobalVector("_ScreenParams", new Vector4(width, height, 1f + 1f / width, 1f + 1f / height));
                commands.DrawMesh(mesh, objectToWorld, material, 0, pass, properties);
                commands.SetGlobalMatrix("unity_MatrixVP", previousVp);
                commands.SetGlobalMatrix("unity_MatrixV", previousView);
                commands.SetGlobalMatrix("glstate_matrix_projection", previousProjection);
                commands.SetGlobalVector("_WorldSpaceCameraPos", previousCamera);
                commands.SetGlobalVector("_ScreenParams", previousScreen);
                commands.SetViewProjectionMatrices(previousView, previousProjection);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                capture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                capture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                capture.Apply();
                if (!HasArtworkPixels(capture))
                {
                    reason = "native Forward draw blank/transparent; " + DescribeMaterial(nativeMaterial) +
                        "; " + DescribeIllustration(source, nativeMaterial) +
                        "; mesh=" + mesh.name + "; readable=" + mesh.isReadable + "; bounds=" + mesh.bounds +
                        "; output=" + width + "x" + height;
                    return null;
                }
                Texture2D ready = capture;
                capture = null;
                return ready;
            }
            finally
            {
                RenderTexture.active = previous;
                if (commands != null) commands.Release();
                if (material != null) UnityEngine.Object.Destroy(material);
                if (capture != null) UnityEngine.Object.Destroy(capture);
                if (target != null) RenderTexture.ReleaseTemporary(target);
            }
        }

        private static string DescribeMaterial(Material material)
        {
            List<string> passes = new List<string>();
            for (int i = 0; i < material.passCount; i++) passes.Add(i + ":" + material.GetPassName(i));
            List<string> textures = new List<string>();
            foreach (string property in material.GetTexturePropertyNames())
            {
                Texture texture = material.GetTexture(property);
                if (texture != null && texture.width > 16 && texture.height > 16)
                    textures.Add(property + "=" + texture.name + "[" + texture.width + "x" + texture.height + "]");
            }
            return "shader=" + material.shader.name + "; pass=" + string.Join(",", passes.ToArray()) +
                "; keywords=" + string.Join(",", material.shaderKeywords) +
                "; textures=" + string.Join(",", textures.ToArray());
        }

        private static string DescribeIllustration(Renderer renderer, Material material)
        {
            MaterialPropertyBlock properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Texture texture = properties.GetTexture("_MainTex");
            string origin = "renderer._MainTex";
            if (texture == null && material.HasProperty("_MainTex"))
            { texture = material.GetTexture("_MainTex"); origin = "material._MainTex"; }
            return origin + "=" + (texture == null ? "null" : texture.name + "[" +
                texture.width + "x" + texture.height + "]");
        }

        private static Mesh MakeArtworkQuad(Bounds bounds)
        {
            Vector3 size = bounds.size;
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;
            float width = size.x, height = size.y;
            if (size.y < size.x && size.y < size.z)
            {
                up = Vector3.forward;
                height = size.z;
            }
            else if (size.x < size.y && size.x < size.z)
            {
                right = Vector3.forward;
                width = size.z;
            }
            right *= width * .5f;
            up *= height * .5f;
            Mesh quad = new Mesh();
            quad.name = "Action Axis artwork capture quad";
            quad.vertices = new Vector3[] { bounds.center - right - up, bounds.center - right + up,
                bounds.center + right + up, bounds.center + right - up };
            quad.uv = new Vector2[] { new Vector2(0, 0), new Vector2(0, 1),
                new Vector2(1, 1), new Vector2(1, 0) };
            quad.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateNormals();
            quad.RecalculateBounds();
            return quad;
        }

        private static bool TryGetArtAxes(Vector3[] vertices, Vector2[] uv, int[] triangles,
            out Vector3 right, out Vector3 up)
        {
            right = Vector3.right;
            up = Vector3.up;
            if (vertices.Length != uv.Length) return false;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 p = vertices[b] - vertices[a], q = vertices[c] - vertices[a];
                Vector2 u = uv[b] - uv[a], v = uv[c] - uv[a];
                float determinant = u.x * v.y - u.y * v.x;
                if (Mathf.Abs(determinant) < 0.00001f) continue;
                right = ((p * v.y - q * u.y) / determinant).normalized;
                Vector3 bitangent = (q * u.x - p * v.x) / determinant;
                up = (bitangent - right * Vector3.Dot(bitangent, right)).normalized;
                return right.sqrMagnitude > .9f && up.sqrMagnitude > .9f;
            }
            return false;
        }

        private static bool HasArtworkPixels(Texture2D capture)
        {
            Color32[] pixels = capture.GetPixels32();
            int visible = 0;
            int min = 255, max = 0;
            for (int i = 0; i < pixels.Length; i += 8)
            {
                Color32 pixel = pixels[i];
                if (pixel.a < 16) continue;
                visible++;
                int light = (pixel.r + pixel.g + pixel.b) / 3;
                min = Math.Min(min, light); max = Math.Max(max, light);
            }
            return visible > pixels.Length / 160 && max - min > 8;
        }

        private static bool IsTrue(object value) { return value is bool && (bool)value; }
        private static string Text(object value) { return value == null ? null : value.ToString(); }

        private static object Read(object target, string name)
        {
            if (target == null) return null;
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.DeclaredOnly;
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(target, null);
                FieldInfo field = type.GetField(name, flags);
                if (field != null) return field.GetValue(target);
            }
            return null;
        }

        private static Type FindType(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
