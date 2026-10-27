using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin.Ui;
using UnityEngine;

namespace BazaarActionAxis.Plugin
{
    // Read BPP's visible in-run controls without referencing or changing its
    // assembly. The hit surface is UGUI so IMGUI cannot click through
    // to the game beneath this otherwise purely decorative control.
    internal sealed class ActionAxisDockButton : IDisposable
    {
        private readonly List<DockButtonBounds> _anchors = new List<DockButtonBounds>();
        private readonly List<DockButtonBounds> _nativeAnchors = new List<DockButtonBounds>();
        private readonly List<DockButtonBounds> _blockers = new List<DockButtonBounds>();
        private readonly Vector3[] _corners = new Vector3[4];
        private UnityEngine.Object[] _buttons = new UnityEngine.Object[0];
        private readonly HashSet<Component> _nativeButtons = new HashSet<Component>();
        private readonly DockButtonVisibility _visibility = new DockButtonVisibility(FindType);
        private Type _buttonType, _graphicType, _canvasType, _canvasGroupType, _imageType;
        private GameObject _hitCanvas;
        private RectTransform _hitRect;
        private Component _skinSource, _visualSource, _frameImage;
        private RectTransform _visualRoot;
        private Texture2D _iconTexture;
        private Sprite _iconSprite, _normalSprite;
        private object _nativeColors, _nativeSprites;
        private int _nativeTransition, _visualState = -1;
        private bool _pointerDown;
        private GUIStyle _style;
        private float _nextScan;
        private bool _loggedHitError;

        public bool CanOpen
        {
            get
            {
                DockButtonBounds placement;
                bool available = _visibility.CanOpen() && ResolvePlacement(out placement);
                if (!available) SetVisible(false);
                return available;
            }
        }

        public void Draw(Action onClick)
        {
            DockButtonBounds placement;
            // 面板状态逐次检查，不能复用本帧较早事件留下的可见性。
            if (!_visibility.CanOpen() || !ResolvePlacement(out placement)) { SetVisible(false); return; }
            Rect rect = new Rect((float)placement.X, (float)placement.Y,
                (float)placement.Width, (float)placement.Height);
            if (!SyncHitSurface(rect)) return;
            EnsureStyle();
            Event current = Event.current;
            bool hovered = current != null && rect.Contains(current.mousePosition);
            // The game enables Input System only; legacy Input mouse polling
            // throws before GUI.Button can process the click. IMGUI raw events
            // also let a release clear the visual state after Event.Use().
            if (current != null)
            {
                if (current.rawType == EventType.MouseLeaveWindow || current.rawType == EventType.Ignore)
                    _pointerDown = false;
                else if (current.button == 0)
                {
                    if (current.rawType == EventType.MouseDown) _pointerDown = hovered;
                    else if (current.rawType == EventType.MouseUp) _pointerDown = false;
                }
            }
            SyncVisualState(hovered, hovered && _pointerDown);
            if (GUI.Button(rect, new GUIContent("", "行动轴 F8"), _style) && onClick != null)
                onClick();
            if (hovered) DrawTooltip(rect);
        }

        public void SetVisible(bool visible)
        {
            if (_hitCanvas != null && _hitCanvas.activeSelf != visible) _hitCanvas.SetActive(visible);
            if (!visible) { _pointerDown = false; _visualState = -1; }
        }

        private bool ResolvePlacement(out DockButtonBounds placement)
        {
            // Active/canvas visibility can change between IMGUI events in the
            // same frame; only cache the object inventory, never eligibility.
            placement = new DockButtonBounds();
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
                    _nativeButtons.Clear();
                    CollectNativeButtons("FightMenuDialog", "SettingButton");
                    _nextScan = Time.unscaledTime + 0.5f;
                }

                _anchors.Clear(); _nativeAnchors.Clear(); _blockers.Clear();
                _skinSource = null;
                DockButtonBounds bppSize = new DockButtonBounds();
                foreach (UnityEngine.Object candidate in _buttons)
                {
                    Component button = candidate as Component;
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    Behaviour behaviour = button as Behaviour;
                    if (behaviour != null && !behaviour.isActiveAndEnabled) continue;
                    DockButtonBounds bounds;
                    if (!TryFootprint(button, out bounds)) continue;
                    string objectName = button.gameObject.name;
                    bool bpp = IsMainDockSibling(button) &&
                        (objectName.StartsWith("BPP_CollectionDockButton_", StringComparison.Ordinal) ||
                         objectName == "BPP_CurrentReplayRecordingButton");
                    if (bpp)
                    {
                        _anchors.Add(bounds);
                        // 优先使用数据库入口的外框，录制提示的大小和显隐不改变行动轴尺寸。
                        DockButtonBounds frame;
                        if ((objectName.StartsWith("BPP_CollectionDockButton_", StringComparison.Ordinal) || !bppSize.IsValid) &&
                            TryButtonFrame(button, out frame))
                        {
                            bppSize = frame;
                            _skinSource = button;
                        }
                    }
                    else
                    {
                        _blockers.Add(bounds);
                        // 只认对局中的原生设置入口；大厅与选英雄的按钮永不作为后备。
                        if (_nativeButtons.Contains(button) &&
                            (bool)_buttonType.GetMethod("IsInteractable").Invoke(button, null))
                        {
                            _nativeAnchors.Add(bounds);
                        }
                    }
                }
                if (_nativeAnchors.Count == 0 || _anchors.Count == 0 || !bppSize.IsValid) return false;
                return DockButtonLayout.TryPlace(Screen.width, Screen.height,
                    _anchors, _blockers, bppSize, out placement);
            }
            catch { return false; }
        }

        private void CollectNativeButtons(string typeName, params string[] fieldNames)
        {
            Type type = FindType(typeName);
            if (type == null) return;
            foreach (UnityEngine.Object owner in UnityEngine.Object.FindObjectsOfType(type, true))
            {
                if (owner == null) continue;
                foreach (string name in fieldNames)
                {
                    FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    object value = field == null ? null : field.GetValue(owner);
                    if (value == null) continue;
                    if (!_buttonType.IsInstanceOfType(value))
                    {
                        MethodInfo getter = value.GetType().GetMethod("GetButton", Type.EmptyTypes);
                        value = getter == null ? null : getter.Invoke(value, null);
                    }
                    Component button = value as Component;
                    if (button != null && _buttonType.IsInstanceOfType(button)) _nativeButtons.Add(button);
                }
            }
        }

        private bool IsMainDockSibling(Component button)
        {
            foreach (Component native in _nativeButtons)
                if (native != null && native.gameObject.activeInHierarchy &&
                    native.transform.parent == button.transform.parent) return true;
            return false;
        }

        private bool TryButtonFrame(Component button, out DockButtonBounds bounds)
        {
            // Native targetGraphic is the smaller, swapping center disc. The
            // decorative frame defines the visible button size, not that disc.
            foreach (Component graphic in button.GetComponentsInChildren(_graphicType, false))
                if (graphic.gameObject.name == "Btn_Frame" && OwnedBy(graphic.transform, button.transform) &&
                    TryScreenBounds(graphic.transform as RectTransform, out bounds)) return true;
            return TryScreenBounds(button.transform as RectTransform, out bounds);
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
                    _imageType = FindType("UnityEngine.UI.Image");
                    if (_canvasType == null || raycasterType == null || _imageType == null)
                        throw new InvalidOperationException("Unity UI unavailable");
                    _hitCanvas = new GameObject("ActionAxis_DockHitCanvas", typeof(RectTransform), _canvasType, raycasterType);
                    _hitCanvas.SetActive(false);
                    _hitCanvas.layer = 5;
                    UnityEngine.Object.DontDestroyOnLoad(_hitCanvas);
                    Component canvas = _hitCanvas.GetComponent(_canvasType);
                    Set(canvas, "renderMode", Enum.ToObject(_canvasType.GetProperty("renderMode").PropertyType, 0));
                    Set(canvas, "overrideSorting", true);
                    Set(canvas, "sortingOrder", (int)short.MaxValue - 1);
                    GameObject hit = new GameObject("ActionAxis_DockHit", typeof(RectTransform), _imageType);
                    hit.layer = 5;
                    _hitRect = hit.GetComponent<RectTransform>();
                    _hitRect.SetParent(_hitCanvas.transform, false);
                    _hitRect.anchorMin = _hitRect.anchorMax = new Vector2(0, 1);
                    _hitRect.pivot = new Vector2(0, 1);
                    Component image = hit.GetComponent(_imageType);
                    Set(image, "color", new Color(0, 0, 0, 0));
                    Set(image, "raycastTarget", true);
                    // Zero-alpha UI still has to participate in raycasts.
                    // Unity can otherwise cull the transparent mesh entirely.
                    object renderer = Get(image, "canvasRenderer");
                    Set(renderer, "cullTransparentMesh", false);
                }
                _hitRect.anchoredPosition = new Vector2(screenRect.x, -screenRect.y);
                _hitRect.sizeDelta = new Vector2(screenRect.width, screenRect.height);
                SyncNativeVisuals(screenRect);
                SetVisible(true);
                return true;
            }
            catch (Exception error)
            {
                SetVisible(false);
                if (_hitCanvas != null) UnityEngine.Object.Destroy(_hitCanvas);
                _hitCanvas = null; _hitRect = null;
                _visualRoot = null; _visualSource = null; _frameImage = null;
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
            // The UGUI visual tree owns the native skin. IMGUI keeps the
            // existing click handling, keyboard shortcut and tooltip only.
            _style = new GUIStyle();
            _style.border = new RectOffset(0, 0, 0, 0);
            _style.padding = new RectOffset(0, 0, 0, 0);
        }

        private void SyncNativeVisuals(Rect screenRect)
        {
            if (_skinSource == null) throw new InvalidOperationException("BPP button skin unavailable");
            if (_visualRoot == null || _visualSource != _skinSource)
            {
                if (_visualRoot != null) UnityEngine.Object.Destroy(_visualRoot.gameObject);
                if (_iconTexture == null)
                {
                    _iconTexture = DockButtonArtwork.CreateIcon();
                    _iconSprite = Sprite.Create(_iconTexture, new Rect(0, 0, _iconTexture.width, _iconTexture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _iconSprite.name = "ActionAxis_DockTimeline";
                }
                Dictionary<Component, Component> images = new Dictionary<Component, Component>();
                _visualRoot = CopyVisualTree(_skinSource.transform, _hitRect, images);
                _visualSource = _skinSource;
                bool iconFound = false;
                foreach (Component original in images.Keys)
                    if (original.gameObject.name == "BPP_DockButtonIcon") { iconFound = true; break; }
                if (!iconFound) throw new InvalidOperationException("BPP button icon unavailable");
                Component sourceFrame = Get(_skinSource, "targetGraphic") as Component;
                if (sourceFrame == null || !images.TryGetValue(sourceFrame, out _frameImage))
                    throw new InvalidOperationException("BPP button frame unavailable");
                // BPP records the baseline before hovering or opening its panel.
                // Read that baseline so our normal state cannot inherit a hover sprite.
                Type savedType = FindType("BazaarPlusPlus.Game.Settings.BppDockButtonNativeVisualState");
                Component saved = savedType == null ? null : _skinSource.GetComponent(savedType);
                object state = LiveSkillReader.GetMember(saved, "CapturedNativeState");
                _normalSprite = LiveSkillReader.GetMember(state, "NormalBaseSprite") as Sprite ?? Get(sourceFrame, "sprite") as Sprite;
                Set(_frameImage, "sprite", _normalSprite);
                _nativeTransition = Convert.ToInt32(Get(_skinSource, "transition"));
                _nativeColors = Get(_skinSource, "colors");
                _nativeSprites = Get(_skinSource, "spriteState");
                _visualState = -1;
            }
            DockButtonBounds frame, sourceBounds;
            RectTransform sourceRect = _skinSource.transform as RectTransform;
            if (sourceRect == null || !TryButtonFrame(_skinSource, out frame) ||
                !TryScreenBounds(sourceRect, out sourceBounds) || sourceRect.rect.width <= 0 || sourceRect.rect.height <= 0)
                throw new InvalidOperationException("BPP button geometry unavailable");
            float scaleX = screenRect.width / (float)frame.Width, scaleY = screenRect.height / (float)frame.Height;
            _visualRoot.anchorMin = _visualRoot.anchorMax = _visualRoot.pivot = new Vector2(0.5f, 0.5f);
            _visualRoot.sizeDelta = sourceRect.rect.size;
            _visualRoot.anchoredPosition = new Vector2((float)(sourceBounds.CenterX - frame.CenterX) * scaleX,
                (float)((frame.Y + frame.Height * 0.5) - (sourceBounds.Y + sourceBounds.Height * 0.5)) * scaleY);
            _visualRoot.localRotation = Quaternion.identity;
            _visualRoot.localScale = new Vector3((float)sourceBounds.Width / sourceRect.rect.width * scaleX,
                (float)sourceBounds.Height / sourceRect.rect.height * scaleY, 1);
        }

        private RectTransform CopyVisualTree(Transform source, Transform parent, Dictionary<Component, Component> images)
        {
            RectTransform original = source as RectTransform;
            if (original == null) return null;
            // Deliberately copy only rectangles and images, never BPP scripts,
            // native button listeners, animators or layout controllers.
            GameObject visual = new GameObject("ActionAxis_Visual_" + source.name, typeof(RectTransform));
            visual.SetActive(false);
            visual.layer = 5;
            RectTransform rect = visual.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = original.anchorMin; rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot; rect.sizeDelta = original.sizeDelta;
            rect.anchoredPosition3D = original.anchoredPosition3D;
            rect.localRotation = original.localRotation; rect.localScale = original.localScale;
            Component sourceImage = source.GetComponent(_imageType);
            if (sourceImage != null)
            {
                Component image = visual.AddComponent(_imageType);
                foreach (string property in new string[] { "sprite", "type", "color", "material", "preserveAspect",
                    "fillCenter", "fillMethod", "fillAmount", "fillOrigin", "fillClockwise", "pixelsPerUnitMultiplier" })
                    Set(image, property, Get(sourceImage, property));
                Set(image, "enabled", Get(sourceImage, "enabled"));
                Set(image, "raycastTarget", false);
                if (source.name == "BPP_DockButtonIcon")
                {
                    Set(image, "sprite", _iconSprite);
                    Set(image, "type", Enum.ToObject(_imageType.GetProperty("type").PropertyType, 0));
                    Set(image, "preserveAspect", true);
                    Set(image, "color", Color.white);
                    Set(image, "enabled", true);
                }
                images[sourceImage] = image;
            }
            for (int i = 0; i < source.childCount; i++) CopyVisualTree(source.GetChild(i), rect, images);
            visual.SetActive(source.gameObject.activeSelf);
            return rect;
        }

        private void SyncVisualState(bool hover, bool pressed)
        {
            int state = pressed ? 2 : hover ? 1 : 0;
            if (_frameImage == null || _visualState == state) return;
            bool reset = _visualState < 0;
            _visualState = state;
            if (_nativeTransition == 2) // Native Selectable.SpriteSwap.
            {
                Sprite sprite = state == 0 ? _normalSprite :
                    Get(_nativeSprites, state == 2 ? "pressedSprite" : "highlightedSprite") as Sprite;
                Set(_frameImage, "sprite", sprite == null ? _normalSprite : sprite);
            }
            else if (_nativeTransition != 0)
            {
                Color tint = (Color)Get(_nativeColors, state == 2 ? "pressedColor" : state == 1 ? "highlightedColor" : "normalColor");
                tint *= Convert.ToSingle(Get(_nativeColors, "colorMultiplier"));
                MethodInfo fade = _graphicType.GetMethod("CrossFadeColor", new Type[] { typeof(Color), typeof(float), typeof(bool), typeof(bool) });
                if (fade != null) fade.Invoke(_frameImage, new object[] { tint, reset ? 0f : Convert.ToSingle(Get(_nativeColors, "fadeDuration")), true, true });
            }
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
            if (_iconSprite != null) UnityEngine.Object.Destroy(_iconSprite);
            if (_iconTexture != null) UnityEngine.Object.Destroy(_iconTexture);
            _hitCanvas = null; _hitRect = null;
            _visualRoot = null; _skinSource = _visualSource = _frameImage = null;
            _iconSprite = _normalSprite = null; _iconTexture = null;
            _nativeColors = _nativeSprites = null;
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
