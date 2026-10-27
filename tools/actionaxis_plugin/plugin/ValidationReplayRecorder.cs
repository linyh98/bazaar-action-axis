using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BazaarActionAxis.Engine;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace BazaarActionAxis.Plugin
{
    // No overlay visibility or BPP installation required.
    internal sealed class ValidationReplayRecorder : MonoBehaviour
    {
        private static ValidationReplayRecorder _instance;
        private ValidationReplayCapture _capture;
        private ValidationReplayWriter _writer;
        private Harmony _harmony;
        private object _catalogManager;
        private string _catalogFile;
        private JsonValue _environment;
        private float _nextCatalog;
        private bool _failed;
        private MethodBase _target;

        private sealed class Pending
        {
            public string Kind, MessageId;
            public bool Opening;
            public JsonValue Before, FrozenMessage;
        }

        private void Awake()
        {
            _instance = this;
            try
            {
                string folder = PathConfig.ResolveDataDir(Paths.PluginPath,
                    ActionAxisPlugin.Instance.Settings.VerificationDir.Value);
                _writer = new ValidationReplayWriter(folder);
                _environment = EnvironmentInfo();
                ResetCapture();
                Type processor = LiveSkillReader.FindType("TheBazaar.NetMessageProcessor");
                Type message = LiveSkillReader.FindType("BazaarGameShared.Infra.Messages.INetMessage");
                if (processor == null || message == null) throw new InvalidOperationException("游戏消息接收接口不可用");
                _target = AccessTools.Method(processor, "Receive", new Type[] { message, typeof(bool) }) ??
                    AccessTools.Method(processor, "ReceiveOrQueue", new Type[] { message });
                if (_target == null) throw new InvalidOperationException("未找到 Receive/ReceiveOrQueue");
                _harmony = new Harmony("com.bazaarsim.actionaxis.validation");
                _harmony.Patch(_target,
                    new HarmonyMethod(typeof(ValidationReplayRecorder), "Prefix") { priority = Priority.First },
                    new HarmonyMethod(typeof(ValidationReplayRecorder), "Postfix") { priority = Priority.Last });
                Status("ready", folder);
                Debug.Log("[ActionAxis] verification recorder ready: " + folder + "; hook=" + _target);
            }
            catch (Exception error) { Fail(error); }
        }

        private void ResetCapture()
        {
            _catalogFile = "catalog-" + Guid.NewGuid().ToString("N") + ".json.gz";
            _capture = new ValidationReplayCapture(_writer.Save, _environment, _catalogFile);
        }

        private static void Prefix(object[] __args, out Pending __state)
        {
            __state = null;
            ValidationReplayRecorder self = _instance;
            if (self == null || self._failed ||
                __args.Length == 0 || __args[0] == null ||
                (__args.Length > 1 && __args[1] is bool && (bool)__args[1])) return;
            try
            {
                Type bpp = LiveSkillReader.FindType("BazaarPlusPlus.Game.CombatReplay.CombatReplayRuntime");
                object runtime = bpp == null ? null : LiveSkillReader.GetStatic(bpp, "Instance");
                object saved = LiveSkillReader.GetMember(runtime, "HasSavedReplaySession");
                if (saved is bool && (bool)saved) return;
                object message = __args[0];
                string kind = message.GetType().Name;
                if (kind != "NetMessageGameSim" && kind != "NetMessageCombatSim") return;
                bool opening = ValidationReplayCapture.IsOpening(message);
                if (kind == "NetMessageGameSim" && !opening && !self._capture.HasPendingFight) return;
                __state = new Pending {
                    Kind = kind, Opening = opening,
                    MessageId = Convert.ToString(LiveSkillReader.GetMember(message, "MessageId")),
                    Before = opening ? ValidationSnapshot.Capture() : null,
                    FrozenMessage = ValidationSnapshot.Message(message)
                };
            }
            catch (Exception error) { self.Fail(error); }
        }

        private static void Postfix(Pending __state)
        {
            if (__state == null || _instance == null || _instance._failed) return;
            try
            {
                _instance._capture.Accept(__state.Kind, __state.MessageId, __state.Opening,
                    __state.FrozenMessage, __state.Before,
                    __state.Opening ? ValidationSnapshot.Capture() : null);
            }
            catch (Exception error) { _instance.Fail(error); }
        }

        private void Update()
        {
            if (_writer == null) return;
            string error;
            while (_writer.TryGetError(out error)) Fail(new IOException(error));
            if (_failed || Time.unscaledTime < _nextCatalog) return;
            _nextCatalog = Time.unscaledTime + 5;
            try
            {
                object manager = ValidationSnapshot.ReadyManager();
                if (manager == null || object.ReferenceEquals(manager, _catalogManager)) return;
                if (_catalogManager != null)
                {
                    _capture.Interrupt("static-data-generation-changed");
                    ResetCapture();
                }
                _catalogManager = manager;
                // Same read-only static DB operation BPP uses on its worker; no live Unity state.
                _writer.Catalog(_catalogFile, manager);
            }
            catch (Exception failure) { Fail(failure); }
        }

        private JsonValue EnvironmentInfo()
        {
            Type shared = LiveSkillReader.FindType("BazaarGameShared.MessagePackConfig");
            Type game = LiveSkillReader.FindType("TheBazaar.Data");
            string hash = null;
            if (shared != null) using (SHA256 sha = SHA256.Create()) using (FileStream file = File.OpenRead(shared.Assembly.Location))
                hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            return LiveSkillReader.SerializeTemplate(new Dictionary<string, object> {
                { "PluginVersion", "1.6.10" }, { "GameVersion", Application.version },
                { "EngineMvid", typeof(BattleEngine).Assembly.ManifestModule.ModuleVersionId.ToString() },
                { "SharedAssemblySha256", hash },
                { "GameMvid", game == null ? null : game.Assembly.ManifestModule.ModuleVersionId.ToString() },
                { "CapturePoint", "NetMessageProcessor prefix raw message + before/after dispatch snapshots" }
            });
        }
        private void Status(string state, string detail)
        {
            if (_writer == null) return;
            _writer.Save("recorder-status.json.gz", LiveSkillReader.SerializeTemplate(new Dictionary<string, object> {
                { "State", state }, { "Detail", detail }, { "UpdatedAtUtc", DateTime.UtcNow.ToString("o") },
                { "Hook", _target == null ? null : _target.ToString() }, { "Environment", _environment }
            }));
        }
        private void Fail(Exception error)
        {
            string message = ValidationSnapshot.Error(error);
            Debug.LogError("[ActionAxis] 验证录制失败: " + message);
            if (_failed) return;
            _failed = true;
            try { if (_capture != null) _capture.Interrupt("recorder-error: " + message); Status("failed", message); }
            catch (Exception) { /* Original failure already logged. */ }
        }
        private void OnDestroy()
        {
            if (_harmony != null && _target != null) _harmony.Unpatch(_target, HarmonyPatchType.All, _harmony.Id);
            if (_capture != null) try { _capture.Interrupt("plugin-stopped"); } catch (Exception error) { Debug.LogError(error); }
            if (_writer != null)
            {
                _writer.Dispose();
                string error;
                while (_writer.TryGetError(out error)) Debug.LogError("[ActionAxis] 验证写入失败: " + error);
            }
            if (_instance == this) _instance = null;
        }
    }
}
