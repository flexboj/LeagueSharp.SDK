﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using LeagueSharp.CommonEx.Core.Enumerations;
using LeagueSharp.CommonEx.Core.Extensions;
using LeagueSharp.CommonEx.Core.Utils;
using Newtonsoft.Json.Linq;

namespace LeagueSharp.CommonEx.Core.Wrappers
{
    /// <summary>
    ///     Calculates damage.
    /// </summary>
    public static class Damage
    {
        /// <summary>
        ///     The DDragon JSON file.
        /// </summary>
        private static JObject _damageFile;

        static Damage()
        {
            // Load JSON file
            _damageFile =
                JObject.Parse(
                    Encoding.UTF8.GetString(
                        Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("LeagueSharp.CommonEx.Damages.json")
                            .GetAllBytes()));
        }

        /// <summary>
        ///     Calculates the "real" damage done to a target.
        /// </summary>
        /// <param name="source"><see cref="Obj_AI_Base" /> where the damage is coming from.</param>
        /// <param name="target"><see cref="Obj_AI_Base" /> where the damage is going to.</param>
        /// <param name="damage">The amount of damage</param>
        /// <param name="damageType">Damage Type</param>
        /// <returns>The real damage done after calculations.</returns>
        public static double CalculateDamage(Obj_AI_Base source,
            Obj_AI_Base target,
            double damage,
            DamageType damageType)
        {
            switch (damageType)
            {
                case DamageType.Physical:
                    return CalculatePhysicalDamage(source, target, damage);
                case DamageType.Magical:
                    return CalculateMagicDamage(source, target, damage);
                case DamageType.True:
                    return damage;
            }
            return 0d;
        }

        private static double CalculateMagicDamage(Obj_AI_Base source, Obj_AI_Base target, double amount)
        {
            var magicResist = target.SpellBlock;

            //Penetration cant reduce magic resist below 0
            double k;
            if (magicResist < 0)
            {
                k = 2 - 100 / (100 - magicResist);
            }
            else if ((target.SpellBlock * source.PercentMagicPenetrationMod) - source.FlatMagicPenetrationMod < 0)
            {
                k = 1;
            }
            else
            {
                k = 100 /
                    (100 + (target.SpellBlock * source.PercentMagicPenetrationMod) - source.FlatMagicPenetrationMod);
            }

            //Take into account the percent passives
            k = PassivePercentMod(source, target, k);

            k = k * (1 - target.PercentMagicReduction) * (1 + target.PercentMagicDamageMod);

            return k * amount;
        }

        private static double CalculatePhysicalDamage(Obj_AI_Base source, Obj_AI_Base target, double amount)
        {
            double armorPenPercent = source.PercentArmorPenetrationMod;
            double armorPenFlat = source.FlatArmorPenetrationMod;

            //Minions return wrong percent values.
            if (source is Obj_AI_Minion)
            {
                armorPenFlat = 0;
                armorPenPercent = 1;
            }

            //Turrets passive.
            if (source is Obj_AI_Turret)
            {
                armorPenFlat = 0;
                armorPenPercent = 0.7f; //Penetrating Bullets passive.
            }

            //Penetration cant reduce armor below 0
            var armor = target.Armor;

            double k;
            if (armor < 0)
            {
                k = 2 - 100 / (100 - armor);
            }
            else if ((target.Armor * armorPenPercent) - armorPenFlat < 0)
            {
                k = 1;
            }
            else
            {
                k = 100 / (100 + (target.Armor * armorPenPercent) - armorPenFlat);
            }

            //Take into account the percent passives
            k = PassivePercentMod(source, target, k);

            return k * amount + PassiveFlatMod(source, target);
        }

        private static double PassiveFlatMod(Obj_AI_Base source, Obj_AI_Base target)
        {
            double d = 0;

            //Offensive masteries:

            //Butcher
            //  Basic attacks and single target abilities do 2 bonus damage to minions and monsters. 
            var hero = source as Obj_AI_Hero;
            if (hero != null && target is Obj_AI_Minion)
            {
                if (hero.Masteries.Any(m => m.Page == MasteryPage.Offense && m.Id == 65 && m.Points == 1))
                {
                    d = d + 2;
                }
            }

            //Defensive masteries:

            //Block
            //Reduces incoming damage from champion basic attacks by 1 / 2
            if (source is Obj_AI_Hero && target is Obj_AI_Hero)
            {
                var mastery =
                    ((Obj_AI_Hero) target).Masteries.FirstOrDefault(m => m.Page == MasteryPage.Defense && m.Id == 65);
                if (mastery != null && mastery.Points >= 1)
                {
                    d = d - 1 * mastery.Points;
                }
            }

            //Tough Skin
            //Reduces damage taken from neutral monsters by 1 / 2
            if (source is Obj_AI_Minion && target is Obj_AI_Hero && source.Team == GameObjectTeam.Neutral)
            {
                var mastery =
                    ((Obj_AI_Hero) target).Masteries.FirstOrDefault(m => m.Page == MasteryPage.Defense && m.Id == 68);
                if (mastery != null && mastery.Points >= 1)
                {
                    d = d - 1 * mastery.Points;
                }
            }

            //Unyielding
            //Melee - Reduces all incoming damage from champions by 2
            //Ranged - Reduces all incoming damage from champions by 1
            if (source is Obj_AI_Hero && target is Obj_AI_Hero)
            {
                var mastery =
                    ((Obj_AI_Hero) target).Masteries.FirstOrDefault(m => m.Page == MasteryPage.Defense && m.Id == 81);
                if (mastery == null || mastery.Points != 1)
                {
                    return d;
                }

                if (source.IsMelee())
                {
                    d = d - 2;
                }
                else
                {
                    d = d - 1;
                }
            }

            return d;
        }

        private static double PassivePercentMod(Obj_AI_Base source, Obj_AI_Base target, double k)
        {
            var siegeMinionList = new List<string>
            {
                "Red_Minion_MechCannon", "Blue_Minion_MechCannon"
            };

            var normalMinionList = new List<string>
            {
                "Red_Minion_Wizard",
                "Blue_Minion_Wizard",
                "Red_Minion_Basic",
                "Blue_Minion_Basic"
            };

            //Minions and towers passives:
            if (source is Obj_AI_Turret)
            {
                //Siege minions receive 70% damage from turrets
                if (siegeMinionList.Contains(target.BaseSkinName))
                {
                    k = 0.7d * k;
                }

                //Normal minions take 114% more damage from towers.
                else if (normalMinionList.Contains(target.BaseSkinName))
                {
                    k = (1 / 0.875) * k;
                }

                // Turrets deal 105% damage to champions for the first attack.
                else if (target is Obj_AI_Hero)
                {
                    k = 1.05 * k;
                }
            }

            var hero = source as Obj_AI_Hero;
            if (hero != null)
            {
                var sourceAsHero = hero;

                //Double edge sword:
                //  Melee champions: You deal 2% increase damage from all sources, but take 1% increase damage from all sources.
                //  Ranged champions: You deal and take 1.5% increased damage from all sources. 
                if (sourceAsHero.Masteries.Any(m => m.Page == MasteryPage.Offense && m.Id == 65 && m.Points == 1))
                {
                    if (sourceAsHero.CombatType == GameObjectCombatType.Melee)
                    {
                        k = k * 1.02d;
                    }
                    else
                    {
                        k = k * 1.015d;
                    }
                }

                //Havoc:
                //  Increases damage by 3%. 
                if (sourceAsHero.Masteries.Any(m => m.Page == MasteryPage.Offense && m.Id == 146 && m.Points == 1))
                {
                    k = k * 1.03d;
                }

                //Executioner
                //  Increases damage dealt to champions below 20 / 35 / 50% by 5%. 
                if (target is Obj_AI_Hero)
                {
                    var mastery =
                        (sourceAsHero).Masteries.FirstOrDefault(m => m.Page == MasteryPage.Offense && m.Id == 100);
                    if (mastery != null && mastery.Points >= 1 &&
                        target.Health / target.MaxHealth <= 0.05d + 0.15d * mastery.Points)
                    {
                        k = k * 1.05;
                    }
                }
            }


            if (!(target is Obj_AI_Hero))
            {
                return k;
            }

            var targetAsHero = (Obj_AI_Hero) target;

            //Double edge sword:
            //     Melee champions: You deal 2% increase damage from all sources, but take 1% increase damage from all sources.
            //     Ranged champions: You deal and take 1.5% increased damage from all sources. 
            if (!targetAsHero.Masteries.Any(m => m.Page == MasteryPage.Offense && m.Id == 65 && m.Points == 1))
            {
                return k;
            }

            if (target.CombatType == GameObjectCombatType.Melee)
            {
                k = k * 1.01d;
            }
            else
            {
                k = k * 1.015d;
            }

            return k;
        }
    }
}