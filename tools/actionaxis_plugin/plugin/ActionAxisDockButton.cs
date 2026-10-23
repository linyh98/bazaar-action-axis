using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin.Ui;
using UnityEngine;

namespace BazaarActionAxis.Plugin
{
    // BPP is optional: read its visible native controls without referencing or
    // changing its assembly. The hit surface is UGUI so IMGUI cannot click through
    // to the game beneath this otherwise purely decorative control.
    internal sealed class ActionAxisDockButton : IDisposable
    {
        private readonly List<DockButtonBounds> _anchors = new List<DockButtonBounds>();
        private readonly List<DockButtonBounds> _nativeAnchors = new List<DockButtonBounds>();
        private readonly List<DockButtonBounds> _blockers = new List<DockButtonBounds>();
        private readonly Vector3[] _corners = new Vector3[4];
        private UnityEngine.Object[] _buttons = new UnityEngine.Object[0];
        private Type _buttonType, _graphicType, _canvasType, _canvasGroupType;
        private GameObject _hitCanvas;
        private RectTransform _hitRect;
        private Texture2D _normal, _hover, _pressed;
        private GUIStyle _style;
        private float _nextScan;
        private int _lastLayoutFrame = -1;
        private bool _layoutAvailable;
        private DockButtonBounds _lastPlacement;
        private bool _loggedHitError;

        public void Draw(Action onClick)
        {
            DockButtonBounds placement;
            if (!ResolvePlacement(out placement)) { SetVisible(false); return; }
            Rect rect = new Rect((float)placement.X, (float)placement.Y,
                (float)placement.Width, (float)placement.Height);
            if (!SyncHitSurface(rect)) return;
            EnsureStyle();
            if (GUI.Button(rect, new GUIContent("", "行动轴 F8"), _style) && onClick != null)
                onClick();
            if (Event.current != null && rect.Contains(Event.current.mousePosition)) DrawTooltip(rect);
        }

        public void SetVisible(bool visible)
        {
            if (_hitCanvas != null && _hitCanvas.activeSelf != visible) _hitCanvas.SetActive(visible);
        }

        private bool ResolvePlacement(out DockButtonBounds placement)
        {
            if (_lastLayoutFrame == Time.frameCount)
            {
                placement = _lastPlacement;
                return _layoutAvailable;
            }
            _lastLayoutFrame = Time.frameCount;
            placement = new DockButtonBounds();
            _layoutAvailable = false;
            try
            {
                if (_buttonType == null) _buttonType = FindType("UnityEngine.UI.Button");
                if (_graphicType == null) _graphicType = FindType("UnityEngine.UI.Graphic");
                if (_canvasType == null) _canvasType = FindType("UnityEngine.Canvas");
                if (_canvasGroupType == null) _canvasGroupType = FindType("UnityEngine.CanvasGroup");
                if (_buttonType == null || _graphicType == null || _canvasType == null) return false;
                if (Time.unscaledTime >= _nextScan)
                {
                    // Keep inactive recorder clones in the cache. Their active
                    // state can change between frames while the scene stays put.
                    _buttons = UnityEngine.Object.FindObjectsOfType(_buttonType, true);
                    _nextScan = Time.unscaledTime + 0.5f;
                }

                _anchors.Clear(); _nativeAnchors.Clear(); _blockers.Clear();
                foreach (UnityEngine.Object candidate in _buttons)
                {
                    Component button = candidate as Component;
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    Behaviour behaviour = button as Behaviour;
                    if (behaviour != null && !behaviour.isActiveAndEnabled) continue;
                    DockButtonBounds bounds;
                    if (!TryFootprint(button, out bounds)) continue;
                    string objectName = button.gameObject.name;
                    bool bpp = objectName.StartsWith("BPP_CollectionDockButton_", StringComparison.Ordinal) ||
                        objectName == "BPP_CurrentReplayRecordingButton";
                    if (bpp) _anchors.Add(bounds);
                    else
                    {
                        _blockers.Add(bounds);
                        if (bounds.X > Screen.width * 0.65 && bounds.Y < Screen.height * 0.35 &&
                            bounds.Width < Screen.width * 0.12 && bounds.Height < Screen.height * 0.16 &&
                            (objectName.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             objectName.IndexOf("Option", StringComparison.OrdinalIgnoreCase) >= 0))
                            _nativeAnchors.Add(bounds);
                    }
                }
                _layoutAvailable = DockButtonLayout.TryPlace(Screen.width, Screen.height,
                    _anchors.Count > 0 ? _anchors : _nativeAnchors, _blockers,
                    Mathf.Clamp(Screen.height / 1080f, 0.55f, 1.5f) * 64f, out placement);
                _lastPlacement = placement;
                return _layoutAvailable;
            }
            catch { return false; }
        }

        private bool TryFootprint(Component button, out DockButtonBounds bounds)
        {
            bounds = new DockButtonBounds();
            Component[] graphics = button.GetComponentsInChildren(_graphicType, false);
            bool any = false;
            foreach (Component graphic in graphics)
            {
                if (graphic == null || !graphic.gameObject.activeInHierarchy || !OwnedBy(graphic.transform, button.transform))
                    continue;
                Behaviour behaviour = graphic as Behaviour;
                if (behaviour != null && !behaviour.isActiveAndEnabled) continue;
                Color color = (Color)Get(graphic, "color");
                if (color.a <= 0.01f || !HasVisibleAncestors(graphic.transform)) continue;
                object renderer = Get(graphic, "canvasRenderer");
                if (renderer != null && (bool)Get(renderer, "cull")) continue;
                if (renderer != null && Convert.ToSingle(renderer.GetType().GetMethod("GetAlpha").Invoke(renderer, null)) <= 0.01f)
                    continue;
                DockButtonBounds visual;
                if (!TryScreenBounds(graphic.transform as RectTransform, out visual)) continue;
                if (!any) bounds = visual;
                else
                {
                    double x = Math.Min(bounds.X, visual.X), y = Math.Min(bounds.Y, visual.Y);
                    bounds = new DockButtonBounds(x, y, Math.Max(bounds.Right, visual.Right) - x,
                        Math.Max(bounds.Bottom, visual.Bottom) - y, true);
                }
                any = true;
            }
            return any && bounds.IsValid;
        }

        private bool OwnedBy(Transform candidate, Transform owner)
        {
            for (Transform current = candidate; current != null; current = current.parent)
            {
                if (current == owner) return true;
                if (current.GetComponent(_buttonType) != null) return false;
            }
            return false;
        }

        private bool HasVisibleAncestors(Transform candidate)
        {
            float alpha = 1f;
            bool ignoreGroups = false;
            for (Transform current = candidate; current != null; current = current.parent)
            {
                Component canvas = current.GetComponent(_canvasType);
                Behaviour behaviour = canvas as Behaviour;
                if (behaviour != null && !behaviour.isActiveAndEnabled) return false;
                if (_canvasGroupType == null || ignoreGroups) continue;
                foreach (Component group in current.GetComponents(_canvasGroupType))
                {
                    alpha *= Convert.ToSingle(Get(group, "alpha"));
                    if ((bool)Get(group, "ignoreParentGroups")) { ignoreGroups = true; break; }
                }
            }
            return alpha > 0.01f;
        }

        private bool TryScreenBounds(RectTransform rect, out DockButtonBounds bounds)
        {
            bounds = new DockButtonBounds();
            if (rect == null) return false;
            Component rootCanvas = null;
            for (Transform current = rect; current != null; current = current.parent)
            {
                Component canvas = current.GetComponent(_canvasType);
                if (canvas != null) rootCanvas = canvas;
            }
            if (rootCanvas == null || Convert.ToInt32(Get(rootCanvas, "targetDisplay")) != 0) return false;
            int mode = Convert.ToInt32(Get(rootCanvas, "renderMode"));
            Camera camera = mode == 0 ? null : Get(rootCanvas, "worldCamera") as Camera;
            if (mode != 0 && camera == null) return false;
            rect.GetWorldCorners(_corners);
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (Vector3 corner in _corners)
            {
                Vector3 point = camera == null ? corner : camera.WorldToScreenPoint(corner);
                if (camera != null && point.z < 0) return false;
                minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
                minY = Math.Min(minY, point.y); maxY = Math.Max(maxY, point.y);
            }
            bounds = new DockButtonBounds(minX, Screen.height - maxY, maxX - minX, maxY - minY, true);
            return bounds.IsValid && bounds.Right > 0 && bounds.X < Screen.width &&
                bounds.Bottom > 0 && bounds.Y < Screen.height;
        }

        private bool SyncHitSurface(Rect screenRect)
        {
            try
            {
                if (_hitCanvas == null)
                {
                    Type raycasterType = FindType("UnityEngine.UI.GraphicRaycaster");
                    Type imageType = FindType("UnityEngine.UI.Image");
                    if (_canvasType == null || raycasterType == null || imageType == null)
                        throw new InvalidOperationException("Unity UI unavailable");
                    _hitCanvas = new GameObject("ActionAxis_DockHitCanvas", typeof(RectTransform), _canvasType, raycasterType);
                    _hitCanvas.SetActive(false);
                    _hitCanvas.layer = 5;
                    UnityEngine.Object.DontDestroyOnLoad(_hitCanvas);
                    Component canvas = _hitCanvas.GetComponent(_canvasType);
                    Set(canvas, "renderMode", Enum.ToObject(_canvasType.GetProperty("renderMode").PropertyType, 0));
                    Set(canvas, "overrideSorting", true);
                    Set(canvas, "sortingOrder", (int)short.MaxValue - 1);
                    GameObject hit = new GameObject("ActionAxis_DockHit", typeof(RectTransform), imageType);
                    hit.layer = 5;
                    _hitRect = hit.GetComponent<RectTransform>();
                    _hitRect.SetParent(_hitCanvas.transform, false);
                    _hitRect.anchorMin = _hitRect.anchorMax = new Vector2(0, 1);
                    _hitRect.pivot = new Vector2(0, 1);
                    Component image = hit.GetComponent(imageType);
                    Set(image, "color", new Color(0, 0, 0, 0));
                    Set(image, "raycastTarget", true);
                    // Zero-alpha UI still has to participate in raycasts.
                    // Unity can otherwise cull the transparent mesh entirely.
                    object renderer = Get(image, "canvasRenderer");
                    Set(renderer, "cullTransparentMesh", false);
                }
                _hitRect.anchoredPosition = new Vector2(screenRect.x, -screenRect.y);
                _hitRect.sizeDelta = new Vector2(screenRect.width, screenRect.height);
                SetVisible(true);
                return true;
            }
            catch (Exception error)
            {
                SetVisible(false);
                if (_hitCanvas != null) UnityEngine.Object.Destroy(_hitCanvas);
                _hitCanvas = null; _hitRect = null;
                if (!_loggedHitError)
                {
                    _loggedHitError = true;
                    Debug.LogWarning("[ActionAxis] dock input surface: " + error.GetBaseException().Message);
                }
                return false;
            }
        }

        private void EnsureStyle()
        {
            if (_style != null) return;
            _normal = BuildTexture(false, false);
            _hover = BuildTexture(true, false);
            _pressed = BuildTexture(true, true);
            _style = new GUIStyle();
            _style.normal.background = _normal;
            _style.hover.background = _hover;
            _style.active.background = _pressed;
            _style.focused.background = _hover;
            _style.border = new RectOffset(0, 0, 0, 0);
            _style.padding = new RectOffset(0, 0, 0, 0);
        }

        private static Texture2D BuildTexture(bool hover, bool pressed)
        {
            const int size = 128;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color sum = new Color(0, 0, 0, 0);
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            sum += IconPixel((x + (sx + 0.5f) * 0.5f) / size * 2f - 1f,
                                (y + (sy + 0.5f) * 0.5f) / size * 2f - 1f, hover, pressed);
                    pixels[y * size + x] = sum * 0.25f;
                }
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "ActionAxis_DockClock";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color IconPixel(float x, float y, bool hover, bool pressed)
        {
            float radius = Mathf.Sqrt(x * x + y * y);
            if (radius > 0.96f) return new Color(0, 0, 0, 0);
            Color gold = hover ? new Color(1f, 0.86f, 0.51f, 1) : new Color(0.73f, 0.60f, 0.35f, 1);
            if (radius > 0.88f) return gold;
            if (radius > 0.81f) return new Color(0.10f, 0.075f, 0.045f, 1);
            if (radius > 0.77f) return gold * new Color(0.65f, 0.65f, 0.65f, 1);
            Color background = pressed ? new Color(0.10f, 0.16f, 0.16f, 1) :
                hover ? new Color(0.17f, 0.25f, 0.24f, 1) : new Color(0.10f, 0.15f, 0.15f, 1);
            // A clock face above three ascending timeline columns.
            float clockY = y - 0.16f;
            float clockRadius = Mathf.Sqrt(x * x + clockY * clockY);
            bool face = clockRadius > 0.40f && clockRadius < 0.46f && y > -0.15f;
            bool hands = (Mathf.Abs(x) < 0.027f && y > 0.13f && y < 0.43f) ||
                (Mathf.Abs(y - (0.16f - x * 0.60f)) < 0.03f && x > -0.025f && x < 0.23f);
            if (face || hands) return new Color(0.98f, 0.91f, 0.73f, 1);
            bool columns = y > -0.49f && ((x > -0.35f && x < -0.20f && y < -0.27f) ||
                (x > -0.075f && x < 0.075f && y < -0.09f) ||
                (x > 0.20f && x < 0.35f && y < 0.09f));
            bool baseline = Mathf.Abs(y + 0.50f) < 0.024f && Mathf.Abs(x) < 0.45f;
            if (columns || baseline) return new Color(0.43f, 0.85f, 0.77f, 1);
            return background;
        }

        private static void DrawTooltip(Rect button)
        {
            string label = "行动轴  F8";
            Vector2 measured = Theme.SmallStyle.CalcSize(new GUIContent(label));
            float padding = 8 * Theme.Scale;
            Rect tooltip = new Rect(Math.Max(4, button.x - measured.x - padding * 3),
                button.y + (button.height - measured.y - padding * 2) * 0.5f,
                measured.x + padding * 2, measured.y + padding * 2);
            Theme.Fill(tooltip, Theme.Panel);
            Theme.Outline(tooltip, Theme.Border, Theme.Scale);
            GUI.Label(new Rect(tooltip.x + padding, tooltip.y + padding, measured.x, measured.y), label, Theme.SmallStyle);
        }

        public void Dispose()
        {
            if (_hitCanvas != null) UnityEngine.Object.Destroy(_hitCanvas);
            if (_normal != null) UnityEngine.Object.Destroy(_normal);
            if (_hover != null) UnityEngine.Object.Destroy(_hover);
            if (_pressed != null) UnityEngine.Object.Destroy(_pressed);
            _hitCanvas = null; _hitRect = null;
            _normal = _hover = _pressed = null;
            _style = null;
        }

        private static object Get(object instance, string property)
        {
            return instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(instance, null);
        }

        private static void Set(object instance, string property, object value)
        {
            instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public).SetValue(instance, value, null);
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
