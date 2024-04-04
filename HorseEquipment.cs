using Facepunch;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Horse Equipment", "VisEntities", "1.0.0")]
    [Description("Automatically equip horses with various types of equipment upon spawning.")]
    public class HorseEquipment : RustPlugin
    {
        #region Fields

        private static HorseEquipment _plugin;
        private static Configuration _config;

        private Coroutine _horseUpdateCoroutine;
        private System.Random _randomGenerator = new System.Random();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Double Saddle Seat Chance")]
            public int DoubleSaddleSeatChance { get; set; }

            [JsonProperty("Single Saddle Seat Chance")]
            public int SingleSaddleSeatChance { get; set; }

            [JsonProperty("Minimum Slots To Equip")]
            public int MinimumSlotsToEquip { get; set; }

            [JsonProperty("Maximum Slots To Equip")]
            public int MaximumSlotsToEquip { get; set; }

            [JsonProperty("Items To Equip")]
            public List<ItemInfo> ItemsToEquip { get; set; }
        }

        public class ItemInfo
        {
            [JsonProperty("Shortname")]
            public string Shortname { get; set; }

            [JsonProperty("Amount")]
            public int Amount { get; set; }

            [JsonIgnore]
            private bool _validated;

            [JsonIgnore]
            private ItemDefinition _itemDefinition;

            [JsonIgnore]
            public ItemDefinition ItemDefinition
            {
                get
                {
                    if (!_validated)
                    {
                        ItemDefinition matchedItemDefinition = ItemManager.FindItemDefinition(Shortname);
                        if (matchedItemDefinition != null)
                            _itemDefinition = matchedItemDefinition;
                        else
                            return null;

                        _validated = true;
                    }

                    return _itemDefinition;
                }
            }

            public int GetItemAmount(ItemContainer container)
            {
                return container.GetAmount(ItemDefinition.itemid, true);
            }

            public void GiveItem(ItemContainer container, BasePlayer player = null)
            {
                container.GiveItem(ItemManager.CreateByItemID(ItemDefinition.itemid, Amount));
                if (player != null)
                    player.Command("note.inv", ItemDefinition.itemid, Amount);
            }

            public int TakeItem(ItemContainer container, BasePlayer player = null)
            {
                int amountTaken = container.Take(null, ItemDefinition.itemid, Amount);
                if (player != null)
                    player.Command("note.inv", ItemDefinition.itemid, -amountTaken);

                return amountTaken;
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DoubleSaddleSeatChance = 50,
                SingleSaddleSeatChance = 50,
                MinimumSlotsToEquip = 1,
                MaximumSlotsToEquip = 4,
                ItemsToEquip = new List<ItemInfo>
                {
                    new ItemInfo
                    {
                        Shortname = "horse.armor.roadsign",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        Shortname = "horse.shoes.advanced",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        Shortname = "horse.saddlebag",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        Shortname = "horse.armor.wood",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        Shortname = "horse.shoes.basic",
                        Amount = 1
                    }
                }
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
        }

        private void OnServerInitialized()
        {
            StartHorseUpdateCoroutine();
        }

        private void Unload()
        {
            StopHorseUpdateCoroutine();
            _config = null;
            _plugin = null;
        }

        private void OnEntitySpawned(RidableHorse horse)
        {
            if (horse == null)
                return;

            NextTick(() =>
            {
                UpdateHorse(horse);
            });
        }

        #endregion Oxide Hooks

        #region Equipment and Seat

        private IEnumerator UpdateAllHorses()
        {
            foreach (RidableHorse horse in BaseNetworkable.serverEntities.OfType<RidableHorse>())
            {
                if (horse != null)
                    UpdateHorse(horse);

                yield return CoroutineEx.waitForSeconds(0.5f);
            }
        }

        private void UpdateHorse(RidableHorse horse)
        {
            if (ChanceSucceeded(_config.DoubleSaddleSeatChance))
            {
                SetSeatCount(horse, numberOfSeats: 2);
            }
            else if (ChanceSucceeded(_config.SingleSaddleSeatChance))
            {
                SetSeatCount(horse, numberOfSeats: 1);
            }

            EquipItems(horse);
        }

        private void SetSeatCount(RidableHorse horse, int numberOfSeats)
        {
            horse.SetFlag(BaseEntity.Flags.Reserved9, false, false, false);
            horse.SetFlag(BaseEntity.Flags.Reserved10, false, false, false);
            if (numberOfSeats == 1)
            {
                horse.SetFlag(BaseEntity.Flags.Reserved9, true, false, false);
            }
            else if (numberOfSeats == 2)
            {
                horse.SetFlag(BaseEntity.Flags.Reserved10, true, false, false);
            }

            horse.UpdateMountFlags();
        }

        private void EquipItems(RidableHorse horse)
        {
            if (horse.equipmentInventory == null)
                return;

            horse.equipmentInventory.Clear();

            List<ItemInfo> uniqueItemsByType = FilterUniqueItemsByType(_config.ItemsToEquip);
            int numberOfSlotsToEquip = Mathf.Clamp(Random.Range(_config.MinimumSlotsToEquip, _config.MaximumSlotsToEquip + 1), 0, 4);

            for (int i = 0; i < numberOfSlotsToEquip; i++)
            {
                ItemInfo itemInfo = uniqueItemsByType[i];
                if (itemInfo.ItemDefinition == null)
                    continue;

                itemInfo.GiveItem(horse.equipmentInventory);
            }

            Pool.FreeList(ref uniqueItemsByType);
        }

        private List<ItemInfo> FilterUniqueItemsByType(List<ItemInfo> itemsToEquip)
        {
            Shuffle(itemsToEquip);
            HashSet<ItemModAnimalEquipment.SlotType> seenSlots = new HashSet<ItemModAnimalEquipment.SlotType>();
            List<ItemInfo> uniqueItems = Pool.GetList<ItemInfo>();

            foreach (var itemInfo in itemsToEquip)
            {
                ItemModAnimalEquipment component = itemInfo.ItemDefinition?.GetComponent<ItemModAnimalEquipment>();
                if (component != null && seenSlots.Add(component.slot))
                {
                    uniqueItems.Add(itemInfo);
                }
            }

            return uniqueItems;
        }

        public bool CanBeEquipped(ItemContainer equipmentInventory, ItemDefinition itemToEquipDefinition)
        {
            ItemModAnimalEquipment component = itemToEquipDefinition.GetComponent<ItemModAnimalEquipment>();
            if (component == null)
                return true;

            foreach (Item slotItem in equipmentInventory.itemList)
            {
                ItemModAnimalEquipment slotComponent = slotItem.info.GetComponent<ItemModAnimalEquipment>();
                if (slotComponent != null && slotComponent.slot == component.slot)
                {
                    return false;
                }
            }
            return true;
        }

        #endregion Equipment and Seat

        #region Coroutine

        private void StartHorseUpdateCoroutine()
        {
            _horseUpdateCoroutine = ServerMgr.Instance.StartCoroutine(UpdateAllHorses());
        }
        
        private void StopHorseUpdateCoroutine()
        {
            if (_horseUpdateCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(_horseUpdateCoroutine);
                _horseUpdateCoroutine = null;
            }
        }

        #endregion Coroutine

        #region Helper Functions

        private static bool ChanceSucceeded(int percentage)
        {
            return Random.Range(0, 100) < percentage;
        }

        private static void Shuffle<T>(List<T> list)
        {
            int remainingItems = list.Count;

            while (remainingItems > 1)
            {
                remainingItems--;
                int randomIndex = _plugin._randomGenerator.Next(remainingItems + 1);

                T itemToSwap = list[randomIndex];
                list[randomIndex] = list[remainingItems];
                list[remainingItems] = itemToSwap;
            }
        }

        #endregion Helper Functions
    }
}