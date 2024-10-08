/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using System;
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
        private System.Random _randomGenerator = new System.Random();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Chance For Double Saddle Seat")]
            public int ChanceForDoubleSaddleSeat { get; set; }

            [JsonProperty("Chance For Single Saddle Seat")]
            public int ChanceForSingleSaddleSeat { get; set; }

            [JsonProperty("Minimum Equipment Slots To Fill")]
            public int MinimumEquipmentSlotsToFill { get; set; }

            [JsonProperty("Maximum Equipment Slots To Fill")]
            public int MaximumEquipmentSlotsToFill { get; set; }

            [JsonProperty("Items To Equip")]
            public List<ItemInfo> ItemsToEquip { get; set; }
        }

        public class ItemInfo
        {
            [JsonProperty("Short Name")]
            public string ShortName { get; set; }

            [JsonProperty("Amount")]
            public int Amount { get; set; }

            [JsonIgnore]
            private bool _itemHasBeenValidated;

            [JsonIgnore]
            private ItemDefinition _itemDefinition;

            [JsonIgnore]
            public ItemDefinition ItemDefinition
            {
                get
                {
                    if (!_itemHasBeenValidated)
                    {
                        ItemDefinition matchedItemDefinition = ItemManager.FindItemDefinition(ShortName);
                        if (matchedItemDefinition != null)
                            _itemDefinition = matchedItemDefinition;
                        else
                            return null;

                        _itemHasBeenValidated = true;
                    }

                    return _itemDefinition;
                }
            }

            public int GetAmount(PlayerInventory inventory)
            {
                return inventory.GetAmount(ItemDefinition.itemid);
            }

            public void Give(ItemContainer inventory, BasePlayer player = null)
            {
                var item = ItemManager.CreateByItemID(ItemDefinition.itemid, Amount);
                if (item != null)
                {
                    inventory.GiveItem(item);

                    if (player != null)
                        player.Command("note.inv", ItemDefinition.itemid, Amount);
                }
            }

            public int Take(ItemContainer inventory, BasePlayer player = null)
            {
                int amountTaken = inventory.Take(null, ItemDefinition.itemid, Amount);

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
                ChanceForSingleSaddleSeat = 50,
                ChanceForDoubleSaddleSeat = 50,
                MinimumEquipmentSlotsToFill = 1,
                MaximumEquipmentSlotsToFill = 4,
                ItemsToEquip = new List<ItemInfo>
                {
                    new ItemInfo
                    {
                        ShortName = "horse.armor.roadsign",
                        Amount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "horse.shoes.advanced",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        ShortName = "horse.saddlebag",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        ShortName = "horse.armor.wood",
                        Amount = 1
                    },
                    new ItemInfo
                    {
                        ShortName = "horse.shoes.basic",
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
            CoroutineUtil.StartCoroutine(Guid.NewGuid().ToString(), UpdateAllHorsesCoroutine());
        }

        private void Unload()
        {
            CoroutineUtil.StopAllCoroutines();
            _config = null;
            _plugin = null;
        }

        private void OnEntitySpawned(RidableHorse horse)
        {
            if (horse != null)
            {
                NextTick(() =>
                {
                    UpdateHorse(horse);
                });
            }
        }

        #endregion Oxide Hooks

        #region Saddle Setup and Item Equipping

        private IEnumerator UpdateAllHorsesCoroutine()
        {
            foreach (RidableHorse horse in BaseNetworkable.serverEntities.OfType<RidableHorse>())
            {
                if (horse != null)
                    UpdateHorse(horse);

                yield return null;
            }
        }

        private void UpdateHorse(RidableHorse horse)
        {
            if (ChanceSucceeded(_config.ChanceForDoubleSaddleSeat))
            {
                SetSeatCount(horse, numberOfSeats: 2);
            }
            else if (ChanceSucceeded(_config.ChanceForSingleSaddleSeat))
            {
                SetSeatCount(horse, numberOfSeats: 1);
            }

            EquipItems(horse);
        }

        private void EquipItems(RidableHorse horse)
        {
            if (horse.equipmentInventory == null)
                return;

            horse.equipmentInventory.Clear();

            List<ItemInfo> uniqueItemsByType = FilterUniqueItemsByType(_config.ItemsToEquip);
            int numberOfSlotsToEquip = Mathf.Clamp(Random.Range(_config.MinimumEquipmentSlotsToFill,
                Mathf.Min(uniqueItemsByType.Count, _config.MaximumEquipmentSlotsToFill) + 1), 0, 4);

            for (int i = 0; i < numberOfSlotsToEquip; i++)
            {
                ItemInfo itemInfo = uniqueItemsByType[i];
                if (itemInfo.ItemDefinition == null)
                    continue;

                itemInfo.Give(horse.equipmentInventory);
            }

            Pool.FreeUnmanaged(ref uniqueItemsByType);
        }

        private List<ItemInfo> FilterUniqueItemsByType(List<ItemInfo> itemsToEquip)
        {
            Shuffle(itemsToEquip);
            HashSet<ItemModAnimalEquipment.SlotType> seenSlots = new HashSet<ItemModAnimalEquipment.SlotType>();
            List<ItemInfo> uniqueItems = Pool.Get<List<ItemInfo>>();

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

        #endregion Saddle Setup and Item Equipping

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

        #region Helper Classes

        public static class CoroutineUtil
        {
            private static readonly Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

            public static void StartCoroutine(string coroutineName, IEnumerator coroutineFunction)
            {
                StopCoroutine(coroutineName);

                Coroutine coroutine = ServerMgr.Instance.StartCoroutine(coroutineFunction);
                _activeCoroutines[coroutineName] = coroutine;
            }

            public static void StopCoroutine(string coroutineName)
            {
                if (_activeCoroutines.TryGetValue(coroutineName, out Coroutine coroutine))
                {
                    if (coroutine != null)
                        ServerMgr.Instance.StopCoroutine(coroutine);

                    _activeCoroutines.Remove(coroutineName);
                }
            }

            public static void StopAllCoroutines()
            {
                foreach (string coroutineName in _activeCoroutines.Keys.ToArray())
                {
                    StopCoroutine(coroutineName);
                }
            }
        }

        #endregion Helper Classes
    }
}