using System;
using System.Collections.Generic;
using BazaarActionAxis.Engine;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    public sealed class SkillRowModel
    {
        public SkillMappingStatus Skill;
        public CardStatsSeries Series;
        public bool Enemy;
    }

    public sealed class ActionAxisPanel
    {
        private readonly IconCache _icons;
        private bool _open;
        private double _playhead;
        private double _endTime = 30;
        private string _status = "";
        private List<TileModel> _tiles = new List<TileModel>();
        private List<SkillRowModel> _skills = new List<SkillRowModel>();
        private Vector2 _skillScroll;
        private string _skillReadError;
        private List<TileModel> _enemyTiles = new List<TileModel>();
        private Func<double, double> _damageAt;
        private Func<double, double> _effectiveDamageAt;
        private double _enemyMaxHp = 100000;
        private NativeCombatReplayData _combatReplay;
        private string _combatReplayError;
        private BattleDamageSeries _damageSeries;
        private string _opponentName = "木桩";
        private readonly List<string> _monsterNames = new List<string>();
        private int _monsterIndex;
        private double _simulationEndTime = 30;
        private string _winner;

        public Action OnRefresh;
        public Action OnShuffle;
        public Action<int> OnSelectMonster;

        public ActionAxisPanel(IconCache icons) { _icons = icons; }
        public bool IsOpen { get { return _open; } }
        public void Open() { _open = true; _playhead = Math.Min(15, _endTime); TimelineWidget.ResetDrag(); }
        public void Close() { _open = false; TimelineWidget.ResetDrag(); }
        public void Toggle() { if (_open) Close(); else Open(); }

        public void SetData(List<TileModel> tiles, double endTime, string status)
        {
            _tiles = tiles ?? new List<TileModel>();
            _tiles.Sort(delegate(TileModel a, TileModel b) { return a.Slot.CompareTo(b.Slot); });
            _endTime = endTime > 0 ? endTime : 30;
            _status = status ?? "";
            _playhead = Math.Min(15, _endTime);
        }

        public void SetOpponent(List<TileModel> tiles, double maxHp,
            Func<double, double> damageAt, Func<double, double> effectiveDamageAt)
        {
            _enemyTiles = tiles ?? new List<TileModel>();
            _enemyTiles.Sort(delegate(TileModel a, TileModel b) { return a.Slot.CompareTo(b.Slot); });
            _enemyMaxHp = maxHp;
            _damageAt = damageAt;
            _effectiveDamageAt = effectiveDamageAt;
            _damageSeries = null;
            _monsterNames.Clear();
            _opponentName = "木桩";
        }

        public void SetOpponentSimulation(BattleDamageSeries damage, string name,
            IList<string> choices, int selected, double endTime, string winner)
        {
            _damageSeries = damage;
            _opponentName = string.IsNullOrEmpty(name) ? "木桩" : name;
            _monsterNames.Clear();
            if (choices != null) _monsterNames.AddRange(choices);
            _monsterIndex = selected;
            _simulationEndTime = endTime;
            _winner = winner;
        }

        public void SetStatus(string status) { _status = status ?? ""; }

        public void SetCombatReplay(NativeCombatReplayData replay, string error)
        {
            _combatReplay = replay;
            _combatReplayError = error;
        }

        public void SetSkills(List<SkillRowModel> skills, string readError)
        {
            _skills = skills ?? new List<SkillRowModel>();
            _skillReadError = readError;
            _skillScroll = Vector2.zero;
        }

        public void Draw()
        {
            if (!_open) return;
            Theme.EnsureInit();
            float s = Theme.Scale;
            float width = 1160f * s;
            float height = 720f * s;
            Rect panel = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height);
            Theme.Frame(panel);
            float left = panel.x + 28f * s;
            float contentWidth = panel.width - 56f * s;

            GUI.Label(new Rect(left, panel.y + 17f * s, 420f * s, 30f * s),
                "行动轴", Theme.TitleStyle);
            GUI.Label(new Rect(left + 88f * s, panel.y + 24f * s, 420f * s, 20f * s),
                _monsterNames.Count > 0 ? "当前棋盘 · 怪物对战推演" : "当前棋盘 · 木桩推演", Theme.SmallStyle);
            if (Theme.Button(new Rect(panel.xMax - 170f * s, panel.y + 18f * s,
                98f * s, 30f * s), "重新记录") && OnRefresh != null)
                OnRefresh();
            if (Theme.Button(new Rect(panel.xMax - 61f * s, panel.y + 18f * s,
                32f * s, 30f * s), "×"))
            {
                Close();
                return;
            }
            GUI.Label(new Rect(left, panel.y + 53f * s, contentWidth, 20f * s),
                _status, Theme.SmallStyle);
            Theme.Fill(new Rect(left, panel.y + 82f * s, contentWidth, s), Theme.FrameDim);

            Rect timeline = new Rect(left, panel.y + 119f * s, contentWidth, 14f * s);
            TimelineWidget.Draw(timeline, _endTime, ref _playhead);

            DrawTotals(new Rect(left, panel.y + 157f * s, contentWidth, 66f * s));
            GUI.Label(new Rect(left, panel.y + 226f * s, contentWidth, 21f * s),
                "我的棋盘 · 推演效果", Theme.LabelStyle);
            DrawBoard(new Rect(left, panel.y + 251f * s, contentWidth, 198f * s), _tiles, false);

            GUI.Label(new Rect(left, panel.y + 466f * s, contentWidth - 120f * s, 21f * s),
                _monsterNames.Count > 0 ? _opponentName + " · 怪物卡组与技能参与对战" :
                    "木桩卡组 · 随机展示，不主动出手", Theme.LabelStyle);
            if (_monsterNames.Count > 1)
                GUI.Label(new Rect(panel.xMax - 242f * s, panel.y + 466f * s, 214f * s, 21f * s),
                    "点击头像切换怪物  " + (_monsterIndex + 1) + " / " + _monsterNames.Count, Theme.SmallStyle);
            else if (_monsterNames.Count == 0 && Theme.Button(new Rect(panel.xMax - 142f * s, panel.y + 460f * s,
                114f * s, 28f * s), "换一套卡牌") && OnShuffle != null)
                OnShuffle();
            DrawBoard(new Rect(left, panel.y + 496f * s, contentWidth, 78f * s), _enemyTiles, true);
            DrawSkills(new Rect(left, panel.y + 578f * s, contentWidth, 99f * s));

            Theme.Fill(new Rect(left, panel.yMax - 32f * s, 10f * s, 10f * s), Theme.WaterLine);
            GUI.Label(new Rect(left + 18f * s, panel.yMax - 37f * s, contentWidth, 23f * s),
                "拖动查看当前时刻的累计效果 · 水位涨满时触发 · F8 / Esc 收起",
                Theme.SmallStyle);
        }

        private void DrawTotals(Rect rect)
        {
            float s = Theme.Scale;
            BattleDamageSnapshot sample = _damageSeries == null ? null : _damageSeries.At(_playhead);
            double damage = sample != null ? sample.OutputDamage : _damageAt == null ? 0 : _damageAt(_playhead);
            double effective = _effectiveDamageAt == null ? 0 : _effectiveDamageAt(_playhead);
            double dps = _playhead > 0 ? damage / _playhead : 0;
            Theme.Fill(rect, Theme.Tile);
            Theme.Outline(rect, Theme.FrameDim, s);
            GUI.Label(new Rect(rect.x + 12f * s, rect.y + 7f * s, 255f * s, 28f * s),
                "累计输出  " + damage.ToString("0.#"), Theme.TimeStyle);
            GUI.Label(new Rect(rect.x + 260f * s, rect.y + 7f * s, 180f * s, 28f * s),
                "DPS  " + dps.ToString("0.#"), Theme.TimeStyle);
            GUI.Label(new Rect(rect.x + 440f * s, rect.y + 9f * s, 310f * s, 24f * s),
                sample == null ? "直伤 / 灼烧 / 中毒" : "直 " + sample.OutputDirectDamage.ToString("0") +
                    " / 烧 " + sample.OutputBurnDamage.ToString("0") + " / 毒 " + sample.OutputPoisonDamage.ToString("0"), Theme.SmallStyle);
            Rect dummy = new Rect(rect.xMax - 300f * s, rect.y + 5f * s, 29f * s, 32f * s);
            Color wood = new Color(0.58f, 0.37f, 0.18f, 1f);
            if (_monsterNames.Count > 0)
            {
                if (Theme.Button(dummy, _monsterNames.Count > 1 ? "↻" : "怪") && _monsterNames.Count > 1 && OnSelectMonster != null)
                    OnSelectMonster((_monsterIndex + 1) % _monsterNames.Count);
            }
            else
            {
                Theme.Fill(new Rect(dummy.x + 10f * s, dummy.y, 10f * s, 9f * s), wood);
                Theme.Fill(new Rect(dummy.x + 13f * s, dummy.y + 10f * s, 4f * s, 22f * s), wood);
                Theme.Fill(new Rect(dummy.x, dummy.y + 13f * s, 29f * s, 5f * s), wood);
            }
            GUI.Label(new Rect(dummy.xMax + 10f * s, rect.y + 7f * s, 250f * s, 27f * s),
                _opponentName + "  " + (sample == null ? Math.Max(0, _enemyMaxHp - effective) : sample.EnemyHp).ToString("0") +
                " / " + _enemyMaxHp.ToString("0"), Theme.LabelStyle);

            string comparison;
            if (_combatReplay != null)
            {
                var actual = _combatReplay.At(_playhead);
                comparison = "实战记录  对手 " + actual.OpponentHp.ToString("0") +
                    " / " + actual.MaxHp.ToString("0") + "  ·  净失血 " + actual.NetHpLoss.ToString("0") +
                    "  ·  承伤 " + (actual.TotalDamageKnown ? actual.TotalDamage.ToString("0") : "未完整读取") +
                    "  ·  回复 " + (actual.HealingKnown ? actual.Healing.ToString("0") : "待核") +
                    "  ·  护盾吸收 " + actual.ShieldAbsorbed.ToString("0");
                if (Math.Abs(actual.OtherHpChange) > 0.01)
                    comparison += "  ·  生命属性变动 " + actual.OtherHpChange.ToString("+0;-0;0");
                if (actual.OverkillDamage > 0)
                    comparison += "  ·  含过量 " + actual.OverkillDamage.ToString("0");
                if (_playhead > _combatReplay.Duration) comparison += "  ·  战斗已结束";
            }
            else comparison = string.IsNullOrEmpty(_combatReplayError) || _combatReplayError == "当前没有实战回放"
                ? "仅统计我方实际命中，治疗不计输出" + (sample == null ? "" :
                    "  ·  对手回复 " + sample.EnemyHealing.ToString("0") + "  ·  护盾 " + sample.EnemyShield.ToString("0")) +
                    (_simulationEndTime < 30 && _playhead >= _simulationEndTime ?
                        "  ·  " + _simulationEndTime.ToString("0.##") + "s " + (_winner == "player" ? "胜利" : _winner == "enemy" ? "落败" : "结束") : "")
                : _combatReplayError;
            GUI.Label(new Rect(rect.x + 12f * s, rect.y + 39f * s, rect.width - 24f * s, 21f * s),
                comparison, Theme.SmallStyle);
        }

        private void DrawBoard(Rect board, List<TileModel> tiles, bool compact)
        {
            float s = Theme.Scale;
            float gap = 7f * s;
            for (int slot = 0; slot < PanelModel.BoardSlots; slot++)
            {
                Rect socket = new Rect(PanelModel.BoardCardX(slot, board.x, board.width, gap),
                    board.y, PanelModel.BoardCardWidth(null, board.width, gap), board.height);
                Theme.Fill(socket, Theme.Track);
                Theme.Outline(socket, Theme.FrameDim, s);
            }

            if (tiles.Count == 0)
            {
                GUI.Label(board, compact ? "正在生成木桩卡组…" : _skills.Count > 0
                    ? "当前棋盘没有物品，已记录技能。"
                    : "进入一局游戏，点击「重新记录」读取当前棋盘。", Theme.PlaceholderStyle);
            }
            else
            {
                foreach (TileModel model in tiles)
                {
                    int span = PanelModel.CardSpan(model.Card);
                    if (model.Slot < 0 || model.Slot + span > PanelModel.BoardSlots) continue;
                    Rect tileRect = new Rect(
                        PanelModel.BoardCardX(model.Slot, board.x, board.width, gap), board.y,
                        PanelModel.BoardCardWidth(model.Card, board.width, gap), board.height);
                    CardStats stats = model.Series != null ? model.Series.At(_playhead) : new CardStats();
                    CardTile.Draw(tileRect, model, stats, _icons, _playhead, compact);
                }
            }

        }

        private void DrawSkills(Rect rect)
        {
            float s = Theme.Scale;
            int enemyCount = _skills.FindAll(delegate(SkillRowModel row) { return row.Enemy; }).Count;
            string title = "技能槽 · 我方 " + (_skills.Count - enemyCount) + (enemyCount > 0 ? " / 怪物 " + enemyCount : " 个技能");
            if (!string.IsNullOrEmpty(_skillReadError)) title += " · 读取不完整";
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 21f * s), title, Theme.LabelStyle);
            Rect viewport = new Rect(rect.x, rect.y + 25f * s, rect.width, rect.height - 25f * s);
            Theme.Fill(viewport, Theme.Track);
            if (_skills.Count == 0)
            {
                GUI.Label(viewport, string.IsNullOrEmpty(_skillReadError)
                    ? "当前没有已获得的技能" : _skillReadError, Theme.SmallStyle);
                return;
            }
            float rowHeight = 24f * s;
            float innerWidth = viewport.width - 22f * s;
            _skillScroll = GUI.BeginScrollView(viewport, _skillScroll,
                new Rect(0, 0, innerWidth, Math.Max(viewport.height, _skills.Count * rowHeight)));
            foreach (SkillRowModel row in _skills)
            {
                int index = _skills.IndexOf(row);
                float y = index * rowHeight;
                if (index % 2 == 0)
                    Theme.Fill(new Rect(0, y, innerWidth, rowHeight), Theme.Tile);
                Theme.Fill(new Rect(0, y + 4f * s, 2f * s, rowHeight - 8f * s),
                    Theme.TierColor(row.Skill.Tier));
                GUI.Label(new Rect(8f * s, y, 230f * s, rowHeight),
                    (row.Enemy ? "敌 · " : "") + row.Skill.Name, Theme.LabelStyle);
                string status = StatusText(row.Skill.Status);
                GUI.Label(new Rect(242f * s, y, 165f * s, rowHeight), status, Theme.SmallStyle);
                string effects = row.Skill.Reason;
                if (row.Skill.RawTooltips.Count > 0 && row.Skill.Status != "unsupported" &&
                    row.Skill.Status != "missing-template")
                    effects = PlainDescription(string.Join(" ", row.Skill.RawTooltips.ToArray()));
                if (row.Series != null)
                {
                    CardStats stats = row.Series.At(_playhead);
                    List<BadgeLine> badges = PanelModel.BadgeLines(stats);
                    List<string> parts = new List<string>();
                    if (stats.ActivateCount > 0) parts.Add("触发 " + stats.ActivateCount + " 次");
                    foreach (BadgeLine badge in badges) parts.Add(badge.Text);
                    if (parts.Count > 0) effects = string.Join(" · ", parts.ToArray());
                }
                GUI.Label(new Rect(410f * s, y, innerWidth - 418f * s, rowHeight),
                    effects ?? "", Theme.SmallStyle);
            }
            GUI.EndScrollView();
        }

        private static string StatusText(string status)
        {
            switch (status)
            {
                case "simulated": return "已纳入战斗计算";
                case "live-applied": return "已含在实时属性中";
                case "inactive": return "当前条件未满足";
                case "out-of-combat": return "战斗外技能";
                case "missing-template": return "缺少技能数据";
                default: return "部分效果暂未支持";
            }
        }

        private static string PlainDescription(string text)
        {
            string plain = System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]*>", "");
            return System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
        }
    }
}
