using System;
using System.Reflection;
using UnityEngine;

namespace BazaarActionAxis.Plugin
{
    // IMGUI's Event.Use() does not consume UGUI raycasts. Match BPP's native
    // EndOfRunMouseBlocker with an invisible, highest-order UGUI hit surface.
    // Reflection keeps this optional adapter free of extra Unity UI references.
    internal sealed class NativeInputBlocker : IDisposable
    {
        private GameObject _canvasObject;
        private PropertyInfo _nativeBlockInput;
        private bool _ownsNativeInput;
        private bool _previousNativeInput;
        private object _eventSystem;
        private GameObject _previousSelection;
        private bool _blocked;

        public string Error { get; private set; }

        public bool SetBlocked(bool blocked)
        {
            if (!blocked)
            {
                Release();
                return true;
            }
            try
            {
                EnsureCanvas();
                if (!_blocked)
                {
                    AcquireNativeInput();
                    CaptureFocus();
                    _blocked = true;
                }
                // Reassert only the owned flag if the game resets it on a screen
                // transition. Closing restores exactly the value captured above.
                if (_ownsNativeInput) _nativeBlockInput.SetValue(null, true, null);
                _canvasObject.SetActive(true);
                Error = null;
                return true;
            }
            catch (Exception error)
            {
                Release();
                // A failed first build may have left an incomplete inactive
                // canvas. Rebuild it fully on the next attempt.
                if (_canvasObject != null) UnityEngine.Object.Destroy(_canvasObject);
                _canvasObject = null;
                Error = "无法阻止游戏底层输入：" + error.GetBaseException().Message;
                return false;
            }
        }

        private void EnsureCanvas()
        {
            if (_canvasObject != null) return;
            Type canvasType = FindType("UnityEngine.Canvas");
            Type raycasterType = FindType("UnityEngine.UI.GraphicRaycaster");
            Type imageType = FindType("UnityEngine.UI.Image");
            if (canvasType == null || raycasterType == null || imageType == null)
                throw new InvalidOperationException("Unity 原生 UI 组件不可用");

            _canvasObject = new GameObject("ActionAxis_NativeInputBlocker", typeof(RectTransform), canvasType, raycasterType);
            // Prevent the new object from intercepting input until it is ready.
            _canvasObject.SetActive(false);
            _canvasObject.layer = 5; // Unity's built-in UI layer.
            UnityEngine.Object.DontDestroyOnLoad(_canvasObject);
            Component canvas = _canvasObject.GetComponent(canvasType);
            Set(canvas, "renderMode", Enum.ToObject(canvasType.GetProperty("renderMode").PropertyType, 0));
            Set(canvas, "overrideSorting", true);
            Set(canvas, "sortingOrder", (int)short.MaxValue);

            GameObject hit = new GameObject("ActionAxis_InputHitSurface", typeof(RectTransform), imageType);
            hit.layer = 5;
            RectTransform rect = hit.GetComponent<RectTransform>();
            rect.SetParent(_canvasObject.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            Component image = hit.GetComponent(imageType);
            Set(image, "color", new Color(0, 0, 0, 0));
            Set(image, "raycastTarget", true);
        }

        private void AcquireNativeInput()
        {
            // Verified in the installed TheBazaarRuntime.dll and used by BPP's
            // current-replay input ownership. This is runtime UI state only.
            Type appState = FindType("TheBazaar.AppState");
            _nativeBlockInput = appState == null ? null : appState.GetProperty("BlockInput", BindingFlags.Static | BindingFlags.Public);
            if (_nativeBlockInput == null || !_nativeBlockInput.CanRead || !_nativeBlockInput.CanWrite)
                throw new InvalidOperationException("AppState.BlockInput 不可用");
            _previousNativeInput = (bool)_nativeBlockInput.GetValue(null, null);
            _ownsNativeInput = true;
            _nativeBlockInput.SetValue(null, true, null);
        }

        private void CaptureFocus()
        {
            Type eventSystemType = FindType("UnityEngine.EventSystems.EventSystem");
            if (eventSystemType == null) return;
            PropertyInfo current = eventSystemType.GetProperty("current", BindingFlags.Static | BindingFlags.Public);
            _eventSystem = current == null ? null : current.GetValue(null, null);
            if (_eventSystem == null) return;
            PropertyInfo selected = eventSystemType.GetProperty("currentSelectedGameObject");
            _previousSelection = selected == null ? null : selected.GetValue(_eventSystem, null) as GameObject;
            Select(_eventSystem, null);
        }

        private void Release()
        {
            if (_canvasObject != null) _canvasObject.SetActive(false);
            if (_ownsNativeInput)
            {
                try { _nativeBlockInput.SetValue(null, _previousNativeInput, null); }
                catch { }
            }
            _ownsNativeInput = false;
            if (_eventSystem != null)
            {
                try
                {
                    PropertyInfo selected = _eventSystem.GetType().GetProperty("currentSelectedGameObject");
                    // Preserve a selection acquired by another owner meanwhile.
                    if (selected != null && selected.GetValue(_eventSystem, null) == null && _previousSelection != null)
                        Select(_eventSystem, _previousSelection);
                }
                catch { }
            }
            _eventSystem = null;
            _previousSelection = null;
            _blocked = false;
        }

        public void Dispose()
        {
            Release();
            if (_canvasObject != null) UnityEngine.Object.Destroy(_canvasObject);
            _canvasObject = null;
        }

        private static void Select(object eventSystem, GameObject selected)
        {
            MethodInfo method = eventSystem.GetType().GetMethod("SetSelectedGameObject", new[] { typeof(GameObject) });
            if (method != null) method.Invoke(eventSystem, new object[] { selected });
        }

        private static void Set(object instance, string name, object value)
        {
            instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public).SetValue(instance, value, null);
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
