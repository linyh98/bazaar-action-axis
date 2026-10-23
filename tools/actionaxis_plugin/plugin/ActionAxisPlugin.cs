using System;
using System.Collections.Generic;
using System.IO;
using BazaarActionAxis.Engine;
using BazaarActionAxis.Plugin.Ui;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace BazaarActionAxis.Plugin
{
    [BepInPlugin("com.bazaarsim.actionaxis", "Bazaar Action Axis", "1.4.0")]
    public sealed class ActionAxisPlugin : BaseUnityPlugin
    {
        internal static ActionAxisPlugin Instance;
        internal PluginConfig Settings;

        private void Awake()
        {
            Instance = this;
            Settings = new PluginConfig();
            Settings.ToggleKey = Config.Bind(
                "General",
                "ToggleKey",
                new KeyboardShortcut(KeyCode.F8),
                "打开/关闭行动轴");
            Settings.DataDir = Config.Bind(
                "General",
                "DataDir",
                "BazaarActionAxis",
                "数据目录（cards.json 与 icons）");

            gameObject.AddComponent<ActionAxisOverlay>();
            Logger.LogInfo("[ActionAxis] loaded");
        }
    }

    public sealed class PluginConfig
    {
        public ConfigEntry<KeyboardShortcut> ToggleKey;
        public ConfigEntry<string> DataDir;
    }

    internal sealed class ActionAxisOverlay : MonoBehaviour
    {
        private ActionAxisPanel _panel;
        private IconCache _icons;
        private CardCatalog _catalog;
        private SkillCatalog _skillCatalog;
        private LiveBoardReader _reader;
        private string _dataDir;
        private bool _ready;
        private bool _refreshRequested;
        private int _dummySeed = 1;
        private string _monsterChoiceKey;
        private int _selectedMonsterIndex;
        private float _nextReplayCapture;
        private string _savedReplayId;
        private int _savedReplayFrameCount;
        private readonly NativeInputBlocker _inputBlocker = new NativeInputBlocker();
        private readonly ActionAxisDockButton _dockButton = new ActionAxisDockButton();

        private void Awake()
        {
            ActionAxisPlugin plugin = ActionAxisPlugin.Instance;
            string configured = plugin != null && plugin.Settings != null
                ? plugin.Settings.DataDir.Value
                : "BazaarActionAxis";
            _dataDir = PathConfig.ResolveDataDir(Paths.PluginPath, configured);
            _icons = new IconCache(Path.Combine(_dataDir, "icons"));
            _panel = new ActionAxisPanel(_icons);
            _panel.OnRefresh = RequestRefresh;
            _panel.OnShuffle = delegate { _dummySeed++; RequestRefresh(); };
            _panel.OnSelectMonster = delegate(int index) { _selectedMonsterIndex = index; RequestRefresh(); };
            _reader = new LiveBoardReader();
            _ready = true;
        }

        private void Update()
        {
            if (!_ready) return;

            if (Time.unscaledTime >= _nextReplayCapture)
            {
                _nextReplayCapture = Time.unscaledTime + 2f;
                NativeCombatReplayData replay;
                string replayError;
                if (NativeCombatReplayReader.TryRead(out replay, out replayError))
                {
                    SaveCombatReplay(replay);
                    if (_panel.IsOpen) _panel.SetCombatReplay(replay, null);
                }
                else if (_panel.IsOpen) _panel.SetCombatReplay(null, replayError);
            }

            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshAndOpen();
            }
            if (!_inputBlocker.SetBlocked(_panel.IsOpen))
            {
                Debug.LogError("[ActionAxis] " + _inputBlocker.Error);
                _panel.Close();
            }
        }

        private void OnGUI()
        {
            if (!_ready) return;
            HandleKeyboardEvent();
            GUI.depth = -1000;
            Theme.Scale = Mathf.Clamp(Mathf.Min(Screen.width / 1240f, Screen.height / 760f), 0.25f, 1.5f);
            Theme.EnsureInit();

            if (_panel.IsOpen)
            {
                _dockButton.SetVisible(false);
                _panel.Draw();
                return;
            }

            DrawLauncher();
        }

        private void HandleKeyboardEvent()
        {
            // The current game enables Input System only. IMGUI keyboard events
            // remain available without invoking the disabled legacy Input API.
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown) return;
            ActionAxisPlugin plugin = ActionAxisPlugin.Instance;
            bool toggle = plugin != null && plugin.Settings != null &&
                MatchesShortcut(plugin.Settings.ToggleKey.Value, current);
            if (!toggle && !(_panel.IsOpen && current.keyCode == KeyCode.Escape)) return;
            if (_panel.IsOpen) { _panel.Close(); _refreshRequested = false; }
            else RequestRefresh();
            current.Use();
        }

        private static bool MatchesShortcut(KeyboardShortcut shortcut, Event current)
        {
            if (shortcut.MainKey == KeyCode.None || current.keyCode != shortcut.MainKey) return false;
            foreach (KeyCode modifier in shortcut.Modifiers)
            {
                if (modifier == KeyCode.LeftControl || modifier == KeyCode.RightControl)
                { if (!current.control) return false; }
                else if (modifier == KeyCode.LeftShift || modifier == KeyCode.RightShift)
                { if (!current.shift) return false; }
                else if (modifier == KeyCode.LeftAlt || modifier == KeyCode.RightAlt)
                { if (!current.alt) return false; }
                else return false;
            }
            return true;
        }

        private void DrawLauncher()
        {
            _dockButton.Draw(RequestRefresh);
        }

        private void RequestRefresh()
        {
            _refreshRequested = true;
        }

        private void RefreshAndOpen()
        {
            List<TileModel> empty = new List<TileModel>();
            _panel.SetData(empty, 30, "读取棋盘…");
            _panel.SetOpponent(null, 100000, null, null);
            _panel.SetSkills(null, null);
            _panel.SetCombatReplay(null, null);
            _panel.Open();
            if (!_inputBlocker.SetBlocked(true))
            {
                Debug.LogError("[ActionAxis] " + _inputBlocker.Error);
                _panel.Close();
                return;
            }

            try
            {
                NativeCombatReplayData combatReplay;
                string replayError;
                if (NativeCombatReplayReader.TryRead(out combatReplay, out replayError))
                {
                    _panel.SetCombatReplay(combatReplay, null);
                    SaveCombatReplay(combatReplay);
                }
                else _panel.SetCombatReplay(null, replayError);

                if (_catalog == null)
                {
                    string cardsPath = Path.Combine(_dataDir, "cards.json");
                    if (!File.Exists(cardsPath))
                    {
                        _panel.SetStatus("找不到卡牌库：" + cardsPath);
                        return;
                    }
                    _catalog = CardCatalog.Load(cardsPath);
                }

                BoardReadResult read = _reader.Read();
                if (!read.Ok)
                {
                    _panel.SetStatus(read.Error);
                    return;
                }

                try
                {
                    Directory.CreateDirectory(_dataDir);
                    File.WriteAllText(Path.Combine(_dataDir, "last_snapshot.json"),
                        LiveSkillReader.BuildDebugJson(read), new System.Text.UTF8Encoding(false));
                }
                catch (Exception snapshotError)
                {
                    Debug.LogWarning("[ActionAxis] snapshot: " + snapshotError.Message);
                }

                ArtworkCaptureResult artwork = null;
                try
                {
                    artwork = NativeArtworkCapture.CaptureMissing(read.Cards, _icons);
                    if (!string.IsNullOrEmpty(artwork.Error))
                        Debug.LogWarning("[ActionAxis] artwork capture: " + artwork.Error);
                }
                catch (Exception captureError)
                {
                    // Art capture never prevents the board or simulation from opening.
                    Debug.LogWarning("[ActionAxis] artwork capture: " + captureError.Message);
                }

                MappedBoard mapped = CardMapper.Build(read.Cards, _catalog,
                    read.PlayerMaxHealth.HasValue && read.PlayerMaxHealth.Value > 0
                        ? read.PlayerMaxHealth.Value : 100,
                    100000, 30);
                mapped.Config.NativeMechanics = true;
                // A fresh dummy projection starts before combat. Current replay
                // HP and temporary Flying/Haste state are not opening state.
                mapped.Config.PlayerStartHp = mapped.Config.PlayerMaxHp;
                if (_skillCatalog == null)
                {
                    string skillsPath = Path.Combine(_dataDir, "skills.json");
                    if (File.Exists(skillsPath))
                    {
                        try { _skillCatalog = SkillCatalog.LoadFile(skillsPath); }
                        catch (Exception catalogError)
                        {
                            Debug.LogWarning("[ActionAxis] skill catalog: " + catalogError.Message);
                        }
                    }
                }
                MonsterReadResult monsters = LiveMonsterReader.ReadChoices();
                LiveMonsterEntry opponent = null;
                MonsterMappingResult monsterMapping = null;
                List<string> choiceNames = new List<string>();
                if (monsters.Choices.Count > 0)
                {
                    if (_monsterChoiceKey != monsters.ChoiceKey)
                    {
                        _monsterChoiceKey = monsters.ChoiceKey;
                        _selectedMonsterIndex = monsters.DefaultIndex;
                    }
                    _selectedMonsterIndex = Math.Max(0, Math.Min(monsters.Choices.Count - 1, _selectedMonsterIndex));
                    opponent = monsters.Choices[_selectedMonsterIndex];
                    mapped.Config.PlayerStartHp = read.PlayerHealth.HasValue
                        ? Math.Max(0, Math.Min(mapped.Config.PlayerMaxHp, read.PlayerHealth.Value))
                        : mapped.Config.PlayerMaxHp;
                    foreach (LiveMonsterEntry choice in monsters.Choices) choiceNames.Add(choice.Name);
                    monsterMapping = MonsterOpponentMapper.Apply(mapped.Config, opponent, _catalog, _skillCatalog);
                }
                else
                {
                    _monsterChoiceKey = null;
                    TrainingDummy.Populate(mapped.Config, _catalog, _dummySeed);
                }
                SkillMappingResult skillMapping = SkillMapper.Apply(mapped.Config, read.Skills, _skillCatalog);
                List<string> nativeRuleWarnings = NativeCardAbilityMapper.Apply(mapped.Config);
                nativeRuleWarnings.AddRange(NativeItemMapper.Apply(mapped.Config));
                if (monsterMapping != null) nativeRuleWarnings.AddRange(monsterMapping.Warnings);
                if (!string.IsNullOrEmpty(monsters.Error)) nativeRuleWarnings.Add(monsters.Error);
                BattleResult result = BattleEngine.Simulate(mapped.Config);
                result.Warnings.AddRange(nativeRuleWarnings);
                List<string> sourceIds = new List<string>();
                foreach (BattleItemView item in result.PlayerItems) sourceIds.Add(item.Id);
                foreach (BattleItemView item in result.EnemyItems) sourceIds.Add(item.Id);
                foreach (SkillMappingStatus skill in skillMapping.Skills)
                    if (!string.IsNullOrEmpty(skill.SourceId)) sourceIds.Add(skill.SourceId);
                if (monsterMapping != null)
                    foreach (SkillMappingStatus skill in monsterMapping.Skills.Skills)
                        if (!string.IsNullOrEmpty(skill.SourceId)) sourceIds.Add(skill.SourceId);
                Dictionary<string, CardStatsSeries> series = Aggregator.Build(result, sourceIds, 0.05);
                BattleDamageSeries damageSeries = Aggregator.BuildDamage(result, mapped.Config.EnemyMaxHp);
                List<SkillRowModel> skillRows = new List<SkillRowModel>();
                foreach (SkillMappingStatus skill in skillMapping.Skills)
                {
                    SkillRowModel row = new SkillRowModel();
                    row.Skill = skill;
                    if (!string.IsNullOrEmpty(skill.SourceId)) series.TryGetValue(skill.SourceId, out row.Series);
                    skillRows.Add(row);
                }
                if (monsterMapping != null)
                    foreach (SkillMappingStatus skill in monsterMapping.Skills.Skills)
                    {
                        SkillRowModel row = new SkillRowModel { Skill = skill, Enemy = true };
                        if (!string.IsNullOrEmpty(skill.SourceId)) series.TryGetValue(skill.SourceId, out row.Series);
                        skillRows.Add(row);
                    }

                List<TileModel> tiles = new List<TileModel>();
                foreach (BattleItemView item in result.PlayerItems)
                {
                    CardStatsSeries stats;
                    series.TryGetValue(item.Id, out stats);
                    string enchantment;
                    mapped.Enchantments.TryGetValue(item.CardId, out enchantment);

                    TileModel tile = new TileModel();
                    tile.Id = item.Id;
                    tile.Card = item.Card;
                    tile.Slot = item.Slot;
                    tile.Enchantment = enchantment;
                    tile.Series = stats;
                    tiles.Add(tile);
                }

                List<TileModel> enemyTiles = new List<TileModel>();
                foreach (BattleItemView item in result.EnemyItems)
                {
                    TileModel tile = new TileModel();
                    tile.Id = item.Id;
                    tile.Card = item.Card;
                    tile.Slot = item.Slot;
                    CardStatsSeries stats;
                    series.TryGetValue(item.Id, out stats);
                    tile.Series = stats;
                    if (monsterMapping != null)
                    {
                        string enchantment;
                        monsterMapping.Board.Enchantments.TryGetValue(item.CardId, out enchantment);
                        tile.Enchantment = enchantment;
                    }
                    enemyTiles.Add(tile);
                }

                string status = "已记录 " + tiles.Count + " 张 · " + (opponent == null ? "30s 木桩推演" :
                    "对战 " + opponent.Name + " · " + opponent.Cards.Count + " 卡 / " + opponent.Skills.Count + " 技能");
                status += " · " + skillMapping.Summary;
                if (!string.IsNullOrEmpty(read.SkillReadError)) status += " · 技能读取不完整";
                if (!string.IsNullOrEmpty(read.CardReadError)) status += " · 卡牌效果读取不完整";
                if (mapped.EnchantmentWarnings.Count > 0)
                    status += " · " + mapped.EnchantmentWarnings.Count + " 条附魔规则待支持";
                if (mapped.LiveAttributeWarnings.Count > 0)
                    status += " · " + mapped.LiveAttributeWarnings.Count + " 条数值映射待支持";
                if (mapped.Unknown.Count > 0)
                    status += " · " + mapped.Unknown.Count + " 张未知";
                if (result.Warnings.Count > 0)
                    status += " · " + result.Warnings.Count + " 条模拟警告";
                if (artwork != null && artwork.Saved > 0)
                    status += " · 新增 " + artwork.Saved + " 张卡图";
                if (artwork != null && artwork.Missing > 0)
                    status += " · " + artwork.Missing + " 张卡图待补全";

                _panel.SetData(tiles, 30, status);
                _panel.SetOpponent(enemyTiles, mapped.Config.EnemyMaxHp,
                    delegate(double time) { return damageSeries.At(time).OutputDamage; },
                    delegate(double time) { return damageSeries.At(time).EffectiveDamage; });
                _panel.SetOpponentSimulation(damageSeries, opponent == null ? "木桩" : opponent.Name,
                    choiceNames, _selectedMonsterIndex, result.EndTime, result.Winner);
                _panel.SetSkills(skillRows, read.SkillReadError);
                SaveSkillReport(read, skillMapping, mapped, result);
                try
                {
                    File.WriteAllText(Path.Combine(_dataDir, "last_monster_choices.json"),
                        LiveSkillReader.BuildDebugJson(new Dictionary<string, object> {
                            { "selectedIndex", _selectedMonsterIndex }, { "read", monsters } }),
                        new System.Text.UTF8Encoding(false));
                    if (monsterMapping != null)
                    {
                        System.Text.StringBuilder report = new System.Text.StringBuilder();
                        report.AppendLine("opponent=" + opponent.Name + "; hp=" + mapped.Config.EnemyMaxHp);
                        foreach (SkillMappingStatus skill in monsterMapping.Skills.Skills)
                            report.AppendLine(skill.Name + " [" + skill.Status + "] " + skill.Reason);
                        foreach (string warning in monsterMapping.Warnings) report.AppendLine(warning);
                        File.WriteAllText(Path.Combine(_dataDir, "last_monster_report.txt"), report.ToString(),
                            new System.Text.UTF8Encoding(false));
                    }
                }
                catch (Exception monsterSaveError) { Debug.LogWarning("[ActionAxis] monster snapshot: " + monsterSaveError.Message); }
                _panel.Open();
            }
            catch (Exception error)
            {
                _panel.SetData(empty, 30, "打开失败：" + error.Message);
                _panel.Open();
            }
        }

        private void SaveSkillReport(BoardReadResult read, SkillMappingResult mapping, MappedBoard mapped, BattleResult result)
        {
            try
            {
                System.Text.StringBuilder report = new System.Text.StringBuilder();
                report.AppendLine("capturedAt=" + DateTime.UtcNow.ToString("o"));
                report.AppendLine("playerHealth=" + read.PlayerHealth + "; playerMaxHealth=" + read.PlayerMaxHealth);
                report.AppendLine("mode=" + (mapped.Config.EnemyIsTrainingDummy ? "training-dummy" : "monster-combat") +
                    "; nativeMechanics=" + mapped.Config.NativeMechanics +
                    "; startHp=" + mapped.Config.PlayerStartHp);
                report.AppendLine(mapping.Summary);
                foreach (SkillMappingStatus skill in mapping.Skills)
                    report.AppendLine(skill.Name + " [" + skill.Status + "] " + skill.Reason);
                foreach (string warning in mapping.Warnings) report.AppendLine("技能警告：" + warning);
                if (!string.IsNullOrEmpty(read.SkillReadError)) report.AppendLine(read.SkillReadError);
                if (!string.IsNullOrEmpty(read.CardReadError)) report.AppendLine(read.CardReadError);
                foreach (string warning in mapped.EnchantmentWarnings) report.AppendLine("附魔警告：" + warning);
                foreach (string warning in mapped.LiveAttributeWarnings) report.AppendLine("实时属性警告：" + warning);
                foreach (string warning in result.Warnings) report.AppendLine("模拟警告：" + warning);
                report.AppendLine("endTime=" + PanelModel.FormatNumber(result.EndTime) +
                    "; winner=" + result.Winner + "; player raw damage=" + PanelModel.FormatNumber(result.Totals.Player.Damage) +
                    "; shield=" + PanelModel.FormatNumber(result.Totals.Player.Shield));
                BattleDamageSeries damage = Aggregator.BuildDamage(result, mapped.Config.EnemyMaxHp);
                report.AppendLine("damageAt0=" + PanelModel.FormatNumber(damage.At(0).Damage) +
                    "; damageAt15=" + PanelModel.FormatNumber(damage.At(15).Damage) +
                    "; damageAt15.3=" + PanelModel.FormatNumber(damage.At(15.3).Damage) +
                    "; damageAt30=" + PanelModel.FormatNumber(damage.At(30).Damage));
                report.AppendLine("outputAt15.3=" + PanelModel.FormatNumber(damage.At(15.3).OutputDamage) +
                    "; dpsAt15.3=" + PanelModel.FormatNumber(damage.DpsAt(15.3)) +
                    "; enemyHpAt15.3=" + PanelModel.FormatNumber(damage.At(15.3).EnemyHp) +
                    "; enemyHealingAt15.3=" + PanelModel.FormatNumber(damage.At(15.3).EnemyHealing));
                File.WriteAllText(Path.Combine(_dataDir, "last_skill_report.txt"), report.ToString(),
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception reportError)
            {
                Debug.LogWarning("[ActionAxis] skill report: " + reportError.Message);
            }
        }

        private void SaveCombatReplay(NativeCombatReplayData replay)
        {
            if (replay == null || (replay.CombatId == _savedReplayId &&
                replay.FrameCount == _savedReplayFrameCount)) return;
            try
            {
                Directory.CreateDirectory(_dataDir);
                replay.SaveJson(Path.Combine(_dataDir, "last_combat_replay.json"));
                string id = replay.CombatId ?? "combat";
                foreach (char invalid in Path.GetInvalidFileNameChars()) id = id.Replace(invalid, '_');
                if (id.Length > 100) id = id.Substring(0, 100);
                string folder = Path.Combine(_dataDir, "combats");
                Directory.CreateDirectory(folder);
                replay.SaveJson(Path.Combine(folder, id + ".json"));
                _savedReplayId = replay.CombatId;
                _savedReplayFrameCount = replay.FrameCount;
                Debug.Log("[ActionAxis] combat recorded: " + id);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[ActionAxis] replay save: " + error.Message);
            }
        }

        private void OnDestroy()
        {
            _dockButton.Dispose();
            _inputBlocker.Dispose();
            if (_icons != null) _icons.Dispose();
            Theme.Dispose();
        }

        private void OnDisable()
        {
            _dockButton.SetVisible(false);
            _inputBlocker.Dispose();
            if (_panel != null) _panel.Close();
        }
    }
}
