using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    public sealed class MonsterMappingResult
    {
        public MappedBoard Board;
        public SkillMappingResult Skills;
        public List<string> Warnings = new List<string>();
    }

    public static class MonsterOpponentMapper
    {
        // Assemble one fresh simulation config per selection. Enemy source ids are
        // namespaced, since both players may own the same template and same slot.
        public static MonsterMappingResult Apply(BattleConfig config, LiveMonsterEntry monster,
            CardCatalog catalog, SkillCatalog skills)
        {
            if (config == null || monster == null) throw new ArgumentNullException();
            if (monster.MaxHp <= 0) throw new ArgumentException("怪物生命上限无效");
            MonsterMappingResult result = new MonsterMappingResult();
            result.Board = CardMapper.Build(monster.Cards, catalog, monster.MaxHp, config.PlayerMaxHp, config.MaxTime);
            config.Enemy.Items.Clear();
            foreach (LoadoutItem loadout in result.Board.Config.Player.Items)
            {
                CardDef card = result.Board.Config.Cards[loadout.CardId];
                string oldId = card.Id;
                string enemyId = "enemy:" + oldId;
                card.Id = enemyId;
                RemapTargets(card, oldId, enemyId);
                config.Cards[enemyId] = card;
                config.Enemy.Items.Add(new LoadoutItem { CardId = enemyId, Slot = loadout.Slot });
                string enchantment;
                if (result.Board.Enchantments.TryGetValue(oldId, out enchantment))
                    result.Board.Enchantments[enemyId] = enchantment;
            }
            config.EnemyIsTrainingDummy = false;
            config.EnemyMaxHp = monster.MaxHp;
            config.EnemyStartHp = Math.Max(0, Math.Min(monster.Health, monster.MaxHp));
            ApplyHeroAttributes(config, monster.Attributes, Side.Enemy);
            int value;
            config.EnemyStartShield = monster.Attributes.TryGetValue("Shield", out value) ? Math.Max(0, value) : 0;
            config.EnemyStartRegen = monster.Attributes.TryGetValue("HealthRegen", out value) ? Math.Max(0, value) : 0;
            result.Skills = SkillMapper.Apply(config, monster.Skills, skills, Side.Enemy);
            result.Warnings.AddRange(monster.Warnings);
            result.Warnings.AddRange(result.Board.EnchantmentWarnings);
            result.Warnings.AddRange(result.Board.LiveAttributeWarnings);
            result.Warnings.AddRange(result.Skills.Warnings);
            foreach (string name in result.Board.Unknown)
                result.Warnings.Add("怪物卡牌缺少本地显示定义；尝试使用原生模板：" + name);
            return result;
        }

        // 只取开场所需的属性上限，不带入回放中的临时怒气、燃烧等状态。
        public static void ApplyHeroAttributes(BattleConfig config, IDictionary<string, int> attributes, Side side)
        {
            if (attributes == null) return;
            Dictionary<string, int> snapshot = side == Side.Player ? config.PlayerAttributes : config.EnemyAttributes;
            foreach (KeyValuePair<string, int> pair in attributes) snapshot[pair.Key] = pair.Value;
            int value;
            if (attributes.TryGetValue("RageMax", out value))
            {
                if (side == Side.Player) config.PlayerRageMax = Math.Max(0, value);
                else config.EnemyRageMax = Math.Max(0, value);
            }
            if (attributes.TryGetValue("EnragedDurationMax", out value))
            {
                if (side == Side.Player) config.PlayerEnragedDurationMs = Math.Max(0, value);
                else config.EnemyEnragedDurationMs = Math.Max(0, value);
            }
            if (attributes.TryGetValue("HealthRegen", out value))
            {
                if (side == Side.Player) config.PlayerStartRegen = Math.Max(0, value);
                else config.EnemyStartRegen = Math.Max(0, value);
            }
        }

        private static void RemapTargets(CardDef card, string oldId, string enemyId)
        {
            // Target objects in parsed catalog definitions are shared. Clone only
            // targets that refer to this card rather than changing catalog state.
            card.Effects = RemapEffects(card.Effects, oldId, enemyId);
            List<TriggerDef> triggers = new List<TriggerDef>();
            foreach (TriggerDef original in card.Triggers)
            {
                TriggerDef trigger = new TriggerDef();
                trigger.Event = original.Event;
                trigger.EverySeconds = original.EverySeconds;
                trigger.Filter = original.Filter;
                trigger.SkillRule = original.SkillRule;
                trigger.AttributeFilter = original.AttributeFilter;
                trigger.ChangeType = original.ChangeType;
                trigger.Effects = RemapEffects(original.Effects, oldId, enemyId);
                triggers.Add(trigger);
            }
            card.Triggers = triggers;
        }

        private static List<Effect> RemapEffects(List<Effect> originals, string oldId, string enemyId)
        {
            List<Effect> effects = new List<Effect>();
            foreach (Effect original in originals)
            {
                if (original.Target == null || original.Target.ItemId != oldId)
                {
                    effects.Add(original);
                    continue;
                }
                Target target = original.Target;
                Effect effect = new Effect { Kind = original.Kind, Amount = original.Amount,
                    AmountValue = original.AmountValue, Seconds = original.Seconds, Count = original.Count,
                    DurationValue = original.DurationValue, CountValue = original.CountValue,
                    Attribute = original.Attribute, Operation = original.Operation, Tags = original.Tags,
                    SourceTarget = original.SourceTarget, Effects = original.Effects, Prerequisites = original.Prerequisites,
                    Chance = original.Chance, Percent = original.Percent, Crit = original.Crit,
                    Target = new Target { Kind = target.Kind, ItemId = enemyId,
                        NativeCondition = target.NativeCondition, ExcludeSelf = target.ExcludeSelf,
                        FlyingOnly = target.FlyingOnly, NotFlyingOnly = target.NotFlyingOnly,
                        HasCooldownOnly = target.HasCooldownOnly, AdjacentOnly = target.AdjacentOnly,
                        PositionMode = target.PositionMode, PositionOrigin = target.PositionOrigin, IncludeOrigin = target.IncludeOrigin } };
                effects.Add(effect);
            }
            return effects;
        }
    }
}
