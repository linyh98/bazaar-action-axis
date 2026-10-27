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
        private readonly List<string> _warnings = new List<string>();
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
        private BattleHealthSeries _healthSeries;
        private Vector2 _enemySkillScroll;
        private Vector2 _playerSkillScroll;
        private Vector2 _enemyRightSkillScroll;
        private Vector2 _playerRightSkillScroll;
        private bool _showBattleDetails;
        private string _hoverText;
        private Func<Texture2D> _playerPortrait;
        private Func<Texture2D> _enemyPortrait;
        private string _playerName = "我方英雄";

        private bool IsMonsterCombat { get { return _monsterNames.Count > 0; } }

        public Action OnRefresh;
        public Action OnShuffle;
        public Action<int> OnSelectMonster;

        public ActionAxisPanel(IconCache icons) { _icons = icons; }
        public bool IsOpen { get { return _open; } }
        public void Open() { _open = true; ResetPlayhead(); }

        private void ResetPlayhead()
        {
            _playhead = PanelModel.DefaultPlayhead(IsMonsterCombat, _endTime, _simulationEndTime);
            // Keep the full dummy simulation available while opening at its midpoint.
            if (!IsMonsterCombat) _playhead = Math.Min(15, _playhead);
            TimelineWidget.ResetDrag();
        }
        public void Close() { _open = false; TimelineWidget.ResetDrag(); }
        public void Toggle() { if (_open) Close(); else Open(); }

        public void SetData(List<TileModel> tiles, double endTime, string status)
        {
            _tiles = tiles ?? new List<TileModel>();
            _tiles.Sort(delegate(TileModel a, TileModel b) { return a.Slot.CompareTo(b.Slot); });
            _endTime = !double.IsNaN(endTime) && !double.IsInfinity(endTime) && endTime >= 0 ? endTime : 30;
            _status = status ?? "";
            _showBattleDetails = false;
            ResetPlayhead();
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
            _healthSeries = null;
            _monsterNames.Clear();
            _opponentName = "木桩";
            _simulationEndTime = 30;
            _winner = null;
        }

        public void SetOpponentSimulation(BattleDamageSeries damage, string name,
            IList<string> choices, int selected, double endTime, string winner, BattleHealthSeries health)
        {
            _damageSeries = damage;
            _opponentName = string.IsNullOrEmpty(name) ? "木桩" : name;
            _monsterNames.Clear();
            if (choices != null) _monsterNames.AddRange(choices);
            _monsterIndex = selected;
            _simulationEndTime = endTime;
            _winner = winner;
            _healthSeries = health;
            ResetPlayhead();
        }

        public void SetWarnings(IEnumerable<string> warnings)
        {
            _warnings.Clear();
            if (warnings == null) return;
            foreach (string warning in warnings)
                if (!string.IsNullOrEmpty(warning) && !_warnings.Contains(warning)) _warnings.Add(warning);
        }

        public void SetStatus(string status) { _status = status ?? ""; }

        public void SetCombatants(string playerName, Func<Texture2D> playerPortrait, Func<Texture2D> enemyPortrait)
        {
            _playerName = string.IsNullOrEmpty(playerName) ? "我方英雄" : playerName;
            _playerPortrait = playerPortrait;
            _enemyPortrait = enemyPortrait;
        }

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
            _enemySkillScroll = new Vector2(-1, 0);
            _playerSkillScroll = new Vector2(-1, 0);
            _enemyRightSkillScroll = Vector2.zero;
            _playerRightSkillScroll = Vector2.zero;
        }

        public void Draw()
        {
            if (!_open) return;
            _hoverText = null;
            Theme.EnsureInit();
            float s = Theme.Scale;
            float width = 1160f * s;
            float height = 784f * s;
            Rect panel = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height);
            Theme.Frame(panel);
            float left = panel.x + 28f * s;
            float contentWidth = panel.width - 56f * s;

            GUI.Label(new Rect(left, panel.y + 17f * s, 420f * s, 30f * s),
                "行动轴", Theme.TitleStyle);
            GUI.Label(new Rect(left + 94f * s, panel.y + 24f * s, 600f * s, 20f * s),
                IsMonsterCombat ? "野怪对战  /  真实阵容 · 最长 30 秒" : "木桩试炼  /  完整 30 秒 · 观察各时段输出", Theme.SmallStyle);
            if (Theme.Button(new Rect(panel.xMax - 170f * s, panel.y + 18f * s,
                98f * s, 30f * s), "重新记录") && OnRefresh != null)
                OnRefresh();
            if (Theme.Button(new Rect(panel.xMax - 61f * s, panel.y + 18f * s,
                32f * s, 30f * s), "×"))
            {
                Close();
                return;
            }
            Theme.Fill(new Rect(left, panel.y + 58f * s, contentWidth, s), Theme.FrameDim);
            if (Theme.Button(new Rect(panel.xMax - 146f * s, panel.y + 66f * s, 118f * s, 28f * s),
                IsMonsterCombat ? "回到结算时刻" : "回到 15 秒")) ResetPlayhead();
            Rect timeline = new Rect(left, panel.y + 103f * s, contentWidth, 12f * s);
            TimelineWidget.Draw(timeline, _endTime, ref _playhead,
                IsMonsterCombat && _healthSeries != null && _healthSeries.EndedByDeath ? (double?)_simulationEndTime : null);
            DrawSummary(new Rect(left, panel.y + 145f * s, contentWidth, 42f * s));
            if (_showBattleDetails)
            {
                DrawTotals(new Rect(left, panel.y + 206f * s, contentWidth, 66f * s));
                DrawSkills(new Rect(left, panel.y + 288f * s, contentWidth, 450f * s));
            }
            else
                DrawBattle(panel, left, contentWidth);

            Theme.Fill(new Rect(left, panel.yMax - 38f * s, contentWidth, s), Theme.FrameDim);
            string details = _showBattleDetails ? "返回对战布局" : "技能与提示" + (_warnings.Count > 0 ? " · " + _warnings.Count : "");
            if (Theme.Button(new Rect(left, panel.yMax - 31f * s, 146f * s, 23f * s), details))
                _showBattleDetails = !_showBattleDetails;
            Rect hint = new Rect(left + 160f * s, panel.yMax - 31f * s, contentWidth - 160f * s, 23f * s);
            GUI.Label(hint, "拖动时间轴回看  ·  悬停查看卡牌与技能  ·  F8 / Esc 收起", Theme.SmallStyle);
            if (hint.Contains(Event.current.mousePosition)) _hoverText = _status;
            DrawHover(panel);
        }

        private void DrawSummary(Rect rect)
        {
            float s = Theme.Scale;
            Theme.Fill(rect, Theme.Tile);
            Theme.Outline(rect, Theme.FrameDim, s);
            if (_damageSeries == null)
            {
                GUIStyle message = new GUIStyle(Theme.LabelStyle) { clipping = TextClipping.Clip };
                GUI.Label(new Rect(rect.x + 14f * s, rect.y + 8f * s, rect.width - 28f * s, rect.height - 16f * s),
                    _status, message);
                if (rect.Contains(Event.current.mousePosition)) _hoverText = _status;
                return;
            }
            BattleDamageSnapshot damage = _damageSeries == null ? null : _damageSeries.At(_playhead);
            string outcome = !IsMonsterCombat ? "30 秒木桩" : _healthSeries == null ? "等待推演" :
                _playhead + 1e-9 < _simulationEndTime ? "战斗中" : _healthSeries.EndedByDeath ?
                    PanelModel.BattleOutcome(true, _winner) : "30s · 尚未分胜负";
            float first = rect.width * 0.28f, second = rect.width * 0.18f, third = rect.width * 0.34f;
            GUI.Label(new Rect(rect.x + 14f * s, rect.y + 9f * s, first, 25f * s),
                "累计输出  " + (damage == null ? "—" : PanelModel.FormatNumber(damage.OutputDamage)), Theme.TimeStyle);
            GUI.Label(new Rect(rect.x + first, rect.y + 9f * s, second, 25f * s),
                "DPS  " + (_damageSeries == null ? "—" : PanelModel.FormatNumber(_damageSeries.DpsAt(_playhead))), Theme.TimeStyle);
            GUI.Label(new Rect(rect.x + first + second, rect.y + 11f * s, third, 22f * s),
                damage == null ? "" : "直伤 " + PanelModel.FormatNumber(damage.OutputDirectDamage) +
                "  /  灼烧 " + PanelModel.FormatNumber(damage.OutputBurnDamage) +
                "  /  中毒 " + PanelModel.FormatNumber(damage.OutputPoisonDamage), Theme.SmallStyle);
            GUI.Label(new Rect(rect.x + first + second + third, rect.y + 9f * s,
                rect.width - first - second - third - 10f * s, 25f * s), outcome, Theme.BadgeStyle);
        }

        private void DrawBattle(Rect panel, float left, float width)
        {
            float s = Theme.Scale;
            BattleHealthSnapshot health = _healthSeries == null ? null : _healthSeries.At(_playhead);
            DrawCombatant(new Rect(left, panel.y + 200f * s, width, 64f * s), true);
            DrawVitals(new Rect(left, panel.y + 274f * s, width, 26f * s),
                IsMonsterCombat ? "敌方生命" : "木桩生命", health == null ? null : health.Enemy, true);
            DrawBoard(new Rect(left, panel.y + 312f * s, width, 146f * s), _enemyTiles, true, true);
            Theme.Fill(new Rect(left, panel.y + 468f * s, width * 0.46f, s), Theme.FrameDim);
            GUI.Label(new Rect(left + width * 0.46f, panel.y + 458f * s, width * 0.08f, 22f * s), "对 战", Theme.CenterSmallStyle);
            Theme.Fill(new Rect(left + width * 0.54f, panel.y + 468f * s, width * 0.46f, s), Theme.FrameDim);
            DrawBoard(new Rect(left, panel.y + 480f * s, width, 146f * s), _tiles, true);
            DrawVitals(new Rect(left, panel.y + 638f * s, width, 26f * s),
                "我方", health == null ? null : health.Player, false);
            DrawCombatant(new Rect(left, panel.y + 676f * s, width, 64f * s), false);
        }

        private void DrawCombatant(Rect rect, bool enemy)
        {
            float s = Theme.Scale;
            Rect portrait = new Rect(rect.x + (rect.width - 64f * s) * 0.5f, rect.y, 64f * s, 64f * s);
            Theme.Fill(portrait, Theme.Track);
            Func<Texture2D> resolve = enemy ? _enemyPortrait : _playerPortrait;
            Texture2D texture = resolve == null ? null : resolve();
            if (texture != null) GUI.DrawTexture(new Rect(portrait.x + 3f * s, portrait.y + 3f * s,
                portrait.width - 6f * s, portrait.height - 6f * s), texture, ScaleMode.ScaleToFit);
            else if (enemy && !IsMonsterCombat) DrawDummyPortrait(portrait);
            else GUI.Label(portrait, enemy ? "敌" : "我", Theme.PlaceholderStyle);
            Theme.Outline(portrait, Theme.Border, s);
            if (portrait.Contains(Event.current.mousePosition))
                _hoverText = enemy ? _opponentName + (IsMonsterCombat ? "\n野怪 " + (_monsterIndex + 1) + " / " + _monsterNames.Count :
                    "\n无限生命木桩\n大、中、小空白卡 · 随机 3–8 秒或无冷却 · 无伤害") : _playerName;
            if (enemy && IsMonsterCombat && _monsterNames.Count > 1)
            {
                Rect cycle = new Rect(portrait.xMax - 30f * s, portrait.y, 30f * s, 30f * s);
                if (Theme.Button(cycle, "↻") && OnSelectMonster != null)
                    OnSelectMonster((_monsterIndex + 1) % _monsterNames.Count);
                if (cycle.Contains(Event.current.mousePosition)) _hoverText = "切换下一只野怪 · " +
                    (_monsterIndex + 1) + " / " + _monsterNames.Count;
            }
            Rect left = new Rect(rect.x, rect.y, portrait.x - rect.x - 18f * s, rect.height);
            Rect right = new Rect(portrait.xMax + 18f * s, rect.y, rect.xMax - portrait.xMax - 18f * s, rect.height);
            if (enemy && !IsMonsterCombat)
            {
                Rect shuffle = new Rect(right.x + 4f * s, right.y + 12f * s, 40f * s, 40f * s);
                if (Theme.Button(shuffle, "↻") && OnShuffle != null)
                    OnShuffle();
                if (shuffle.Contains(Event.current.mousePosition))
                    _hoverText = "更换空白卡组\n随机生成大、中、小空白卡\n3–8 秒冷却或无冷却，均不造成伤害";
            }
            else DrawSideSkills(left, right, enemy);
        }

        private static void DrawDummyPortrait(Rect rect)
        {
            float s = Theme.Scale;
            Color wood = Theme.Border;
            Theme.Fill(new Rect(rect.x + 23f * s, rect.y + 10f * s, 18f * s, 16f * s), wood);
            Theme.Fill(new Rect(rect.x + 29f * s, rect.y + 28f * s, 6f * s, 27f * s), wood);
            Theme.Fill(new Rect(rect.x + 11f * s, rect.y + 33f * s, 42f * s, 5f * s), wood);
            Theme.Fill(new Rect(rect.x + 20f * s, rect.y + 54f * s, 25f * s, 3f * s), wood);
        }

        private void DrawVitals(Rect rect, string name, HeroVitals vitals, bool enemy)
        {
            float s = Theme.Scale;
            Theme.Fill(rect, Theme.Tile);
            Theme.Fill(new Rect(rect.x, rect.y, 3f * s, rect.height), enemy ? Theme.BadgeColor("damage") : Theme.WaterLine);
            GUIStyle clipped = new GUIStyle(Theme.LabelStyle) { clipping = TextClipping.Clip };
            Rect nameRect = new Rect(rect.x + 10f * s, rect.y, 90f * s, rect.height);
            GUI.Label(nameRect, name, clipped);
            if (nameRect.Contains(Event.current.mousePosition)) _hoverText = name;
            float barWidth = rect.width * 0.56f;
            Rect bar = new Rect(rect.x + (rect.width - barWidth) * 0.5f, rect.y + 2f * s, barWidth, rect.height - 4f * s);
            Theme.Fill(bar, new Color(0.14f, 0.04f, 0.04f, 0.8f));
            bool infinite = enemy && !IsMonsterCombat;
            double ratio = infinite ? 1 : vitals == null ? 0 : PanelModel.HealthRatio(vitals.Hp, vitals.MaxHp);
            Theme.Fill(new Rect(bar.x, bar.y, bar.width * (float)ratio, bar.height),
                infinite ? new Color(0.46f, 0.37f, 0.19f, 0.7f) : ratio < 0.25 ? new Color(0.65f, 0.20f, 0.15f) :
                enemy ? new Color(0.60f, 0.22f, 0.20f, 0.9f) : new Color(0.19f, 0.46f, 0.35f, 0.9f));
            for (int tick = 1; tick < 10; tick++)
                Theme.Fill(new Rect(bar.x + bar.width * tick / 10f, bar.y, s, bar.height), new Color(0, 0, 0, 0.18f));
            Theme.Outline(bar, Theme.Border, s);
            GUI.Label(bar, infinite ? "∞  无限生命" : vitals == null ? "生命未读取" : PanelModel.FormatNumber(vitals.Hp) + " / " +
                PanelModel.FormatNumber(vitals.MaxHp) + (vitals.Hp <= 0 ? "  已倒下" : ""), Theme.BadgeStyle);
            if (vitals == null) return;
            string states = "<color=#FFDA59>盾 " + PanelModel.FormatNumber(vitals.Shield) + "</color>" +
                "  <color=#FF9940>烧 " + PanelModel.FormatNumber(vitals.Burn) + "</color>" +
                "  <color=#8CD966>毒 " + PanelModel.FormatNumber(vitals.Poison) + "</color>" +
                "  <color=#80F28C>回复 " + PanelModel.FormatNumber(vitals.Regen) + "</color>";
            Rect status = new Rect(bar.xMax + 12f * s, rect.y, rect.xMax - bar.xMax - 16f * s, rect.height);
            GUI.Label(status, states, clipped);
            if (status.Contains(Event.current.mousePosition)) _hoverText = PlainDescription(states);
        }

        private void DrawSideSkills(Rect left, Rect right, bool enemy)
        {
            List<SkillRowModel> rows = _skills.FindAll(delegate(SkillRowModel row) { return row.Enemy == enemy; });
            List<SkillRowModel> leftRows = new List<SkillRowModel>();
            List<SkillRowModel> rightRows = new List<SkillRowModel>();
            for (int i = 0; i < rows.Count; i++)
                (i % 2 == 0 ? leftRows : rightRows).Add(rows[i]);
            if (enemy)
            {
                DrawSkillWing(left, leftRows, ref _enemySkillScroll, true);
                DrawSkillWing(right, rightRows, ref _enemyRightSkillScroll, false);
            }
            else
            {
                DrawSkillWing(left, leftRows, ref _playerSkillScroll, true);
                DrawSkillWing(right, rightRows, ref _playerRightSkillScroll, false);
            }
        }

        private void DrawSkillWing(Rect rect, List<SkillRowModel> rows, ref Vector2 scroll, bool left)
        {
            if (rows.Count == 0) return;
            float s = Theme.Scale;
            float extent = 60f * s, gap = 8f * s;
            float width = Math.Max(rect.width, rows.Count * (extent + gap) - gap);
            bool overflow = width > rect.width;
            // Keep full-size icons when a wing overflows. The slim scrollbar
            // fits in the gap below the portrait without covering the artwork.
            Rect viewport = new Rect(rect.x, rect.y, rect.width, rect.height + (overflow ? 4f * s : 0));
            float height = overflow ? viewport.height - Theme.SkillScrollbarStyle.fixedHeight - 2f * s : rect.height;
            if (scroll.x < 0) scroll.x = left ? width - rect.width : 0;
            bool hovered = new Rect(rect.x, rect.y, rect.width, height).Contains(Event.current.mousePosition);
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, width, height),
                Theme.SkillScrollbarStyle, GUIStyle.none);
            for (int i = 0; i < rows.Count; i++)
            {
                SkillRowModel row = rows[i];
                if (row == null || row.Skill == null) continue;
                Rect iconRect = new Rect(left ? width - extent - i * (extent + gap) : i * (extent + gap),
                    (height - extent) * 0.5f, extent, extent);
                bool hot = hovered && iconRect.Contains(Event.current.mousePosition);
                Texture2D icon = SkillIconResolver.Get(row.Skill.TemplateId, row.Skill.Tier, row.Skill.Name, _icons);
                if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
                Texture2D frame = _icons == null ? null : _icons.Get("skill-frame-" + (row.Skill.Tier ?? "Bronze").ToLowerInvariant());
                Theme.SkillFrame(iconRect, hot ? Theme.Border : Theme.TierColor(row.Skill.Tier), frame);
                if (hot)
                {
                    string warning = row.Skill.Status == "unsupported" || row.Skill.Status == "missing-template"
                        ? SkillStatus(row) + "\n" : "";
                    _hoverText = row.Skill.Name + "\n" + warning + SkillEffects(row);
                }
            }
            GUI.EndScrollView();
        }

        private void DrawHover(Rect panel)
        {
            if (string.IsNullOrEmpty(_hoverText)) return;
            float s = Theme.Scale;
            GUIStyle style = new GUIStyle(Theme.LabelStyle) { wordWrap = true, clipping = TextClipping.Clip };
            float width = Math.Min(440f * s, panel.width - 24f * s);
            float height = Math.Min(240f * s, style.CalcHeight(new GUIContent(_hoverText), width - 20f * s) + 20f * s);
            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Clamp(mouse.x + 14f * s, panel.x + 8f * s, panel.xMax - width - 8f * s);
            float y = mouse.y + 20f * s;
            if (y + height > panel.yMax - 8f * s) y = mouse.y - height - 8f * s;
            Rect tooltip = new Rect(x, Math.Max(panel.y + 8f * s, y), width, height);
            Theme.Frame(tooltip);
            GUI.Label(new Rect(tooltip.x + 10f * s, tooltip.y + 10f * s, width - 20f * s, height - 20f * s), _hoverText, style);
        }

        private static string SkillStatus(SkillRowModel row)
        {
            return row.Skill.NativeTierHasNoEffects ? "当前品质无效果" : StatusText(row.Skill.Status);
        }

        private string SkillEffects(SkillRowModel row)
        {
            string effects = row.Skill.Reason;
            if (!row.Skill.NativeTierHasNoEffects && row.Skill.RawTooltips.Count > 0 && row.Skill.Status != "unsupported" &&
                row.Skill.Status != "missing-template")
                effects = PlainDescription(string.Join(" ", row.Skill.RawTooltips.ToArray()));
            if (row.Series != null)
            {
                CardStats stats = row.Series.At(_playhead);
                List<string> parts = new List<string>();
                if (stats.ActivateCount > 0) parts.Add("触发 " + stats.ActivateCount + " 次");
                foreach (BadgeLine badge in PanelModel.BadgeLines(stats)) parts.Add(badge.Text);
                if (parts.Count > 0) effects += "\n当前累计：" + string.Join(" · ", parts.ToArray());
            }
            return effects ?? "";
        }

        private void DrawTotals(Rect rect)
        {
            float s = Theme.Scale;
            BattleDamageSnapshot sample = _damageSeries == null ? null : _damageSeries.At(_playhead);
            double damage = sample != null ? sample.OutputDamage : _damageAt == null ? 0 : _damageAt(_playhead);
            double effective = _effectiveDamageAt == null ? 0 : _effectiveDamageAt(_playhead);
            double dps = _damageSeries != null ? _damageSeries.DpsAt(_playhead) :
                _playhead > 0 ? damage / _playhead : 0;
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
                IsMonsterCombat ? _opponentName + "  " + (sample == null ? Math.Max(0, _enemyMaxHp - effective) : sample.EnemyHp).ToString("0") +
                " / " + _enemyMaxHp.ToString("0") : "木桩  ∞ 无限生命", Theme.LabelStyle);

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

        private void DrawBoard(Rect board, List<TileModel> tiles, bool compact, bool enemy = false)
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
                GUI.Label(board, enemy ? IsMonsterCombat ? "敌方没有在场卡牌" : "暂无木桩卡牌" :
                    _skills.Count > 0 ? "我方没有物品，已记录技能" : "我方棋盘为空 · 点击「重新记录」读取当前棋盘", Theme.PlaceholderStyle);
            }
            else
            {
                foreach (TileModel entry in tiles)
                {
                    TileModel model = entry.At(_playhead);
                    if (model == null) continue;
                    int span = PanelModel.CardSpan(model.Card);
                    if (model.Slot < 0 || model.Slot + span > PanelModel.BoardSlots) continue;
                    Rect tileRect = new Rect(
                        PanelModel.BoardCardX(model.Slot, board.x, board.width, gap), board.y,
                        PanelModel.BoardCardWidth(model.Card, board.width, gap), board.height);
                    CardStats stats = model.Series != null ? model.Series.At(_playhead) : new CardStats();
                    CardTile.Draw(tileRect, model, stats, _icons, _playhead, compact);
                    if (tileRect.Contains(Event.current.mousePosition))
                    {
                        List<string> lines = new List<string>();
                        lines.Add(model.Card.DisplayName() + " · " + PanelModel.TriggerText(stats));
                        foreach (BadgeLine badge in PanelModel.BadgeLines(stats)) lines.Add(badge.Text);
                        lines.Add(model.Card.Cooldown > 0 ? "基础冷却 " + model.Card.Cooldown.ToString("0.##") + "s" : "无冷却");
                        if (model.Card.RawTooltips != null && model.Card.RawTooltips.Count > 0)
                            lines.Add(PlainDescription(string.Join("\n", model.Card.RawTooltips.ToArray())));
                        _hoverText = string.Join("\n", lines.ToArray());
                    }
                }
            }

        }

        private void DrawSkills(Rect rect)
        {
            float s = Theme.Scale;
            int enemyCount = _skills.FindAll(delegate(SkillRowModel row) { return row.Enemy; }).Count;
            string title = "技能槽 · 我方 " + (_skills.Count - enemyCount) + (enemyCount > 0 ? " / 怪物 " + enemyCount : " 个技能");
            if (!string.IsNullOrEmpty(_skillReadError)) title += " · 读取不完整";
            if (_warnings.Count > 0) title += " · " + _warnings.Count + " 条模拟提示（向下滚动查看）";
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 21f * s), title, Theme.LabelStyle);
            Rect viewport = new Rect(rect.x, rect.y + 25f * s, rect.width, rect.height - 25f * s);
            Theme.Fill(viewport, Theme.Track);
            if (_skills.Count == 0 && _warnings.Count == 0)
            {
                GUI.Label(viewport, string.IsNullOrEmpty(_skillReadError)
                    ? "当前没有已获得的技能" : _skillReadError, Theme.SmallStyle);
                return;
            }
            float rowHeight = 24f * s;
            float innerWidth = viewport.width - 22f * s;
            GUIStyle warningStyle = new GUIStyle(Theme.SmallStyle);
            warningStyle.wordWrap = true;
            warningStyle.normal.textColor = new Color(0.92f, 0.73f, 0.43f);
            List<float> warningHeights = new List<float>();
            float totalHeight = _skills.Count * rowHeight;
            foreach (string warning in _warnings)
            {
                float height = Math.Max(rowHeight, warningStyle.CalcHeight(new GUIContent("提示 · " + warning), innerWidth - 16f * s) + 8f * s);
                warningHeights.Add(height);
                totalHeight += height;
            }
            _skillScroll = GUI.BeginScrollView(viewport, _skillScroll,
                new Rect(0, 0, innerWidth, Math.Max(viewport.height, totalHeight)));
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
                string status = row.Skill.NativeTierHasNoEffects ? "当前品质无效果" : StatusText(row.Skill.Status);
                GUI.Label(new Rect(242f * s, y, 165f * s, rowHeight), status, Theme.SmallStyle);
                string effects = row.Skill.Reason;
                if (!row.Skill.NativeTierHasNoEffects && row.Skill.RawTooltips.Count > 0 && row.Skill.Status != "unsupported" &&
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
            float warningY = _skills.Count * rowHeight;
            for (int index = 0; index < _warnings.Count; index++)
            {
                GUI.Label(new Rect(8f * s, warningY + 4f * s, innerWidth - 16f * s, warningHeights[index] - 8f * s),
                    "提示 · " + _warnings[index], warningStyle);
                warningY += warningHeights[index];
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
