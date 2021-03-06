using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MHArmory.Core;
using MHArmory.Core.DataStructures;
using MHArmory.MhwDbDataSource.DataStructures;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace MHArmory.MhwDbDataSource
{
    public class DataSource : IDataSource
    {
        private readonly ILogger logger;
        private readonly bool hasWriteAccess;

        public DataSource(ILogger logger, bool hasWriteAccess)
        {
            this.logger = logger;
            this.hasWriteAccess = hasWriteAccess;
        }

        public async Task<IAbility[]> GetAbilities()
        {
            if (loadTask == null)
                loadTask = LoadData(null);

            await loadTask;

            return abilities;
        }

        public async Task<ISkill[]> GetSkills()
        {
            if (loadTask == null)
                loadTask = LoadData(null);

            await loadTask;

            return skills;
        }

        public async Task<IArmorPiece[]> GetArmorPieces()
        {
            if (loadTask == null)
                loadTask = LoadData(null);

            await loadTask;

            return armors;
        }

        public async Task<ICharm[]> GetCharms()
        {
            if (loadTask == null)
                loadTask = LoadData(null);

            await loadTask;

            return charms;
        }

        public async Task<IJewel[]> GetJewels()
        {
            if (loadTask == null)
                loadTask = LoadData(null);

            await loadTask;

            return jewels;
        }

        private Task loadTask;

        private IAbility[] abilities;
        private ISkill[] skills;
        private IArmorPiece[] armors;
        private ICharm[] charms;
        private IJewel[] jewels;

        public string Description { get; } = "http://mhw-db.com";

        private async Task LoadData(ILogger logger)
        {
            await LoadSkillsData(logger);

            await Task.WhenAll( // <- this must be called after LoadSkillsData
                LoadArmorsData(logger),
                LoadCharmsData(logger),
                LoadJewelsData(logger)
            );
        }

        private async Task<IList<T>> LoadBase<T>(string api, ILogger logger)
        {
            string content;

            var dataAccess = new HttpDataAccess(
                logger,
                hasWriteAccess,
                "mhwdb",
                TimeSpan.FromHours(24.0),
                httpClient => httpClient.GetStringAsync($"https://mhw-db.com/{api}")
            );

            for (int tryCount = 0; tryCount < 2; tryCount++)
            {
                content = await dataAccess.GetRawData(api);

                if (content == null)
                {
                    dataAccess.InvalidateCache(api);
                    continue;
                }

                try
                {
                    return JsonConvert.DeserializeObject<IList<T>>(content);
                }
                catch
                {
                    dataAccess.InvalidateCache(api);
                }
            }

            return null;
        }

        private async Task LoadArmorsData(ILogger logger)
        {
            Task<IList<ArmorPiecePrimitive>> armorTask = LoadBase<ArmorPiecePrimitive>("armor", logger);
            Task<IList<ArmorSetPrimitive>> setsTask = LoadBase<ArmorSetPrimitive>("armor/sets", logger);

            await Task.WhenAll(armorTask, setsTask);

            IList<ArmorPiecePrimitive> armorResult = armorTask.Result;
            IList<ArmorSetPrimitive> setsResult = setsTask.Result;

            if (armorResult == null || setsResult == null)
                return;

            var allArmors = new IArmorPiece[armorResult.Count];

            for (int i = 0; i < allArmors.Length; i++)
            {
                var localAbilities = new IAbility[armorResult[i].Abilities.Count];

                for (int j = 0; j < localAbilities.Length; j++)
                {
                    int skillId = armorResult[i].Abilities[j].SkillId;
                    int abilityLevel = armorResult[i].Abilities[j].Level;

                    ISkill skill = skills.FirstOrDefault(s => s.Id == skillId);
                    IAbility ability = abilities.FirstOrDefault(a => a.Skill.Id == skillId && a.Level == abilityLevel);

                    localAbilities[j] = new Ability(skill, abilityLevel, ability.Description);
                }

                allArmors[i] = new DataStructures.ArmorPiece(armorResult[i], localAbilities);
            }

            foreach (ArmorSetPrimitive armorSetPrimitive in setsResult)
            {
                var setArmorPieces = new IArmorPiece[armorSetPrimitive.ArmorPieces.Length];
                for (int i = 0; i < armorSetPrimitive.ArmorPieces.Length; i++)
                    setArmorPieces[i] = allArmors.FirstOrDefault(x => x.Id == armorSetPrimitive.ArmorPieces[i].ArmorPieceId);

                List<IArmorSetSkill> armorSetSkills = null;

                if (armorSetPrimitive.Bonus != null)
                {
                    armorSetSkills = new List<IArmorSetSkill>();
                    foreach (ArmorSetBonusRankPrimitive bonusRank in armorSetPrimitive.Bonus.Ranks)
                    {
                        IAbility[] setAbilities = abilities.Where(a => a.Skill.Id == bonusRank.Skill.SkillId && a.Level == bonusRank.Skill.Level).ToArray();
                        armorSetSkills.Add(new ArmorSetSkill(bonusRank.PieceCount, setAbilities));
                    }
                }

                var armorSet = new ArmorSet(armorSetPrimitive.Id, false, setArmorPieces, armorSetSkills?.ToArray());

                foreach (DataStructures.ArmorPiece armorPiece in setArmorPieces)
                    armorPiece.UpdateArmorSet(armorSet);
            }

            armors = allArmors;
        }

        private async Task LoadSkillsData(ILogger logger)
        {
            IList<SkillPrimitive> result = await LoadBase<SkillPrimitive>("skills", logger);

            if (result == null)
                return;

            var allAbilities = new HashSet<IAbility>();
            var allSkills = new ISkill[result.Count];
            int localSkillCount = 0;

            foreach (SkillPrimitive skillPrimitive in result)
            {
                var skill = new DataStructures.Skill(skillPrimitive.Id, skillPrimitive.Name, skillPrimitive.Description);
                var localAbilities = new IAbility[skillPrimitive.Abilities.Count];
                int localAbilityCount = 0;

                foreach (AbilityPrimitive abilityPrimitive in skillPrimitive.Abilities)
                {
                    var ability = new Ability(skill, abilityPrimitive.Level, abilityPrimitive.Description);
                    if (allAbilities.Add(ability) == false)
                        logger?.LogError($"Ability identifier 'skill {ability.Skill.Name} level {ability.Level}' is a duplicate");
                    localAbilities[localAbilityCount++] = ability;
                }

                skill.SetAbilities(localAbilities);

                allSkills[localSkillCount++] = skill;
            }

            abilities = allAbilities.ToArray();
            skills = allSkills;
        }

        private async Task LoadCharmsData(ILogger logger)
        {
            IList<CharmPrimitive> result = await LoadBase<CharmPrimitive>("charms", logger);

            if (result == null)
                return;

            var localCharms = new ICharm[result.Count];

            for (int i = 0; i < localCharms.Length; i++)
            {
                CharmPrimitive currentCharmPrimitive = result[i];

                var charmLevels = new DataStructures.CharmLevel[currentCharmPrimitive.Levels.Count];

                for (int j = 0; j < charmLevels.Length; j++)
                {
                    IAbility[] localAbilities = currentCharmPrimitive.Levels[j].Abilitites.Select(x => CreateAbility(x.SkillId, x.Level)).ToArray();
                    charmLevels[j] = new DataStructures.CharmLevel(i + 1, currentCharmPrimitive.Levels[j], localAbilities);
                }

                localCharms[i] = new Charm(i + 1, currentCharmPrimitive.Name, charmLevels);
            }

            charms = localCharms;
        }

        private async Task LoadJewelsData(ILogger logger)
        {
            IList<JewelPrimitive> result = await LoadBase<JewelPrimitive>("decorations", logger);

            if (result == null)
                return;

            var localJewels = new DataStructures.Jewel[result.Count];

            for (int i = 0; i < localJewels.Length; i++)
            {
                IAbility[] localAbilities = result[i].Abilitites.Select(a => CreateAbility(a.SkillId, a.Level)).ToArray();
                localJewels[i] = new DataStructures.Jewel(result[i], localAbilities);
            }

            jewels = localJewels;
        }

        private Ability CreateAbility(int skillId, int level)
        {
            IAbility localAbility = abilities.FirstOrDefault(a => a.Skill.Id == skillId && a.Level == level);
            ISkill localSkill = skills.FirstOrDefault(s => s.Id == skillId);

            return new Ability(localSkill, level, localAbility.Description);
        }

        // ***** Conversion methods *****

        public void ConvertArmorPieces(IEnumerable<IArmorPiece> armorPieces, string outputFilename)
        {
            ConvertMany(armorPieces, outputFilename, ConvertArmorPiece);
        }

        public void ConvertSkills(IEnumerable<ISkill> skills, string outputFilename)
        {
            ConvertMany(skills, outputFilename, ConvertSkill);
        }

        public void ConvertCharms(IEnumerable<ICharm> charms, string outputFilename)
        {
            ConvertMany(charms, outputFilename, ConvertCharm);
        }

        public void ConvertJewels(IEnumerable<IJewel> jewels, string outputFilename)
        {
            ConvertMany(jewels, outputFilename, ConvertJewel);
        }

        // --------------------------------------------------------

        private void ConvertMany<TSource, TTarget>(IEnumerable<TSource> source, string outputFilename, Func<TSource, TTarget> convert)
        {
            IList<TTarget> primitives = source
                .Select(convert)
                .ToList();

            string content = JsonConvert.SerializeObject(primitives, Formatting.Indented);

            File.WriteAllText(outputFilename, content);
        }

        private ArmorPiecePrimitive ConvertArmorPiece(IArmorPiece armorPiece)
        {
            return new ArmorPiecePrimitive
            {
                Abilities = armorPiece.Abilities.Select(ConvertArmorAbility).ToList(),
                Assets = null,
                Attributes = new ArmorPieceAttributesPrimitive
                {
                    RequiredGender = DataStructures.ArmorPieceAttributes.GenderToString(armorPiece.Attributes.RequiredGender)
                },
                Defense = ConvertDefense(armorPiece.Defense),
                Id = armorPiece.Id,
                Name = armorPiece.Name,
                Rarity = armorPiece.Rarity,
                Resistances = ConvertResistances(armorPiece.Resistances),
                Slots = ConvertSlots(armorPiece.Slots),
                Type = armorPiece.Type
            };
        }

        private ArmorAbilityPrimitive ConvertArmorAbility(IAbility ability)
        {
            return new ArmorAbilityPrimitive
            {
                SkillId = ability.Skill.Id,
                Level = ability.Level,
            };
        }

        private ArmorPieceDefensePrimitive ConvertDefense(IArmorPieceDefense defense)
        {
            return new ArmorPieceDefensePrimitive
            {
                Base = defense.Base,
                Max = defense.Max,
                Augmented = defense.Augmented
            };
        }

        private ArmorPieceResistancesPrimitive ConvertResistances(IArmorPieceResistances resistances)
        {
            return new ArmorPieceResistancesPrimitive
            {
                Fire = resistances.Fire,
                Water = resistances.Water,
                Thunder = resistances.Thunder,
                Ice = resistances.Ice,
                Dragon = resistances.Dragon
            };
        }

        private IList<ArmorSlotRank> ConvertSlots(int[] slots)
        {
            return slots
                .Select(x => new ArmorSlotRank { Rank = x })
                .ToList();
        }

        private SkillPrimitive ConvertSkill(ISkill skill)
        {
            return new SkillPrimitive
            {
                Id = skill.Id,
                Name = skill.Name,
                Description = skill.Description,
                Abilities = skill.Abilities.Select(ConvertSkillAbility).ToList()
            };
        }

        private AbilityPrimitive ConvertSkillAbility(IAbility ability)
        {
            return new AbilityPrimitive
            {
                SkillId = ability.Skill.Id,
                Level = ability.Level,
                Description = ability.Description
            };
        }

        private CharmPrimitive ConvertCharm(ICharm charm)
        {
            return new CharmPrimitive
            {
                Name = charm.Name,
                Levels = charm.Levels.Select(ConvertCharmLevel).ToList()
            };
        }

        private CharmLevelPrimitive ConvertCharmLevel(ICharmLevel charmLevel)
        {
            return new CharmLevelPrimitive
            {
                Name = charmLevel.Name,
                Level = charmLevel.Level,
                Rarity = charmLevel.Rarity,
                Abilitites = charmLevel.Abilities.Select(x => new AbilityIdPrimitive { SkillId = x.Skill.Id, Level = x.Level }).ToList()
            };
        }

        private JewelPrimitive ConvertJewel(IJewel jewel)
        {
            return new JewelPrimitive
            {
                Id = jewel.Id,
                Name = jewel.Name,
                Rarity = jewel.Rarity,
                SlotSize = jewel.SlotSize,
                Abilitites = jewel.Abilities.Select(x => new AbilityIdPrimitive { SkillId = x.Skill.Id, Level = x.Level }).ToList()
            };
        }
    }
}
