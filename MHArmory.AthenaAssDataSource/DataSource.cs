using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MHArmory.AthenaAssDataSource.DataStructures;
using MHArmory.Core;
using MHArmory.Core.DataStructures;
using MHArmory.Core.ServiceContracts;
using Microsoft.Extensions.Logging;

namespace MHArmory.AthenaAssDataSource
{
    public class DataSource : IDataSource
    {
        private readonly ILogger logger;
        private readonly string dataFolderPath;

        public const string HeadsFilename = "head.txt";
        public const string ChestsFilename = "body.txt";
        public const string GlovesFilename = "arms.txt";
        public const string WaistsFilename = "waist.txt";
        public const string LegsFilename = "legs.txt";
        public const string SkillsFilename = "skills.txt";
        public const string SkillsDescriptionsFilename = "Languages/English/skills.txt";
        public const string AbilitiesDescriptionsFilename = "Languages/English/skill_descriptions.txt";
        public const string CharmsFilename = "charms.txt";
        public const string JewelsFilename = "decorations.txt";
        public const string EventsFilename = "events.txt";
        public const string EventNamesFilename = "Languages/English/events.txt";

        public DataSource(ILogger logger, IDirectoryBrowserService directoryBrowserService, IMessageBoxService messageBoxService)
        {
            this.logger = logger;

            string filename = Path.Combine(AppContext.BaseDirectory, "ass_path.txt");

            if (File.Exists(filename))
            {
                dataFolderPath = File.ReadAllText(filename).Trim();
                if (string.IsNullOrWhiteSpace(dataFolderPath) || Directory.Exists(dataFolderPath) == false)
                    dataFolderPath = null;
            }

            if (dataFolderPath == null && directoryBrowserService != null && messageBoxService != null)
                dataFolderPath = DetermineAssPath(directoryBrowserService, messageBoxService);

            if (dataFolderPath == null)
            {
                if (messageBoxService != null)
                {
                    messageBoxService.Show(new MessageBoxServiceOptions
                    {
                        MessageBoxText = "Data source is missing, the application will exit.",
                        Title = "Application will exit",
                        Buttons = MessageBoxButton.OK,
                        Icon = MessageBoxImage.Error
                    });
                }

                throw new InvalidDataSourceException();
            }

            LoadData();
        }

        private string DetermineAssPath(IDirectoryBrowserService directoryBrowserService, IMessageBoxService messageBoxService)
        {
            string selectedPath;

            bool showIntroduction = true;

            while (true)
            {
                while (true)
                {
                    if (showIntroduction)
                    {
                        showIntroduction = false;

                        MessageBoxResult messageBoxResult = messageBoxService.Show(new MessageBoxServiceOptions
                        {
                            MessageBoxText = "Data from Athena's ASS application is required.\nClick OK to continue providing it, or Cancel to exit the application.",
                            Title = "Data required",
                            Buttons = MessageBoxButton.OKCancel,
                            DefaultResult = MessageBoxResult.Cancel,
                            Icon = MessageBoxImage.Question
                        });

                        if (messageBoxResult != MessageBoxResult.OK)
                            return null;
                    }

                    var directoryOptions = new DirectoryBrowserServiceOptions
                    {
                        Description = "Select the 'Data' directory of the Athena's ASS application",
                        ShowNewFolderButton = false
                    };

                    DialogResult directoryResult = directoryBrowserService.ShowDialog(directoryOptions);

                    if (directoryResult == DialogResult.OK)
                    {
                        selectedPath = directoryOptions.SelectedPath;
                        break;
                    }

                    showIntroduction = true;
                }

                if (File.Exists(Path.Combine(selectedPath, HeadsFilename)))
                    return selectedPath;

                string altPath = Path.Combine(selectedPath, "Data");
                if (File.Exists(Path.Combine(altPath, HeadsFilename)))
                    return altPath;

                messageBoxService.Show(new MessageBoxServiceOptions
                {
                    MessageBoxText = "Selected directory is invalid.",
                    Title = "Invalid data source",
                    Buttons = MessageBoxButton.OK,
                    DefaultResult = MessageBoxResult.OK,
                    Icon = MessageBoxImage.Warning
                });

                showIntroduction = false;
            }
        }

        private void LoadData()
        {
            LoadSkills();
            LoadArmorSetSkills();
            LoadJewels();
            LoadCharms();
            LoadEvents();
            LoadArmorPieces();

            string filename = Path.Combine(AppContext.BaseDirectory, "ass_path.txt");
            File.WriteAllText(filename, dataFolderPath);
        }

        private IList<ArmorSetSkillPrimitive> armorSetSkillPrimitives;

        private void LoadJewels()
        {
            string jewelsFilePath = Path.Combine(dataFolderPath, JewelsFilename);

            string[] allLines = File.ReadAllLines(jewelsFilePath);

            (int headerIndex, int dataIndex) = FindIndexes(allLines);
            if (headerIndex < 0)
            {
                Console.WriteLine($"[ERROR] Failed to create data loader for decorations");
                jewels = Task.FromResult<IJewel[]>(null);
                return;
            }

            var localJewels = new List<IJewel>();
            var dataLoader = new DataLoader<JewelPrimitive>(allLines[headerIndex].Substring(1).Split(','), logger);

            for (int i = dataIndex; i < allLines.Length; i++)
            {
                JewelPrimitive jewelPrimitive = dataLoader.CreateObject(allLines[i].Split(','), i + dataIndex);

                if (string.IsNullOrWhiteSpace(jewelPrimitive.Name))
                    continue;

                IAbility ability = abilities.FirstOrDefault(a => a.Skill.Name == jewelPrimitive.Skill && a.Level == 1);

                if (ability == null)
                    throw new FormatException($"Cannot find skill '{jewelPrimitive.Name}'");

                localJewels.Add(new Jewel(i - dataIndex, jewelPrimitive.Name, jewelPrimitive.Rarity, jewelPrimitive.SlotSize, new IAbility[] { ability }));
            }

            jewels = Task.FromResult(localJewels.ToArray());
        }

        private void LoadCharms()
        {
            string charmsFilePath = Path.Combine(dataFolderPath, CharmsFilename);

            string[] allLines = File.ReadAllLines(charmsFilePath);

            (int headerIndex, int dataIndex) = FindIndexes(allLines);
            if (headerIndex < 0)
            {
                Console.WriteLine($"[ERROR] Failed to create data loader for charms");
                charms = Task.FromResult<ICharm[]>(null);
                return;
            }

            var dataLoader = new DataLoader<CharmPrimitive>(allLines[headerIndex].Substring(1).Split(','), logger);

            var localCharms = new Dictionary<string, List<ICharmLevel>>();
            int[] noSlots = new int[0];

            for (int i = dataIndex; i < allLines.Length; i++)
            {
                CharmPrimitive charmPrimitive = dataLoader.CreateObject(allLines[i].Split(','), i + dataIndex);

                if (string.IsNullOrWhiteSpace(charmPrimitive.Name))
                    continue;

                (string charmName, int level) = DetermineCharmNameAndLevel(charmPrimitive.Name);

                if (localCharms.TryGetValue(charmName, out List<ICharmLevel> charmLevels) == false)
                {
                    charmLevels = new List<ICharmLevel>();
                    localCharms.Add(charmName, charmLevels);
                }

                IEvent foundEvent = CreateEvent(
                    charmPrimitive.Material1,
                    charmPrimitive.Material2,
                    charmPrimitive.Material3,
                    charmPrimitive.Material4
                );

                charmLevels.Add(new CharmLevel(i - dataIndex, level, charmPrimitive.Name, 0, noSlots, ParseAbilities(charmPrimitive), foundEvent));
            }

            ICharm[] nonTaskCharms = localCharms
                .Select((kv, i) => new Charm(i, kv.Key, kv.Value.ToArray()))
                .Cast<ICharm>()
                .ToArray();

            charms = Task.FromResult(nonTaskCharms);
        }

        private (string charmName, int level) DetermineCharmNameAndLevel(string name)
        {
            int index = name.LastIndexOf(' ');

            if (index < 0)
                return (name, 1);

            switch (name.Substring(index + 1))
            {
                case "III": return (name.Substring(0, index), 3);
                case "II": return (name.Substring(0, index), 2);
                case "I": return (name.Substring(0, index), 1);
            }

            return (name, 1);
        }

        private IList<IArmorSetSkill> armorSetSkills = new List<IArmorSetSkill>();

        private void LoadArmorSetSkills()
        {
            string filename = Path.Combine(dataFolderPath, "set_skills.txt");
            armorSetSkillPrimitives = LoadArmorSetSkills(filename).ToList();

            int id = 0;
            int partId = 0;

            IAbility FindAbility(string skillName)
            {
                return abilities.FirstOrDefault(a => a.Skill.Name == skillName && a.Level == 1);
            }

            foreach (IGrouping<string, ArmorSetSkillPrimitive> armorSetSkillGroup in armorSetSkillPrimitives.GroupBy(x => x.Name))
            {
                ArmorSetSkillPart[] armorSetSkillParts = armorSetSkillGroup
                    .Select(x => new ArmorSetSkillPart(partId++, x.PiecesNeeded, new IAbility[] { FindAbility(x.SkillGranted) }))
                    .ToArray();

                armorSetSkills.Add(new ArmorSetSkill(id++, armorSetSkillGroup.Key, armorSetSkillParts));
            }
        }

        private bool ContainsSkillName(ArmorPiecePrimitive primitive, string skillName)
        {
            return
                primitive.Skill1 == skillName ||
                primitive.Skill2 == skillName ||
                primitive.Skill3 == skillName;
        }

        private void LoadSkills()
        {
            string skillsFilePath = Path.Combine(dataFolderPath, SkillsFilename);
            string skillsDescriptionsFilePath = Path.Combine(dataFolderPath, SkillsDescriptionsFilename);
            string abilitiesDescriptionsFilePath = Path.Combine(dataFolderPath, AbilitiesDescriptionsFilename);

            string[] allLines = File.ReadAllLines(skillsFilePath);
            string[] allSkillsDescriptions = File.ReadAllLines(skillsDescriptionsFilePath);
            string[] allAbilitiesDescriptions = File.ReadAllLines(abilitiesDescriptionsFilePath);

            (int headerIndex, int dataIndex) = FindIndexes(allLines);
            if (headerIndex < 0)
            {
                Console.WriteLine($"[ERROR] Failed to create data loader for skills");
                nonTaskSkills = null;
                abilities = null;
                return;
            }

            var dataLoader = new DataLoader<SkillPrimitive>(allLines[headerIndex].Substring(1).Split(','), logger);

            var localSkills = new List<ISkill>();

            for (int i = dataIndex; i < allLines.Length; i++)
            {
                SkillPrimitive skillPrimitive = dataLoader.CreateObject(allLines[i].Split(','), i + dataIndex);

                if (string.IsNullOrWhiteSpace(skillPrimitive.Name))
                    continue;

                int index = i - dataIndex;
                localSkills.Add(new DataStructures.Skill(index, allSkillsDescriptions[index], skillPrimitive));
            }

            abilities = localSkills
                .SelectMany(s => s.Abilities)
                .ForEach((a, i) => ((Ability)a).Update(i, allAbilitiesDescriptions[i]))
                .Distinct(AbilityEqualityComparer.Default)
                .ToArray();

            nonTaskSkills = localSkills.ToArray();
        }

        private readonly List<EventPrimitive> eventPrimitives = new List<EventPrimitive>();

        private void LoadEvents()
        {
            string eventsFilePath = Path.Combine(dataFolderPath, EventsFilename);
            string eventNamesFilePath = Path.Combine(dataFolderPath, EventNamesFilename);

            string[] eventLines = File.ReadAllLines(eventsFilePath);
            string[] eventNameLines = File.ReadAllLines(eventNamesFilePath);

            int count = Math.Min(eventLines.Length, eventNameLines.Length);

            for (int i = 0; i < count; i++)
            {
                var evenPrimitive = new EventPrimitive
                {
                    Id = i,
                    Name = eventNameLines[i],
                    CraftItems = eventLines[i]
                        .Split(',')
                        .Select(x => x.Trim())
                        .ToArray()
                };
                eventPrimitives.Add(evenPrimitive);
            }
        }

        private void LoadArmorPieces()
        {
            string headsFilePath = Path.Combine(dataFolderPath, HeadsFilename);
            string chestsFilePath = Path.Combine(dataFolderPath, ChestsFilename);
            string glovesFilePath = Path.Combine(dataFolderPath, GlovesFilename);
            string waistsFilePath = Path.Combine(dataFolderPath, WaistsFilename);
            string legsFilePath = Path.Combine(dataFolderPath, LegsFilename);

            try
            {
                var heads = LoadArmorPieceParts(EquipmentType.Head, headsFilePath).ToList();
                var chests = LoadArmorPieceParts(EquipmentType.Chest, chestsFilePath).ToList();
                var gloves = LoadArmorPieceParts(EquipmentType.Gloves, glovesFilePath).ToList();
                var waists = LoadArmorPieceParts(EquipmentType.Waist, waistsFilePath).ToList();
                var legs = LoadArmorPieceParts(EquipmentType.Legs, legsFilePath).ToList();

                var allArmorSetContainers = heads
                    .Concat(chests)
                    .Concat(gloves)
                    .Concat(waists)
                    .Concat(legs)
                    .ToList();

                foreach (ArmorPieceContainer container in allArmorSetContainers)
                    container.ArmorPiece = CreateArmorPiece(container);

                UpdateFullArmorSets(allArmorSetContainers);

                IList<ArmorPiece> allPieces = allArmorSetContainers
                    .Select(x => x.ArmorPiece)
                    .ToList();

                armorPiecesTask = Task.FromResult(allPieces.ToArray<IArmorPiece>());
            }
            catch (Exception ex)
            {
                armorPiecesTask = Task.FromResult<IArmorPiece[]>(null);
                Console.WriteLine(ex.ToString());
            }
        }

        private (int headerIndex, int dataIndex) FindIndexes(string[] allLines)
        {
            for (int i = 0; i < allLines.Length; i++)
            {
                if (allLines[i].StartsWith("#"))
                {
                    for (int j = i + 1; j < allLines.Length; j++)
                    {
                        if (allLines[j].StartsWith("#") == false)
                            return (i, j);
                    }
                }
            }

            return (-1, -1);
        }

        private IEvent CreateEvent(string material1, string material2, string material3, string material4)
        {
            IEvent foundEventPrimitive = null;

            foreach (EventPrimitive eventPrimitive in eventPrimitives)
            {
                if (eventPrimitive.CraftItems.Contains(material1) ||
                    eventPrimitive.CraftItems.Contains(material2) ||
                    eventPrimitive.CraftItems.Contains(material3) ||
                    eventPrimitive.CraftItems.Contains(material4))
                {
                    foundEventPrimitive = new Event(eventPrimitive.Id, eventPrimitive.Name);
                    break;
                }
            }

            return foundEventPrimitive;
        }

        private ArmorPiece CreateArmorPiece(ArmorPieceContainer container)
        {
            ArmorPiecePrimitive primitive = container.Primitive;

            IEvent foundEventPrimitive = CreateEvent(
                container.Primitive.Material1,
                container.Primitive.Material2,
                container.Primitive.Material3,
                container.Primitive.Material4
            );

            return new ArmorPiece(
                primitive.Id,
                primitive.Name,
                container.Type,
                primitive.Rarity,
                ParseSlots(primitive.Slots),
                ParseAbilities(primitive),
                ParseArmorSetSkills(primitive),
                new ArmorPieceDefense(primitive.MinDef, primitive.MaxDef, primitive.AugmentedDef),
                new ArmorPieceResistances(primitive.FireRes, primitive.WaterRes, primitive.ThunderRes, primitive.IceRes, primitive.DragonRes),
                CreateArmorPieceAttributes(primitive),
                ArmorPieceAssets.Null,
                null,
                foundEventPrimitive
            );
        }

        private void UpdateFullArmorSets(IList<ArmorPieceContainer> allArmorPieceContainers)
        {
            var list = allArmorPieceContainers
                .Where(x => x.Primitive.Restriction == "Full")
                .ToList();

            int id = 0;

            foreach (ArmorPieceContainer container in list)
            {
                if (container.ArmorPiece.FullArmorSet != null)
                    continue;

                int[] armorPieceIds = list
                    .Where(x => x.Primitive.Id == container.Primitive.Id)
                    .Select(x => x.Primitive.Id)
                    .ToArray();

                if (armorPieceIds.Length != 5)
                {
                    logger?.LogError($"Armor piece '{container.Primitive.Name}' of full armor set seems to bound to more than 5 armor pieces ({armorPieceIds.Length})");
                    continue;
                }

                ArmorPiece[] fullArmorSetArmorPieces = list
                    .Where(x => x.Primitive.Id == container.Primitive.Id)
                    .Select(x => x.ArmorPiece)
                    .ToArray();

                IFullArmorSet fullArmorSet = new FullArmorSet(id++, fullArmorSetArmorPieces);

                foreach (ArmorPiece setPiece in fullArmorSetArmorPieces)
                    setPiece.SetFullArmorSet(fullArmorSet);
            }
        }

        private IArmorPieceAttributes CreateArmorPieceAttributes(ArmorPiecePrimitive primitive)
        {
            switch (primitive.Restriction)
            {
                case "M": return new ArmorPieceAttributes(Gender.Male);
                case "F": return new ArmorPieceAttributes(Gender.Female);
            }
            return new ArmorPieceAttributes(Gender.Both);
        }

        private int[] ParseSlots(string slots)
        {
            if (slots == null || slots.Length == 0 || slots == "0")
                return new int[0];

            return slots.Select(c => c - '0').ToArray();
        }

        private IAbility[] ParseAbilities(ArmorPiecePrimitive primitive)
        {
            var result = new List<IAbility>
            {
                ParseOneAbility(primitive.Skill1, primitive.PointSkill1),
                ParseOneAbility(primitive.Skill2, primitive.PointSkill2),
                ParseOneAbility(primitive.Skill3, primitive.PointSkill3)
            };

            return result.Where(x => x != null).ToArray();
        }

        private IAbility[] ParseAbilities(CharmPrimitive primitive)
        {
            var result = new List<IAbility>
            {
                ParseOneAbility(primitive.Skill1, primitive.Points1),
                ParseOneAbility(primitive.Skill2, primitive.Points2)
            };

            return result.Where(x => x != null).ToArray();
        }

        private IAbility ParseOneAbility(string name, int level)
        {
            if (string.IsNullOrEmpty(name) || level <= 0)
                return null;

            ISkill skill = nonTaskSkills.FirstOrDefault(s => s.Name == name);

            if (skill == null)
                return null;

            return skill.Abilities.FirstOrDefault(a => a.Level == level);
        }

        private IArmorSetSkill[] ParseArmorSetSkills(ArmorPiecePrimitive primitive)
        {
            var result = new List<IArmorSetSkill>
            {
                armorSetSkills.FirstOrDefault(x => x.Name == primitive.Skill1),
                armorSetSkills.FirstOrDefault(x => x.Name == primitive.Skill2),
                armorSetSkills.FirstOrDefault(x => x.Name == primitive.Skill3)
            };

            if (result.Any(x => x != null))
                return result.Where(x => x != null).ToArray();

            return null;
        }

        private IEnumerable<ArmorPieceContainer> LoadArmorPieceParts(EquipmentType type, string filename)
        {
            string[] allLines = File.ReadAllLines(filename);

            (int headerIndex, int dataIndex) = FindIndexes(allLines);
            if (headerIndex < 0)
            {
                Console.WriteLine($"[ERROR] Failed to create data loader for {type}");
                yield break;
            }

            var dataLoader = new DataLoader<ArmorPiecePrimitive>(allLines[headerIndex].Substring(1).Split(','), logger);

            for (int i = dataIndex; i < allLines.Length; i++)
            {
                ArmorPiecePrimitive armorPiecePrimitive = dataLoader.CreateObject(allLines[i].Split(','), i + dataIndex);

                if (string.IsNullOrWhiteSpace(armorPiecePrimitive.Name))
                    continue;

                if (armorPiecePrimitive.Name == "Chainmail Armor")
                    armorPiecePrimitive.Name = "Chainmail Vest";

                armorPiecePrimitive.Id = i - dataIndex;

                var container = new ArmorPieceContainer
                {
                    Primitive = armorPiecePrimitive,
                    Type = type
                };

                yield return container;
            }
        }

        private IEnumerable<ArmorSetSkillPrimitive> LoadArmorSetSkills(string filename)
        {
            string[] allLines = File.ReadAllLines(filename);

            (int headerIndex, int dataIndex) = FindIndexes(allLines);
            if (headerIndex < 0)
            {
                Console.WriteLine($"[ERROR] Failed to create data loader for armor set skills");
                yield break;
            }

            var dataLoader = new DataLoader<ArmorSetSkillPrimitive>(allLines[headerIndex].Substring(1).Split(','), logger);

            for (int i = dataIndex; i < allLines.Length; i++)
            {
                ArmorSetSkillPrimitive armorSetSkill = dataLoader.CreateObject(allLines[i].Split(','), i + dataIndex);

                if (string.IsNullOrWhiteSpace(armorSetSkill.Name))
                    continue;

                armorSetSkill.Id = i - dataIndex;

                yield return armorSetSkill;
            }
        }

        public string Description { get; } = "Athena's ASS";

        private Task<IArmorPiece[]> armorPiecesTask;
        private Task<ICharm[]> charms;
        private Task<IJewel[]> jewels;
        private ISkill[] nonTaskSkills;
        private IAbility[] abilities;

        public Task<IArmorPiece[]> GetArmorPieces()
        {
            if (armorPiecesTask == null)
                return Task.FromResult<IArmorPiece[]>(null);

            return armorPiecesTask;
        }

        public Task<ICharm[]> GetCharms()
        {
            if (charms == null)
                return Task.FromResult<ICharm[]>(null);

            return charms;
        }

        public Task<IJewel[]> GetJewels()
        {
            if (jewels == null)
                return Task.FromResult<IJewel[]>(null);

            return jewels;
        }

        public Task<ISkill[]> GetSkills()
        {
            return Task.FromResult(nonTaskSkills);
        }
    }

    internal static class AbiliyOperators
    {
        public static IEnumerable<IAbility> ForEach(this IEnumerable<IAbility> abilities, Action<IAbility, int> action)
        {
            int index = 0;
            foreach (IAbility ability in abilities)
            {
                action(ability, index);
                index++;
            }

            return abilities;
        }
    }
}
