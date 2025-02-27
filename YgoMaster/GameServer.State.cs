﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace YgoMaster
{
    partial class GameServer
    {
        static readonly Version highestSupportedClientVersion = new Version(int.MaxValue, int.MaxValue);

        static readonly string deckSearchUrl = "https://ayk-deck.mo.konami.net/ayk/yocgapi/search";
        static readonly string deckSearchDetailUrl = "https://ayk-deck.mo.konami.net/ayk/yocgapi/detail";
        static readonly string deckSearchAttributesUrl = "https://ayk-deck.mo.konami.net/ayk/yocgapi/attributes";

        static readonly string serverUrl = "http://localhost/ygo";
        static readonly string serverPollUrl = "http://localhost/ygo/poll";

        static string dataDirectory;
        static string decksDirectory;
        static string playerSettingsFile;
        static string settingsFile;

        Random rand = new Random();
        Player thePlayer;

        FileSystemWatcher decksFileWatcher;
        Thread decksFileWatcherUpdateThread;
        HashSet<string> decksFileWatcherDecksToUpdate;
        DateTime decksFileWatcherLastUpdate;

        int NumDeckSlots;
        bool UnlockAllCards;
        bool UnlockAllItems;
        bool UnlockAllSoloChapters;
        bool SoloRemoveDuelTutorials;
        bool SoloDisableNoShuffle;
        HashSet<int> DefaultItems;
        int DefaultGems;
        CraftInfo Craft;
        ShopInfo Shop;
        Dictionary<int, DeckInfo> StructureDecks;// <structureid, DeckInfo>
        Dictionary<int, int> CardRare;
        List<int> CardCraftable;
        Dictionary<string, object> Regulation;
        Dictionary<string, object> SoloData;
        Dictionary<int, DuelSettings> SoloDuels;// <chapterid, DuelSettings>
        Dictionary<int, CardCategory> CardCategories;// <categoryId, CardCategory>
        Dictionary<string, CardCategory> CardCategoriesByName;// <categoryName, CardCategory>
        DuelSettings CustomDuelSettings;
        DateTime CustomDuelLastModified;
        DuelRewardInfos DuelRewards;
        /// <summary>
        /// Cards will only be visible in the trunk if they are obtainable via the shop
        /// </summary>
        bool ProgressiveCardList;
        /// <summary>
        /// Card rarities will be determined based on the following criteria:
        /// - Find the lowest rarity of the given card in all unlocked packs
        /// - If the lowest found rarity is higher than the main rarity list then the pack rarity will be used, else use the main rarity
        /// </summary>
        bool ProgressiveCardRarities;
        /// <summary>
        /// In solo mode show "gate clear" in order to display the secret pack unlock for chapters which aren't flagged as the goal
        /// </summary>
        bool SoloShowGateClearForAllSecretPacks;
        /// <summary>
        /// Put all solo rewards in the duel result screen (as opposed to the reward dialog popup)
        /// </summary>
        bool SoloRewardsInDuelResult;
        /// <summary>
        /// Solo rewards in the duel result screen are flagged as rare (gold box)
        /// </summary>
        bool SoloRewardsInDuelResultAreRare;

        void LoadSettings()
        {
            try
            {
                string overrideDataDirFile = "DataDirServer.txt";
                if (!File.Exists(overrideDataDirFile))
                {
                    overrideDataDirFile = "DataDir.txt";
                }
                if (File.Exists(overrideDataDirFile))
                {
                    string newDir = File.ReadAllLines(overrideDataDirFile)[0].Trim();
                    if (Directory.Exists(newDir))
                    {
                        dataDirectory = newDir;
                    }
                }
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(dataDirectory))
            {
                dataDirectory = "Data";
            }
            if (!Directory.Exists(dataDirectory))
            {
                LogWarning("Failed to find data directory '" + dataDirectory + "'");
                return;
            }
            settingsFile = Path.Combine(dataDirectory, "Settings.json");
            playerSettingsFile = Path.Combine(dataDirectory, "Player.json");
            decksDirectory = Path.Combine(dataDirectory, "Decks");
            TryCreateDirectory(decksDirectory);

            if (!File.Exists(settingsFile))
            {
                LogWarning("Failed to load settings file");
                return;
            }

            InitDecksWatcher();
            YdkHelper.LoadIdMap(dataDirectory);

            string text = File.ReadAllText(settingsFile);
            Dictionary<string, object> values = MiniJSON.Json.DeserializeStripped(text) as Dictionary<string, object>;
            if (values == null)
            {
                throw new Exception("Failed to parse settings json");
            }

            NumDeckSlots = GetValue<int>(values, "DeckSlots", 20);
            GetIntHashSet(values, "DefaultItems", DefaultItems = new HashSet<int>(), ignoreZero: true);
            DefaultGems = GetValue<int>(values, "DefaultGems");
            UnlockAllCards = GetValue<bool>(values, "UnlockAllCards");
            UnlockAllItems = GetValue<bool>(values, "UnlockAllItems");
            UnlockAllSoloChapters = GetValue<bool>(values, "UnlockAllSoloChapters");
            SoloRemoveDuelTutorials = GetValue<bool>(values, "SoloRemoveDuelTutorials");
            SoloDisableNoShuffle = GetValue<bool>(values, "SoloDisableNoShuffle");
            SoloShowGateClearForAllSecretPacks = GetValue<bool>(values, "SoloShowGateClearForAllSecretPacks");
            SoloRewardsInDuelResult = GetValue<bool>(values, "SoloRewardsInDuelResult");
            SoloRewardsInDuelResultAreRare = GetValue<bool>(values, "SoloRewardsInDuelResultAreRare");
            ProgressiveCardList = GetValue<bool>(values, "ProgressiveCardList");
            ProgressiveCardRarities = GetValue<bool>(values, "ProgressiveCardRarities");

            CardRare = new Dictionary<int, int>();
            string cardListFile = Path.Combine(dataDirectory, "CardList.json");
            if (File.Exists(cardListFile))
            {
                Dictionary<string, object> cardRareDict = MiniJSON.Json.DeserializeStripped(File.ReadAllText(cardListFile)) as Dictionary<string, object>;
                if (cardRareDict != null)
                {
                    foreach (KeyValuePair<string, object> item in cardRareDict)
                    {
                        CardRare[int.Parse(item.Key)] = (int)Convert.ChangeType(item.Value, typeof(int));
                    }
                }
            }

            CardCraftable = new List<int>();
            string cardCraftableListFile = Path.Combine(dataDirectory, "CardCraftableList.json");
            if (File.Exists(cardCraftableListFile))
            {
                List<object> cardCrList = MiniJSON.Json.DeserializeStripped(File.ReadAllText(cardCraftableListFile)) as List<object>;
                if (cardCrList != null)
                {
                    foreach (object id in cardCrList)
                    {
                        CardCraftable.Add((int)Convert.ChangeType(id, typeof(int)));
                    }
                }
            }
            if (GetValue<bool>(values, "CardCratableAll"))
            {
                CardCraftable.Clear();
                foreach (int cardId in CardRare.Keys)
                {
                    CardCraftable.Add(cardId);
                }
            }

            string cardBanListFile = Path.Combine(dataDirectory, "CardBanList.json");
            if (File.Exists(cardBanListFile))
            {
                Regulation = MiniJSON.Json.DeserializeStripped(File.ReadAllText(cardBanListFile)) as Dictionary<string, object>;
            }

            Craft = new CraftInfo();
            Craft.FromDictionary(GetValue(values, "Craft", default(Dictionary<string, object>)));

            LoadStructureDecks();
            LoadCardCategory();
            LoadShop();
            LoadSolo();

            DuelRewards = new DuelRewardInfos();
            DuelRewards.FromDictionary(GetDictionary(values, "DuelRewards"));

            // TODO: Move elsewhere (these are helpers to generate files)
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 0)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    bool log = true;
                    string arg = args[i].ToLowerInvariant();
                    switch (arg)
                    {
                        case "--mergeshops":// Gets all logs from "/ShopDumps/" and merges them into "AllShopsMerged.json"
                            MergeShopDumps();
                            break;
                        case "--shopsfromsets":// Scrapes ygo db for sets and creates shops from them based on "OfficialSetsExtraData.json"
                            CreateShopsFromOfficialSets();
                            break;
                        case "--le2master":
                            ConvertLeDataToSolo();
                            break;
                        case "--extractstructure":// Takes "StructureDecks.json" (obtained via "Master.Structure") and expands them into "/StructureDecks/"
                            ExtractStructureDecks();
                            break;
                        case "--updateydk":// Updates "YdkIds.txt" based on a YgoMasterClient "carddata" dump and ygoprodeck cardinfo.php
                            YdkHelper.GenerateIdMap(dataDirectory);
                            break;
                        default:
                            log = false;
                            break;
                    }
                    if (!log)
                    {
                        Console.WriteLine("Done (" + arg + ")");
                    }
                }
            }
        }

        void InitDecksWatcher()
        {
            decksFileWatcherDecksToUpdate = new HashSet<string>();
            decksFileWatcherUpdateThread = new Thread(delegate()
            {
                while (true)
                {
                    lock (decksFileWatcherDecksToUpdate)
                    {
                        if (decksFileWatcherDecksToUpdate.Count > 0 && decksFileWatcherLastUpdate <= DateTime.Now - TimeSpan.FromSeconds(1))
                        {
                            if (thePlayer != null)
                            {
                                Dictionary<string, DeckInfo> fullPathDecks = new Dictionary<string, DeckInfo>();
                                foreach (DeckInfo deckInfo in thePlayer.Decks.Values)
                                {
                                    try
                                    {
                                        if (!string.IsNullOrEmpty(deckInfo.File))
                                        {
                                            fullPathDecks[Path.GetFullPath(deckInfo.File).ToLowerInvariant()] = deckInfo;
                                        }
                                    }
                                    catch
                                    {
                                    }
                                }
                                foreach (string path in decksFileWatcherDecksToUpdate)
                                {
                                    try
                                    {
                                        if (File.Exists(path))
                                        {
                                            DeckInfo deck;
                                            if (!fullPathDecks.TryGetValue(path.ToLowerInvariant(), out deck))
                                            {
                                                //Console.WriteLine("Deck added");
                                                LoadDeck(thePlayer, path);
                                            }
                                        }
                                        else
                                        {
                                            DeckInfo deck;
                                            if (fullPathDecks.TryGetValue(path.ToLowerInvariant(), out deck))
                                            {
                                                //Console.WriteLine("Deck removed");
                                                thePlayer.Decks.Remove(deck.Id);
                                            }
                                        }
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                            decksFileWatcherDecksToUpdate.Clear();
                        }
                    }
                    Thread.Sleep(1000);
                }
            });
            decksFileWatcherUpdateThread.Start();
            decksFileWatcher = new FileSystemWatcher(decksDirectory);
            decksFileWatcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite;
            FileSystemEventHandler decksFileWatcherUpdateEvent = (object sender, FileSystemEventArgs e) =>
            {
                lock (decksFileWatcherDecksToUpdate)
                {
                    decksFileWatcherLastUpdate = DateTime.Now;
                    decksFileWatcherDecksToUpdate.Add(Path.GetFullPath(e.FullPath));
                }
            };
            decksFileWatcher.Created += decksFileWatcherUpdateEvent;
            decksFileWatcher.Deleted += decksFileWatcherUpdateEvent;
            decksFileWatcher.Renamed += (object sender, RenamedEventArgs e) =>
                {
                    // TODO: Renaming files currently isn't handled and we lose track of the file when a rename occurs
                };
            decksFileWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Returns all card rarities which should be visible to the game
        /// - When using a progressive card list this will expand over time
        /// - When using progressive card rarities the rarity of any given card can change over time
        /// - Also used to get the card rarities for a specific pack in a shop (which can use specific rarities for that pack)
        /// </summary>
        Dictionary<int, int> GetCardRarities(Player player, ShopItemInfo targetShopItem = null)
        {
            // NOTE: This function is probably a little heavy handed, only call once per packet
            if (targetShopItem != null)
            {
                Dictionary<int, int> packCardRare = new Dictionary<int, int>();
                foreach (KeyValuePair<int, CardRarity> card in targetShopItem.Cards)
                {
                    CardRarity rarity = card.Value;
                    if (rarity == CardRarity.None || !Shop.PerPackRarities)
                    {
                        TryGetCardRarity(card.Key, CardRare, out rarity);
                    }
                    if (rarity == CardRarity.None)
                    {
                        continue;
                    }
                    packCardRare[card.Key] = (int)rarity;
                }
                return packCardRare;
            }
            if ((!ProgressiveCardList && !ProgressiveCardRarities) ||
                player.ShopState.GetAvailability(Shop, Shop.StandardPack) == PlayerShopItemAvailability.Available)
            {
                return CardRare;
            }
            Dictionary<int, int> result = ProgressiveCardList ? new Dictionary<int, int>() : new Dictionary<int, int>(CardRare);
            Dictionary<int, int> lowestPackRarities = ProgressiveCardRarities ? new Dictionary<int, int>() : null;
            foreach (ShopItemInfo shopItem in Shop.PackShop.Values)
            {
                if (ProgressiveCardList && player.ShopState.GetAvailability(Shop, shopItem) == PlayerShopItemAvailability.Hidden)
                {
                    continue;
                }
                foreach (KeyValuePair<int, CardRarity> card in shopItem.Cards)
                {
                    if (ProgressiveCardRarities)
                    {
                        CardRarity rarity;
                        if (!TryGetCardRarity(card.Key, lowestPackRarities, out rarity) || card.Value < rarity)
                        {
                            lowestPackRarities[card.Key] = (int)card.Value;
                        }
                    }
                    if (ProgressiveCardList)
                    {
                        CardRarity rarity;
                        if (!TryGetCardRarity(card.Key, result, out rarity) &&
                            TryGetCardRarity(card.Key, CardRare, out rarity))
                        {
                            result[card.Key] = (int)rarity;
                        }
                    }
                }
            }
            if (ProgressiveCardRarities)
            {
                foreach (KeyValuePair<int, int> card in new Dictionary<int, int>(result))
                {
                    CardRarity rarity;
                    if (TryGetCardRarity(card.Key, lowestPackRarities, out rarity) && rarity > (CardRarity)card.Value)
                    {
                        result[card.Key] = (int)rarity;
                    }
                }
            }
            return result;
        }

        bool TryGetCardRarity(int cardId, Dictionary<int, int> rarities, out CardRarity result)
        {
            result = CardRarity.None;
            int rarity;
            if (rarities.TryGetValue(cardId, out rarity))
            {
                result = (CardRarity)rarity;
                return true;
            }
            return false;
        }

        void SavePlayer(Player player)
        {
            // To void saving multiple times per packet save after the request has been processed
            player.RequiresSaving = true;
        }

        void SavePlayerNow(Player player)
        {
            //LogInfo("Save (player)");
            Dictionary<string, object> data = new Dictionary<string, object>();
            data["Code"] = player.Code;
            data["Name"] = player.Name;
            data["Rank"] = player.Rank;
            data["Rate"] = player.Rate;
            data["Level"] = player.Level;
            data["Exp"] = player.Exp;
            data["Gems"] = player.Gems;
            data["IconId"] = player.IconId;
            data["IconFrameId"] = player.IconFrameId;
            data["AvatarId"] = player.AvatarId;
            data["Wallpaper"] = player.Wallpaper;
            data["CardFavorites"] = player.CardFavorites.ToDictionary();
            data["TitleTags"] = player.TitleTags.ToArray();
            if (!UnlockAllItems)
            {
                data["Items"] = player.Items.ToArray();
            }
            if (!UnlockAllSoloChapters)
            {
                data["SoloChapters"] = player.SoloChaptersToDictionary();
            }
            data["CraftPoints"] = player.CraftPoints.ToDictionary();
            data["OrbPoints"] = player.OrbPoints.ToDictionary();
            data["SelectedDeck"] = player.Duel.SelectedDeckToDictionary();
            data["ShopState"] = player.ShopState.ToDictionary();
            if (!UnlockAllCards)
            {
                data["Cards"] = player.Cards.ToDictionary();
            }
            string jsonFormatted = FormatJson(MiniJSON.Json.Serialize(data));
            File.WriteAllText(playerSettingsFile, jsonFormatted);
            player.RequiresSaving = false;
        }

        void LoadPlayer(Player player)
        {
            Dictionary<string, object> data = null;
            if (File.Exists(GameServer.playerSettingsFile))
            {
                data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(GameServer.playerSettingsFile)) as Dictionary<string, object>;
            }
            if (data == null)
            {
                data = new Dictionary<string, object>();
            }

            uint code;
            if (TryGetValue(data, "Code", out code) && code != 0)
            {
                player.Code = code;
            }
            player.Name = GetValue<string>(data, "Name", "Duelist");
            player.Rank = GetValue<int>(data, "Rank", (int)StandardRank.ROOKIE);
            player.Rate = GetValue<int>(data, "Rate");
            player.Level = GetValue<int>(data, "Level", 1);
            player.Exp = GetValue<long>(data, "Exp");
            player.Gems = GetValue<int>(data, "Gems", DefaultGems);
            player.IconId = GetValue<int>(data, "IconId");
            player.IconFrameId = GetValue<int>(data, "IconFrameId");
            player.AvatarId = GetValue<int>(data, "AvatarId");
            player.Wallpaper = GetValue<int>(data, "Wallpaper");

            if (UnlockAllSoloChapters)
            {
                foreach (int chapterId in GetAllSoloChapterIds())
                {
                    player.SoloChapters[chapterId] = ChapterStatus.COMPLETE;
                }
            }
            else
            {
                player.SoloChaptersFromDictionary(GetDictionary(data, "SoloChapters"));
            }
            player.CraftPoints.FromDictionary(GetDictionary(data, "CraftPoints"));
            player.OrbPoints.FromDictionary(GetDictionary(data, "OrbPoints"));
            player.ShopState.FromDictionary(GetDictionary(data, "ShopState"));
            player.CardFavorites.FromDictionary(GetDictionary(data, "CardFavorites"));
            List<object> titleTags;
            if (TryGetValue(data, "TitleTags", out titleTags))
            {
                foreach (object tag in titleTags)
                {
                    player.TitleTags.Add((int)Convert.ChangeType(tag, typeof(int)));
                }
            }
            if (UnlockAllItems)
            {
                Type[] enumTypes =
                {
                    typeof(ItemID.AVATAR),
                    typeof(ItemID.ICON),
                    typeof(ItemID.ICON_FRAME),
                    typeof(ItemID.PROTECTOR),
                    typeof(ItemID.DECK_CASE),
                    typeof(ItemID.FIELD),
                    typeof(ItemID.FIELD_OBJ),
                    typeof(ItemID.AVATAR_HOME),
                    typeof(ItemID.WALLPAPER),
                };
                foreach (Type enumType in enumTypes)
                {
                    foreach (int value in Enum.GetValues(enumType))
                    {
                        player.Items.Add(value);
                    }
                }
            }
            else
            {
                GetIntHashSet(data, "Items", player.Items, ignoreZero: true);
                foreach (int item in DefaultItems)
                {
                    if (item != 0)
                    {
                        player.Items.Add(item);
                        switch (ItemID.GetCategoryFromID(item))
                        {
                            case ItemID.Category.ICON: if (!player.Items.Contains(player.IconId)) player.IconId = item; break;
                            case ItemID.Category.ICON_FRAME: if (!player.Items.Contains(player.IconFrameId)) player.IconFrameId = item; break;
                            case ItemID.Category.AVATAR: if (!player.Items.Contains(player.AvatarId)) player.AvatarId = item; break;
                            case ItemID.Category.WALLPAPER: if (!player.Items.Contains(player.Wallpaper)) player.Wallpaper = item; break;
                            case ItemID.Category.FIELD:
                                foreach (int fieldPartItemId in ItemID.GetDuelFieldParts(item))
                                {
                                    player.Items.Add(fieldPartItemId);
                                }
                                break;
                        }
                    }
                }
            }
            if (UnlockAllCards)
            {
                foreach (int cardId in CardRare.Keys)
                {
                    player.Cards.SetCount(cardId, 3, PlayerCardKind.Dismantle, CardStyleRarity.Normal);
                }
            }
            else
            {
                Dictionary<string, object> cards = GetDictionary(data, "Cards");
                if (cards != null)
                {
                    player.Cards.FromDictionary(cards);
                }
            }
            if (player.Cards.Count == 0)
            {
                // Only look for default starting decks if the player has no cards
                foreach (int itemId in player.Items)
                {
                    if (ItemID.GetCategoryFromID(itemId) == ItemID.Category.STRUCTURE)
                    {
                        DeckInfo deck;
                        if (StructureDecks.TryGetValue(itemId, out deck))
                        {
                            foreach (int cardId in deck.GetAllCards())
                            {
                                player.Cards.Add(cardId, 1, PlayerCardKind.NoDismantle, CardStyleRarity.Normal);
                            }
                        }
                    }
                }
            }
            if (Directory.Exists(decksDirectory))
            {
                foreach (string file in Directory.GetFiles(decksDirectory))
                {
                    LoadDeck(player, file);
                }
            }
            player.Duel.SelectedDeckFromDictionary(GetDictionary(data, "SelectedDeck"));
        }

        void SaveDeck(DeckInfo deck)
        {
            TryCreateDirectory(decksDirectory);
            if (deck.IsYdkDeck)
            {
                YdkHelper.SaveDeck(deck);
            }
            else
            {
                File.WriteAllText(deck.File, MiniJSON.Json.Serialize(deck.ToDictionaryEx()));
            }
        }

        void LoadDeck(Player player, string file)
        {
            if (file.EndsWith(".json") || file.EndsWith(".ydk"))
            {
                try
                {
                    DeckInfo deck = new DeckInfo();
                    deck.File = file;
                    LoadDeck(deck);
                    deck.Id = player.NextDeckUId++;
                    player.Decks[deck.Id] = deck;
                }
                catch
                {
                    LogWarning("Failed to load deck " + file);
                }
            }
        }

        void LoadDeck(DeckInfo deck)
        {
            if (deck.IsYdkDeck)
            {
                YdkHelper.LoadDeck(deck);
            }
            else
            {
                deck.FromDictionaryEx(MiniJSON.Json.DeserializeStripped(File.ReadAllText(deck.File)) as Dictionary<string, object>);
            }
        }

        void DeleteDeck(DeckInfo deck)
        {
            try
            {
                if (!string.IsNullOrEmpty(deck.File) && File.Exists(deck.File))
                {
                    File.Delete(deck.File);
                }
            }
            catch
            {
            }
        }

        void LoadStructureDecks()
        {
            StructureDecks = new Dictionary<int, DeckInfo>();
            string dir = Path.Combine(dataDirectory, "StructureDecks");
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as Dictionary<string, object>;
                if (data != null)
                {
                    DeckInfo deck = new DeckInfo();
                    deck.Id = GetValue<int>(data, "structure_id");
                    if (deck.Id == 0)
                    {
                        int.TryParse(Path.GetFileNameWithoutExtension(file), out deck.Id);
                    }
                    deck.Accessory.FromDictionary(GetDictionary(data, "accessory"));
                    deck.DisplayCards.FromDictionary(GetDictionary(data, "focus"));
                    deck.FromDictionary(GetDictionary(data, "contents"));
                    StructureDecks[deck.Id] = deck;
                }
            }
        }

        void LoadCardCategory()
        {
            // This data can be extracted from "External/CardCategory/CardCategory" (assets/resourcesassetbundle/external/cardcategory/cardcategory.bytes)
            CardCategories = new Dictionary<int, CardCategory>();
            CardCategoriesByName = new Dictionary<string, CardCategory>();
            string file = Path.Combine(dataDirectory, "CardCategory.bytes");
            if (!File.Exists(file))
            {
                return;
            }
            List<object> data = MessagePack.Unpack(LZ4.Decompress(File.ReadAllBytes(file))) as List<object>;
            foreach (object entry in data)
            {
                Dictionary<string, object> categoryData = entry as Dictionary<string, object>;
                if (categoryData != null)
                {
                    //id, createStart, createEnd, searchStart, searchEnd, nameJa, nameEn, nameIt, nameDe, nameEs, namePt, nameKo, sort
                    CardCategory category = new CardCategory();
                    category.Id = GetValue<int>(categoryData, "id");
                    category.Name = GetValue<string>(categoryData, "nameEn");
                    category.Sort = GetValue<int>(categoryData, "sort");// actually a string
                    CardCategories[category.Id] = category;
                    CardCategoriesByName[category.Name] = category;
                    //Debug.WriteLine(category.Id + " " + category.Name);
                }
            }
        }

        void LoadShop()
        {
            Shop = new ShopInfo();
            LoadShop(Path.Combine(dataDirectory, "Shop.json"), true);
            string dir = Path.Combine(dataDirectory, "Shop");
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, "*.json"))
                {
                    LoadShop(file, false);
                }
            }
            Dictionary<int, ShopItemInfo>[] allShops = { Shop.PackShop, Shop.StructureShop, Shop.AccessoryShop, Shop.SpecialShop };
            foreach (Dictionary<int, ShopItemInfo> shopTab in allShops)
            {
                foreach (KeyValuePair<int, ShopItemInfo> shopItem in shopTab)
                {
                    if (Shop.AllShops.ContainsKey(shopItem.Key))
                    {
                        LogWarning("Duplicate shop id " + shopItem.Key);
                    }
                    else
                    {
                        Shop.AllShops[shopItem.Key] = shopItem.Value;
                        switch (shopItem.Value.Category)
                        {
                            case ShopCategory.Pack:
                                Shop.PacksByPackId[shopItem.Value.Id] = shopItem.Value;
                                break;
                        }
                        switch (shopItem.Value.PackType)
                        {
                            case ShopPackType.Secret:
                                if (shopItem.Value.SecretType == ShopItemSecretType.None)
                                {
                                    shopItem.Value.SecretType = ShopItemSecretType.FindOrCraft;
                                }
                                foreach (int cardId in shopItem.Value.Cards.Keys)
                                {
                                    if (!Shop.SecretPacksByCardId.ContainsKey(cardId))
                                    {
                                        Shop.SecretPacksByCardId[cardId] = new List<ShopItemInfo>();
                                    }
                                    Shop.SecretPacksByCardId[cardId].Add(shopItem.Value);
                                }
                                break;
                        }
                    }
                }
            }
            if (Shop.StandardPack == null)
            {
                foreach (ShopItemInfo item in Shop.AllShops.Values)
                {
                    if (item.PackType == ShopPackType.Standard)
                    {
                        Shop.StandardPack = item;
                        break;
                    }
                }
            }
            if (Shop.PutAllCardsInStandardPack && Shop.StandardPack != null)
            {
                Shop.StandardPack.Cards.Clear();
                foreach (int cardId in CardRare.Keys.OrderBy(x => x))
                {
                    Shop.StandardPack.Cards[cardId] = CardRarity.None;
                }
            }

            foreach (ShopItemInfo shopItem in Shop.PackShop.Values)
            {
                switch (shopItem.Category)
                {
                    case ShopCategory.Pack:
                        {
                            if (string.IsNullOrEmpty(shopItem.PackImageName) &&
                                !Shop.PackShopImagesByCardId.TryGetValue(shopItem.IconMrk, out shopItem.PackImageName) &&
                                !Shop.PackShopImagesByCardId.TryGetValue(0, out shopItem.PackImageName))
                            {
                                shopItem.PackImageName = "CardPackTex01_0000";
                            }
                            if (Shop.OverridePackPrice > 0)
                            {
                                ShopItemPrice price = shopItem.Prices.FirstOrDefault(x => x.ItemAmount == 1);
                                if (price == null)
                                {
                                    // Update the price list if there's an existing price with an item amount of more than 1
                                    if (shopItem.Prices.Count != 0)
                                    {
                                        foreach (ShopItemPrice existingPrice in shopItem.Prices)
                                        {
                                            existingPrice.Id++;
                                        }
                                    }
                                    price = new ShopItemPrice();
                                    price.Id = 1;
                                    shopItem.Prices.Insert(0, price);
                                }
                                price.ItemAmount = 1;
                                price.Price = Shop.OverridePackPrice;
                            }
                            if (Shop.OverridePackPriceX10 > 0)
                            {
                                ShopItemPrice price = shopItem.Prices.FirstOrDefault(x => x.ItemAmount > 1);
                                if (price == null)
                                {
                                    price = new ShopItemPrice();
                                    price.Id = shopItem.Prices.Count + 1;
                                    shopItem.Prices.Add(price);
                                }
                                price.ItemAmount = 10;
                                price.Price = Shop.OverridePackPriceX10;
                            }
                            if (shopItem.Prices.Count == 0)
                            {
                                if (Shop.DefaultPackPrice > 0)
                                {
                                    ShopItemPrice price = new ShopItemPrice();
                                    price.Id = shopItem.Prices.Count + 1;
                                    price.ItemAmount = 1;
                                    price.Price = Shop.DefaultPackPrice;
                                    shopItem.Prices.Add(price);
                                }
                                if (Shop.DefaultPackPriceX10 > 0)
                                {
                                    ShopItemPrice price = new ShopItemPrice();
                                    price.Id = shopItem.Prices.Count + 1;
                                    price.ItemAmount = 10;
                                    price.Price = Shop.DefaultPackPriceX10;
                                    shopItem.Prices.Add(price);
                                }
                            }
                            if (shopItem.UnlockSecrets.Count > 0)
                            {
                                if (Shop.OverrideUnlockSecretsAtPercent > 0)
                                {
                                    shopItem.UnlockSecretsAtPercent = Shop.OverrideUnlockSecretsAtPercent;
                                }
                                else if (shopItem.UnlockSecretsAtPercent == 0)
                                {
                                    shopItem.UnlockSecretsAtPercent = Shop.DefaultUnlockSecretsAtPercent;
                                }
                            }
                            switch (shopItem.SecretType)
                            {
                                case ShopItemSecretType.None:
                                case ShopItemSecretType.Other:// These aren't given a duration unless explicitly defined
                                    shopItem.SecretDurationInSeconds = 0;
                                    break;
                                default:
                                    if (Shop.OverrideSecretDuration > 0)
                                    {
                                        shopItem.SecretDurationInSeconds = Shop.OverrideSecretDuration;
                                    }
                                    else if (Shop.DefaultSecretDuration > 0 && shopItem.SecretDurationInSeconds == 0)
                                    {
                                        shopItem.SecretDurationInSeconds = Shop.DefaultSecretDuration;
                                    }
                                    else if (shopItem.SecretDurationInSeconds < 0)
                                    {
                                        shopItem.SecretDurationInSeconds = 0;
                                    }
                                    break;
                            }
                            if (Shop.OverridePackCardNum > 0)
                            {
                                shopItem.CardNum = Shop.OverridePackCardNum;
                            }
                            else if (Shop.DefaultPackCardNum > 0 && shopItem.CardNum == 0)
                            {
                                shopItem.CardNum = Shop.DefaultPackCardNum;
                            }
                        }
                        break;
                    case ShopCategory.Structure:
                        {
                            if (Shop.OverrideStructureDeckPrice > 0 || (shopItem.Prices.Count == 0 && Shop.DefaultStructureDeckPrice > 0))
                            {
                                shopItem.Prices.Clear();
                                ShopItemPrice price = new ShopItemPrice();
                                price.Id = 1;
                                price.ItemAmount = 1;
                                price.Price = Shop.OverrideStructureDeckPrice > 0 ? Shop.OverrideStructureDeckPrice : Shop.DefaultStructureDeckPrice;
                                shopItem.Prices.Add(price);
                            }
                        }
                        break;
                }
            }
            

            LoadShopPackOdds(Path.Combine(dataDirectory, "ShopPackOdds.json"));
            dir = Path.Combine(dataDirectory, "ShopPackOdds");
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles("*.json"))
                {
                    LoadShopPackOdds(file);
                }
            }
            LoadShopPackOddsVisuals(Path.Combine(dataDirectory, "ShopPackOddsVisuals.json"));// TODO: Maybe just merge into Shop.json
        }

        void LoadShop(string file, bool isMainShop)
        {
            if (!File.Exists(file))
            {
                return;
            }
            Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as Dictionary<string, object>;
            if (data == null)
            {
                return;
            }
            long secretPackDuration;
            if (!TryGetValue<long>(data, "SecretPackDuration", out secretPackDuration))
            {
                secretPackDuration = -1;// Special value to state that it's not defined (only checked in LoadShopItems)
            }
            if (isMainShop)
            {
                Shop.PutAllCardsInStandardPack = GetValue<bool>(data, "PutAllCardsInStandardPack");
                Shop.NoDuplicatesPerPack = GetValue<bool>(data, "NoDuplicatesPerPack");
                Shop.PerPackRarities = GetValue<bool>(data, "PerPackRarities");
                Shop.DisableCardStyleRarity = GetValue<bool>(data, "DisableCardStyleRarity");
                Shop.UnlockAllSecrets = GetValue<bool>(data, "UnlockAllSecrets");
                Shop.DisableUltraRareGuarantee = GetValue<bool>(data, "DisableUltraRareGuarantee");
                Shop.UpgradeRarityWhenNotFound = GetValue<bool>(data, "UpgradeRarityWhenNotFound");

                Shop.DefaultSecretDuration = GetValue<long>(data, "DefaultSecretDuration");
                Shop.OverrideSecretDuration = GetValue<long>(data, "OverrideSecretDuration");
                Shop.DefaultPackPrice = GetValue<int>(data, "DefaultPackPrice");
                Shop.DefaultPackPriceX10 = GetValue<int>(data, "DefaultPackPriceX10");
                Shop.OverridePackPrice = GetValue<int>(data, "OverridePackPrice");
                Shop.OverridePackPriceX10 = GetValue<int>(data, "OverridePackPriceX10");
                Shop.DefaultStructureDeckPrice = GetValue<int>(data, "DefaultStructureDeckPrice");
                Shop.OverrideStructureDeckPrice = GetValue<int>(data, "OverrideStructureDeckPrice");
                Shop.DefaultUnlockSecretsAtPercent = GetValue<int>(data, "DefaultUnlockSecretsAtPercent");
                Shop.OverrideUnlockSecretsAtPercent = GetValue<int>(data, "OverrideUnlockSecretsAtPercent");
                Shop.DefaultPackCardNum = GetValue<int>(data, "DefaultPackCardNum");
                Shop.OverridePackCardNum = GetValue<int>(data, "OverridePackCardNum");
            }
            LoadShopItems(Shop.PackShop, "PackShop", data, secretPackDuration);
            LoadShopItems(Shop.StructureShop, "StructureShop", data, secretPackDuration);
            LoadShopItems(Shop.AccessoryShop, "AccessoryShop", data, secretPackDuration);
            LoadShopItems(Shop.SpecialShop, "SpecialShop", data, secretPackDuration);

            List<object> packShopImageStrings = GetValue(data, "PackShopImages", default(List<object>));
            if (packShopImageStrings != null)
            {
                foreach (object obj in packShopImageStrings)
                {
                    string packShopImage = obj as string;
                    if (!string.IsNullOrEmpty(packShopImage))
                    {
                        int cardId = int.Parse(packShopImage.Split('_')[1]);
                        Shop.PackShopImagesByCardId[cardId] = packShopImage;
                    }
                }
            }
        }

        void LoadShopItems(Dictionary<int, ShopItemInfo> shopItems, string type, Dictionary<string, object> shopData, long secretPackDuration)
        {
            foreach (Dictionary<string, object> data in GetDictionaryCollection(shopData, type))
            {
                ShopItemInfo info = new ShopItemInfo();
                info.Buylimit = GetValue<int>(data, "buyLimit");// custom
                info.SecretBuyLimit = GetValue<int>(data, "secretBuyLimit");// custom
                info.SecretType = (ShopItemSecretType)GetValue<int>(data, "secretType");// custom
                if (TryGetValue(data, "secretDuration", out info.SecretDurationInSeconds) && info.SecretDurationInSeconds <= 0)// custom
                {
                    info.SecretDurationInSeconds = -1;// Temporary, set to 0 in the default/override value checks
                }
                info.IconType = (ShopItemIconType)GetValue<int>(data, "iconType");
                info.IconData = GetValue<string>(data, "iconData");
                info.SubCategory = GetValue<int>(data, "subCategory", 1);
                object previewObj = GetValue<object>(data, "preview");
                if (previewObj is string)
                {
                    info.Preview = previewObj as string;
                }
                else if (previewObj is Dictionary<string, object>)
                {
                    info.Preview = MiniJSON.Json.Serialize(previewObj);
                }
                List<int> unlockSecrets = GetIntList(data, "unlockSecrets");// custom
                if (unlockSecrets != null && unlockSecrets.Count > 0)
                {
                    info.UnlockSecrets.AddRange(unlockSecrets);
                    info.UnlockSecretsAtPercent = GetValue<double>(data, "unlockSecretsAtPercent");
                }
                List<int> setPurchased = GetIntList(data, "setPurchased");// custom
                if (setPurchased != null && setPurchased.Count > 0)
                {
                    info.SetPurchased.AddRange(setPurchased);
                }
                List<object> searchCategoryList = GetValue<List<object>>(data, "searchCategory");
                if (searchCategoryList != null)
                {
                    foreach (object entry in searchCategoryList)
                    {
                        if (entry is string)
                        {
                            CardCategory category;
                            int categoryId;
                            if (int.TryParse(entry as string, out categoryId))
                            {
                                if (categoryId > 0)
                                {
                                    info.SearchCategory.Add(categoryId);
                                }
                            }
                            else if (CardCategoriesByName.TryGetValue(entry as string, out category))
                            {
                                if (category.Id > 0)
                                {
                                    info.SearchCategory.Add(category.Id);
                                }
                            }
                        }
                        else
                        {
                            int categoryId = (int)Convert.ChangeType(entry, typeof(int));
                            if (categoryId > 0)
                            {
                                info.SearchCategory.Add(categoryId);
                            }
                        }
                    }
                }
                switch (type)
                {
                    case "PackShop":
                        info.Category = ShopCategory.Pack;
                        info.Id = GetValue<int>(data, "packId");
                        if (info.Id == 0)
                        {
                            LogWarning("Invalid pack id id " + info.Id + " in shop data");
                            return;
                        }
                        info.PackType = (ShopPackType)GetValue<int>(data, "packType", 1);
                        info.CardNum = GetValue<int>(data, "pack_card_num", 1);
                        info.PackImageName = GetValue<string>(data, "packImage");// custom
                        object cardListObj;
                        if (data.TryGetValue("cardList", out cardListObj))// custom
                        {
                            if (cardListObj is List<object>)
                            {
                                List<object> cardList = cardListObj as List<object>;
                                foreach (object card in cardList)
                                {
                                    info.Cards[(int)Convert.ChangeType(card, typeof(int))] = CardRarity.None;
                                }
                            }
                            else if (cardListObj is Dictionary<string, object>)
                            {
                                Dictionary<string, object> cardList = cardListObj as Dictionary<string, object>;
                                foreach (KeyValuePair<string, object> card in cardList)
                                {
                                    int cardId;
                                    if (int.TryParse(card.Key, out cardId))
                                    {
                                        info.Cards[cardId] = (CardRarity)(int)Convert.ChangeType(card.Value, typeof(int));
                                    }
                                }
                            }
                        }
                        if (info.Cards.Count == 0)
                        {
                            LogWarning("Card pack " + info.Id + " doesn't have any cards");
                        }
                        switch (info.PackType)
                        {
                            case ShopPackType.Secret:
                                if (info.SecretType == ShopItemSecretType.None)
                                {
                                    info.SecretType = ShopItemSecretType.FindOrCraft;
                                }
                                break;
                            case ShopPackType.Standard:
                                if (GetValue<bool>(data, "isMainStandardPack"))// custom
                                {
                                    Shop.StandardPack = info;
                                }
                                break;
                        }
                        break;
                    case "StructureShop":
                        info.Category = ShopCategory.Structure;
                        info.Id = GetValue<int>(data, "structure_id");
                        if (!StructureDecks.ContainsKey(info.Id))
                        {
                            // Unknown structure deck
                            LogWarning("Unknown structure deck id " + info.Id + " in shop data");
                            return;
                        }
                        break;
                    case "AccessoryShop":
                        info.Category = ShopCategory.Accessory;
                        info.Id = GetValue<int>(data, "itemId");// There's also "item_id"
                        ItemID.Category itemCategory = ItemID.GetCategoryFromID(info.Id);
                        switch (itemCategory)
                        {
                            case ItemID.Category.AVATAR:
                                info.SubCategory = (int)ShopSubCategoryAccessory.Mate;
                                break;
                            case ItemID.Category.FIELD:
                                info.SubCategory = (int)ShopSubCategoryAccessory.Field;
                                break;
                            case ItemID.Category.PROTECTOR:
                                info.SubCategory = (int)ShopSubCategoryAccessory.Protector;
                                break;
                            case ItemID.Category.ICON:
                                info.SubCategory = (int)ShopSubCategoryAccessory.Icon;
                                break;
                            default:
                                LogWarning("Unhandled shop accessory type " + itemCategory + " for item id " + info.Id);
                                return;
                        }
                        break;
                    case "SpecialShop":
                        info.Category = ShopCategory.Special;
                        break;
                }
                switch (type)
                {
                    case "PackShop":
                    case "StructureShop":
                        info.NameText = GetValue<string>(data, "nameTextId");
                        info.DescShortText = GetValue<string>(data, "descShortTextId");
                        info.DescFullText = GetValue<string>(data, "descFullTextId");
                        info.DescTextGenerated = GetValue<bool>(data, "descGenerated");// custom
                        info.ReleaseDate = ConvertEpochTime(GetValue<long>(data, "releaseDate"));// custom
                        info.IconMrk = GetValue<int>(data, "iconMrk");
                        info.Power = GetValue<int>(data, "power");
                        info.Flexibility = GetValue<int>(data, "flexibility");
                        info.Difficulty = GetValue<int>(data, "difficulty");
                        info.OddsName = GetValue<string>(data, "oddsName");// custom
                        break;
                }
                foreach (Dictionary<string, object> priceData in GetDictionaryCollection(data, "prices"))
                {
                    ShopItemPrice price = new ShopItemPrice();
                    price.Id = info.Prices.Count + 1;
                    price.Price = GetValue<int>(priceData, "use_item_num");
                    List<object> textArgs;
                    if (TryGetValue(priceData, "textArgs", out textArgs) && textArgs.Count > 0)
                    {
                        price.ItemAmount = Math.Max(1, (int)Convert.ChangeType(textArgs[0], typeof(int)));
                    }
                    else
                    {
                        TryGetValue(priceData, "itemAmount", out price.ItemAmount);
                    }
                    price.ItemAmount = Math.Max(1, price.ItemAmount);
                    info.Prices.Add(price);
                }
                if (info.Prices.Count == 0)
                {
                    // custom
                    int priceX1 = GetValue<int>(data, "price");
                    if (priceX1 > 0)
                    {
                        info.Prices.Add(new ShopItemPrice()
                        {
                            Id = 1,
                            ItemAmount = 1,
                            Price = priceX1
                        });
                        if (info.Category == ShopCategory.Pack)
                        {
                            int priceX10 = GetValue<int>(data, "priceX10", priceX1 * 10);
                            info.Prices.Add(new ShopItemPrice()
                            {
                                Id = 2,
                                ItemAmount = 10,
                                Price = priceX10
                            });
                        }
                    }
                }
                if (shopItems.ContainsKey(info.ShopId))
                {
                    LogWarning("Duplicate shop id " + info.ShopId);
                    return;
                }
                shopItems[info.ShopId] = info;
            }
        }

        void LoadShopPackOdds(string file)
        {
            if (!File.Exists(file))
            {
                return;
            }
            List<object> oddsList = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as List<object>;
            if (oddsList == null)
            {
                return;
            }
            foreach (object dataObj in oddsList)
            {
                Dictionary<string, object> data = dataObj as Dictionary<string, object>;
                if (data == null)
                {
                    continue;
                }
                ShopOddsInfo shopOdds = new ShopOddsInfo();
                shopOdds.Name = GetValue<string>(data, "name");
                List<object> packTypesList = GetValue(data, "packTypes", default(List<object>));
                if (packTypesList != null)
                {
                    foreach (object packType in packTypesList)
                    {
                        shopOdds.PackTypes.Add((ShopPackType)(int)Convert.ChangeType(packType, typeof(int)));
                    }
                }
                List<object> cardRateList = GetValue(data, "cardRateList", default(List<object>));
                if (cardRateList != null)
                {
                    foreach (object cardRateDataObj in cardRateList)
                    {
                        Dictionary<string, object> cardRateData = cardRateDataObj as Dictionary<string, object>;
                        ShopOddsRarity rarityInfo = new ShopOddsRarity();
                        rarityInfo.StartNum = GetValue<int>(cardRateData, "start_num");
                        rarityInfo.EndNum = GetValue<int>(cardRateData, "end_num");
                        rarityInfo.Standard = GetValue<bool>(cardRateData, "standard");
                        rarityInfo.GuaranteeRareMin = (CardRarity)GetValue<int>(cardRateData, "settle_rare_min");
                        rarityInfo.GuaranteeRareMax = (CardRarity)GetValue<int>(cardRateData, "settle_rare_max");
                        Dictionary<string, object> rateData = GetDictionary(cardRateData, "rate");
                        foreach (KeyValuePair<string, object> item in rateData)
                        {
                            CardRarity rarity = (CardRarity)int.Parse(item.Key);
                            if (rarity == CardRarity.None)
                            {
                                LogWarning("Invalid card rarity in shop odds");
                            }
                            else
                            {
                                rarityInfo.Rate[rarity] = GetValue<double>(item.Value as Dictionary<string, object>, "rate");
                            }
                        }
                        shopOdds.CardRateList.Add(rarityInfo);
                    }
                }
                List<object> premiereRateList = GetValue(data, "premiereRateList", default(List<object>));
                if (premiereRateList != null)
                {
                    foreach (object premRateDataObj in premiereRateList)
                    {
                        Dictionary<string, object> premRateData = premRateDataObj as Dictionary<string, object>;
                        List<object> rareData = GetValue(premRateData, "rare", default(List<object>));
                        Dictionary<string, object> rateData = GetDictionary(premRateData, "rate");
                        if (rareData != null && rateData != null)
                        {
                            ShopOddsStyleRarity rarityInfo = new ShopOddsStyleRarity();
                            foreach (object rareObj in rareData)
                            {
                                CardRarity rarity = (CardRarity)(int)Convert.ChangeType(rareObj, typeof(int));
                                if (rarity != CardRarity.None)
                                {
                                    rarityInfo.Rarities.Add(rarity);
                                }
                            }
                            foreach (KeyValuePair<string, object> rate in rateData)
                            {
                                CardStyleRarity value = (CardStyleRarity)(int)Convert.ChangeType(rate.Key, typeof(int));
                                rarityInfo.Rate[value] = (double)Convert.ChangeType(rate.Value, typeof(double));
                            }
                            shopOdds.CardStyleRarityRateList.Add(rarityInfo);
                        }
                    }
                }
                foreach (ShopPackType packType in shopOdds.PackTypes)
                {
                    if (Shop.PackOddsByPackType.ContainsKey(packType))
                    {
                        LogWarning("Duplicate shop odds for pack type " + packType);
                    }
                    else
                    {
                        Shop.PackOddsByPackType[packType] = shopOdds;
                    }
                }
                if (!string.IsNullOrEmpty(shopOdds.Name))
                {
                    if (Shop.PackOddsByName.ContainsKey(shopOdds.Name))
                    {
                        LogWarning("Duplicate shop odds name '" + shopOdds.Name + "'");
                    }
                    else
                    {
                        Shop.PackOddsByName[shopOdds.Name] = shopOdds;
                    }
                }
            }
        }

        void LoadShopPackOddsVisuals(string file)
        {
            if (!File.Exists(file))
            {
                return;
            }
            Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as Dictionary<string, object>;
            ShopOddsVisualsSettings settings = Shop.PackOddsVisuals;
            settings.RarityJebait = GetValue<bool>(data, "RarityJebait", true);
            settings.RarityOnCardBack = GetValue<bool>(data, "RarityOnCardBack", true);
            settings.RarityOnPack = GetValue<bool>(data, "RarityOnPack", true);
        }

        void LoadSolo()
        {
            string file = Path.Combine(dataDirectory, "Solo.json");
            if (!File.Exists(file))
            {
                LogWarning("Failed to load solo file");
                return;
            }
            Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as Dictionary<string, object>;
            data = GetResData(data);
            Dictionary<string, object> masterData;
            if (data != null && TryGetValue(data, "Master", out masterData))
            {
                data = masterData;
            }
            if (data != null)
            {
                TryGetValue(data, "Solo", out SoloData);
            }
            LoadSoloDuels();
        }

        void LoadSoloDuels()
        {
            SoloDuels = new Dictionary<int, DuelSettings>();
            int nextStoryDeckId = 1;
            string dir = Path.Combine(dataDirectory, "SoloDuels");
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as Dictionary<string, object>;
                data = GetResData(data);
                Dictionary<string, object> duelData;
                if (!TryGetValue(data, "Duel", out duelData))
                {
                    continue;
                }
                int chapterId;
                if (!TryGetValue(duelData, "chapter", out chapterId))
                {
                    continue;
                }
                DuelSettings duel = new DuelSettings();
                duel.FromDictionary(duelData);
                for (int i = 0; i < duel.Deck.Length; i++)
                {
                    if (duel.Deck[i].MainDeckCards.Count > 0)
                    {
                        duel.Deck[i].Id = nextStoryDeckId++;
                    }
                }
                duel.SetRequiredDefaults();
                if (SoloDuels.ContainsKey(chapterId))
                {
                    LogWarning("Duplicate chapter " + chapterId);
                    continue;
                }
                SoloDuels[chapterId] = duel;
            }
        }

        /// <summary>
        /// Helper to merge packet logs of visting the shop to extract out desired information (card packs)
        /// </summary>
        void MergeShopDumps()
        {
            string dir = Path.Combine(dataDirectory, "ShopDumps");
            if (!Directory.Exists(dir))
            {
                return;
            }
            Dictionary<int, Dictionary<string, object>> packShop = new Dictionary<int, Dictionary<string, object>>();
            Dictionary<int, Dictionary<string, object>> structureShop = new Dictionary<int, Dictionary<string, object>>();
            Dictionary<int, Dictionary<string, object>> accessoryShop = new Dictionary<int, Dictionary<string, object>>();
            Dictionary<int, Dictionary<string, object>> specialShop = new Dictionary<int, Dictionary<string, object>>();
            List<Dictionary<string, object>> gachaDatas = new List<Dictionary<string, object>>();
            foreach (string file in Directory.GetFiles(dir))
            {
                Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(file)) as Dictionary<string, object>;
                data = GetResData(data);
                if (data != null)
                {
                    Dictionary<string, object> shopData = GetValue(data, "Shop", default(Dictionary<string, object>));
                    if (shopData != null)
                    {
                        Action<string, Dictionary<int, Dictionary<string, object>>> fetchShop = (string shopName, Dictionary<int, Dictionary<string, object>> shop) =>
                            {
                                Dictionary<string, object> items = GetValue(shopData, shopName, default(Dictionary<string, object>));
                                foreach (KeyValuePair<string, object> item in items)
                                {
                                    int shopId;
                                    if (int.TryParse(item.Key, out shopId))
                                    {
                                        Dictionary<string, object> itemData = item.Value as Dictionary<string, object>;
                                        if (itemData != null)
                                        {
                                            shop[shopId] = itemData;
                                        }
                                    }
                                }
                            };
                        fetchShop("PackShop", packShop);
                        fetchShop("StructureShop", structureShop);
                        fetchShop("AccessoryShop", accessoryShop);
                        fetchShop("SpecialShop", specialShop);
                    }
                    Dictionary<string, object> gachaData = GetValue(data, "Gacha", default(Dictionary<string, object>));
                    if (gachaData != null)
                    {
                        gachaDatas.Add(gachaData);
                    }
                }
            }
            foreach (Dictionary<string, object> gachaData in gachaDatas)
            {
                Dictionary<string, object> cardListData = GetValue(gachaData, "cardList", default(Dictionary<string, object>));
                if (cardListData != null)
                {
                    foreach (KeyValuePair<int, Dictionary<string, object>> packShopItem in packShop)
                    {
                        int normalCardListId = GetValue<int>(packShopItem.Value, "normalCardListId");
                        int pickupCardListId = GetValue<int>(packShopItem.Value, "pickupCardListId");
                        object cardIdsObj = null;
                        if (pickupCardListId != 0)
                        {
                            TryGetValue(cardListData, pickupCardListId.ToString(), out cardIdsObj);
                        }
                        else if (normalCardListId != 0)
                        {
                            TryGetValue(cardListData, normalCardListId.ToString(), out cardIdsObj);
                        }
                        if (cardIdsObj != null)
                        {
                            packShopItem.Value["cardList"] = cardIdsObj;
                        }
                    }
                }
            }
            MergeShops(Path.Combine(dataDirectory, "AllShopsMerged.json"), packShop, structureShop, accessoryShop, specialShop);
        }

        void MergeShops(string outputFile,
            Dictionary<int, Dictionary<string, object>> packShop,
            Dictionary<int, Dictionary<string, object>> structureShop,
            Dictionary<int, Dictionary<string, object>> accessoryShop,
            Dictionary<int, Dictionary<string, object>> specialShop)
        {
            StringBuilder sb = new StringBuilder();
            Dictionary<string, Dictionary<int, Dictionary<string, object>>> allShops = new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();
            if (packShop != null)
            {
                allShops["PackShop"] = packShop;
            }
            if (structureShop != null)
            {
                allShops["StructureShop"] = structureShop;
            }
            if (accessoryShop != null)
            {
                allShops["AccessoryShop"] = accessoryShop;
            }
            if (specialShop != null)
            {
                //allShops["SpecialShop"] = specialShop;// TODO
            }
            foreach (KeyValuePair<string, Dictionary<int, Dictionary<string, object>>> shop in allShops)
            {
                sb.AppendLine("    \"" + shop.Key + "\": {");
                foreach (KeyValuePair<int, Dictionary<string, object>> shopItem in shop.Value)
                {
                    sb.AppendLine("        \"" + shopItem.Key + "\":" + MiniJSON.Json.Serialize(shopItem.Value) + ",");
                }
                sb.AppendLine("    },");
            }
            File.WriteAllText(outputFile, sb.ToString());
        }

        // TODO: Remove. This is no longer being used (though fetching official sets is still useful)
        void CreateShopsFromOfficialSets()
        {
            // TODO: Change this to scrape wikia/yugipedia to get more verbose info
            string file = Path.Combine(dataDirectory, "OfficialSets.json");
            YgoDbSetCollection collection = new YgoDbSetCollection();
            if (!File.Exists(file))
            {
                Console.WriteLine("Downloading sets...");
                string dumpDir = Path.Combine(dataDirectory, "OfficialSetsDump");
                TryCreateDirectory(dumpDir);
                collection.DownloadSets(dumpDir);
                collection.Save(file);
                Console.WriteLine("Done");
            }
            collection.Load(file);

            const int basePackId = 9000;
            int nextPackId = basePackId + 1;
            const int baseStructureId = 1120900;// Should be high enough
            int nextStructureId = baseStructureId + 1;

            string extraDataFile = Path.Combine(dataDirectory, "OfficialSetsExtraData.json");
            if (!File.Exists(extraDataFile))
            {
                Console.WriteLine("Couldn't find extra data for official sets");
                return;
            }
            Dictionary<string, object> allExtraData = MiniJSON.Json.DeserializeStripped(File.ReadAllText(extraDataFile)) as Dictionary<string, object>;
            Dictionary<long, Dictionary<string, object>> extraDataCollection = new Dictionary<long, Dictionary<string, object>>();
            List<object> extraDataSets = GetValue(allExtraData, "sets", default(List<object>));
            foreach (object obj in extraDataSets)
            {
                Dictionary<string, object> extra = obj as Dictionary<string, object>;
                long setId;
                if (extra != null && TryGetValue(extra, "id", out setId))
                {
                    extraDataCollection[setId] = extra;
                }
            }
            List<object> extraDataAutoSetsListObj = GetValue(allExtraData, "autoSets", default(List<object>));
            List<DateTime> extraDataAutoSetsList = new List<DateTime>();
            if (extraDataAutoSetsListObj != null)
            {
                foreach (object obj in extraDataAutoSetsListObj)
                {
                    extraDataAutoSetsList.Add(DateTime.Parse(obj as string));
                }
            }
            extraDataAutoSetsList.Sort();
            int nextAutoSetIndex = extraDataAutoSetsList.Count > 0 ? 0 : -1;

            bool isFirstPack = true;
            Dictionary<int, Dictionary<string, object>> packShop = new Dictionary<int, Dictionary<string, object>>();
            Dictionary<int, Dictionary<string, object>> structureShop = new Dictionary<int, Dictionary<string, object>>();
            Dictionary<int, CardRarity> lowestCardRarity = new Dictionary<int, CardRarity>();
            HashSet<int> usedCardIds = new HashSet<int>();
            foreach (YgoDbSet set in collection.Sets.Values.OrderBy(x => x.ReleaseDate))
            {
                foreach (KeyValuePair<int, YgoDbSet.CardRarity> card in set.Cards)
                {
                    if (CardRare.ContainsKey(card.Key))
                    {
                        CardRarity rarity = YgoDbSet.ConvertRarity(card.Value);
                        if (rarity > CardRarity.None)
                        {
                            CardRarity currentRarity;
                            if (!lowestCardRarity.TryGetValue(card.Key, out currentRarity) || rarity < currentRarity)
                            {
                                lowestCardRarity[card.Key] = rarity;
                            }
                        }
                    }
                }

                string setName;
                Dictionary<string, object> extraData;
                if (!extraDataCollection.TryGetValue(set.Id, out extraData) ||
                    //!GetValue<bool>(extraData, "core") ||
                    !TryGetValue(extraData, "name", out setName))
                {
                    continue;
                }
                int coverCardId = GetValue<int>(extraData, "cover");
                if (coverCardId == 0)
                {
                    LogWarning("Set " + set.Id + " doesn't have a cover card");
                    continue;
                }
                if (!CardRare.ContainsKey(coverCardId))
                {
                    LogWarning("Cover card id " + coverCardId + " is not in the game (set: " + set.Id + ")");
                    coverCardId = 6018;// Mokey mokey
                }
                if (set.Type == YgoDbSetType.StarterDeck)
                {
                    int structureId = nextStructureId++;

                    int sleeve = GetValue<int>(extraData, "sleeve");
                    int box = GetValue<int>(extraData, "box");
                    
                    DeckInfo deck = new DeckInfo();
                    deck.Id = structureId;
                    deck.Accessory.Box = box > 0 ? box : (int)ItemID.DECK_CASE.ID1080001;
                    deck.Accessory.Sleeve = sleeve > 0 ? sleeve : (int)ItemID.PROTECTOR.ID1070001;

                    foreach (int cardId in set.Cards.Keys)
                    {
                        if (!CardRare.ContainsKey(cardId))
                        {
                            continue;
                        }
                        usedCardIds.Add(cardId);
                        // TODO: Determine way to get the card type
                        switch (cardId)
                        {
                            case 4021:// Flame Swordsman
                            case 4075:// Thousand Dragon
                                deck.ExtraDeckCards.Add(cardId);
                                break;
                            default:
                                deck.MainDeckCards.Add(cardId);
                                break;
                        }
                    }

                    List<int> focusCards = GetIntList(extraData, "focus");
                    if (focusCards != null && focusCards.Count > 0)
                    {
                        for (int i = 0; i < focusCards.Count && i < 3; i++)
                        {
                            deck.DisplayCards.Add(focusCards[i]);
                        }
                    }
                    // NOTE: Focus cards are required outwise it'll bug out the shop
                    while (deck.DisplayCards.Count < 3)
                    {
                        deck.DisplayCards.Add(6018);// Mokey mokey
                    }
                    structureShop[ShopInfo.GetBaseShopId(ShopCategory.Structure) + structureId] = new Dictionary<string, object>()
                    {
                        { "releaseDate", GetEpochTime(set.ReleaseDate) },
                        { "category", (int)ShopCategory.Structure },
                        { "subCategory", 1 },
                        { "price", 100 },
                        { "iconMrk", coverCardId },
                        { "iconType", (int)ShopItemIconType.CardThumb },
                        { "iconData", coverCardId.ToString() },
                        //{ "preview", },//TODO
                        //{ "searchCategory", },//TODO
                        { "structure_id", structureId },
                        { "accessory", deck.Accessory.ToDictionary() },
                        { "focus", deck.DisplayCards.ToDictionary() },
                        { "contents", deck.ToDictionary() }
                    };
                    Dictionary<string, object> structureDeckData = deck.ToDictionaryStructureDeck();
                    File.WriteAllText(Path.Combine(dataDirectory, "StructureDecks", structureId + ".json"), MiniJSON.Json.Serialize(structureDeckData));
                }
                else if (set.Type == YgoDbSetType.BoosterPack)
                {
                    int numCardsInGame = 0;
                    foreach (KeyValuePair<int, YgoDbSet.CardRarity> card in set.Cards)
                    {
                        if (CardRare.ContainsKey(card.Key))
                        {
                            numCardsInGame++;
                        }
                    }
                    if (set.Cards.Count - numCardsInGame > 10) //numCardsInGame < 55)
                    {
                        LogWarning("Skip set " + set.Id + " with only " + numCardsInGame + "/" + set.Cards.Count + " cards in game '" + set.Name + "'");
                        continue;
                    }

                    if (nextAutoSetIndex >= 0 && nextAutoSetIndex < extraDataAutoSetsList.Count)
                    {
                        // TODO
                        DateTime autoSetDate = extraDataAutoSetsList[nextAutoSetIndex];
                        if (autoSetDate < set.ReleaseDate)
                        {
                            int numAdded = 0;
                            int numUltra = 0, numSuper = 0, numRare = 0, numNormal = 0;
                            foreach (KeyValuePair<int, CardRarity> card in lowestCardRarity)
                            {
                                if (!usedCardIds.Contains(card.Key))
                                {
                                    numAdded++;
                                    usedCardIds.Add(card.Key);
                                    switch (card.Value)
                                    {
                                        case CardRarity.UltraRare: numUltra++; break;
                                        case CardRarity.SuperRare: numSuper++; break;
                                        case CardRarity.Rare: numRare++; break;
                                        case CardRarity.Normal: numNormal++; break;
                                    }
                                }
                            }
                            Console.WriteLine("Add " + numAdded + " " + set.ReleaseDate + " " + autoSetDate + " | " + numUltra + " " + numSuper + " " + numRare + " " + numNormal);
                            nextAutoSetIndex++;
                        }
                    }

                    Dictionary<int, int> cards = new Dictionary<int, int>();
                    foreach (KeyValuePair<int, YgoDbSet.CardRarity> card in set.Cards)
                    {
                        if (CardRare.ContainsKey(card.Key))
                        {
                            usedCardIds.Add(card.Key);
                            cards[card.Key] = (int)YgoDbSet.ConvertRarity(card.Value);
                        }
                    }
                    int packId = nextPackId++;
                    packShop[packId] = new Dictionary<string, object>()
                    {
                        { "packId", packId },
                        { "packType", (int)ShopPackType.Standard },
                        { "secretType", (int)(isFirstPack ? ShopItemSecretType.None : ShopItemSecretType.Other) },
                        { "unlockSecrets", new int[] { nextPackId } },
                        { "nameTextId", setName },
                        { "descGenerated", true },
                        { "releaseDate", GetEpochTime(set.ReleaseDate) },
                        //{ "packImage", "YdbCardPack_" + set.Id },
                        { "pack_card_num", Math.Min(8, GetValue<int>(extraData, "num", 8)) },
                        //{ "category", (int)ShopCategory.Pack },// Not needed
                        { "subCategory", 1 },
                        { "iconMrk", coverCardId },
                        { "iconType", (int)ShopItemIconType.ItemThumb },
                        { "iconData", "set" + set.Id },
                        { "preview", "[{\"type\":3,\"path\":\"set" + set.Id + "\"}]" },
                        { "packImage", "set" + set.Id },
                        //{ "preview", },//TODO
                        //{ "searchCategory", },//TODO
                        { "cardList", cards },
                        { "price", GetValue<int>(extraData, "price") },
                        { "oddsName", GetValue<string>(extraData, "odds") },
                    };
                    isFirstPack = false;
                }
            }
            if (extraDataAutoSetsList.Count > 0)
            {
                // TODO
                int numAddedFinal = 0;
                foreach (KeyValuePair<int, int> card in CardRare)
                {
                    if (!usedCardIds.Contains(card.Key))
                    {
                        numAddedFinal++;
                        usedCardIds.Add(card.Key);
                    }
                }
                Console.WriteLine("Add (final) " + numAddedFinal);
            }
            int numAdditionalCards = 0;
            if (packShop.Count > 0)
            {
                packShop[nextPackId - 1]["unlockSecrets"] = new int[] { nextPackId };
                int packId = nextPackId++;
                List<int> unusedCardIds = new List<int>();
                foreach (int cid in CardRare.Keys)
                {
                    if (!usedCardIds.Contains(cid))
                    {
                        numAdditionalCards++;
                        unusedCardIds.Add(cid);
                    }
                }
                packShop[packId] = new Dictionary<string, object>()
                {
                    { "isMainStandardPack", true },
                    { "packId", packId },
                    { "packType", (int)ShopPackType.Standard },
                    { "secretType", (int)ShopItemSecretType.Other },
                    { "nameTextId", "All remaining cards" },
                    { "descGenerated", true },
                    { "pack_card_num", 8 },
                    { "subCategory", 1 },
                    { "iconMrk", 0 },
                    { "iconType", (int)ShopItemIconType.ItemThumb },
                    { "iconData", "packthumb0001" },
                    { "preview", "[{\"type\":3,\"path\":\"packthumb0001\"}]" },
                    { "cardList", unusedCardIds },
                };
            }
            foreach (KeyValuePair<int, Dictionary<string, object>> shop in structureShop)
            {
                int[] otherIds = structureShop.Keys.ToList().FindAll(x => x != shop.Key).ToArray();
                shop.Value["setPurchased"] = otherIds;
            }
            Console.WriteLine("realPacks: " + usedCardIds.Count + " / " + CardRare.Keys.Count + " additional: " + numAdditionalCards);
            MergeShops(Path.Combine(dataDirectory, "GeneratedShopsFromOfficialSets.json"), packShop, structureShop, null, null);
        }

        // TODO: Remove. This is no longer being used.
        void ConvertLeDataToSolo()
        {
            // TODO: Also pull in the duelist challenges (maybe via sub gates?)
            string leDir = Path.Combine(dataDirectory, "LeData");
            if (!Directory.Exists(leDir))
            {
                Console.WriteLine("Couldn't find link evolution data directory '" + leDir + "'");
                return;
            }
            string duelsDir = Path.Combine(leDir, "Duels");
            string soloDuelsDir = Path.Combine(dataDirectory, "SoloDuels");

            TryCreateDirectory(soloDuelsDir);

            StringBuilder soloStrings = new StringBuilder();
            Dictionary<string, object> data = new Dictionary<string, object>();
            Dictionary<string, object> masterData = new Dictionary<string, object>();
            data["Master"] = masterData;
            Dictionary<string, object> soloData = new Dictionary<string, object>();
            masterData["Solo"] = soloData;
            Dictionary<string, object> allGatesData = new Dictionary<string, object>();
            Dictionary<string, object> allChaptersData = new Dictionary<string, object>();
            Dictionary<string, object> allRewardData = new Dictionary<string, object>();
            soloData["gate"] = allGatesData;
            soloData["chapter"] = allChaptersData;
            soloData["unlock"] = new Dictionary<string, object>();
            soloData["unlock_item"] = new Dictionary<string, object>();
            soloData["reward"] = allRewardData;
            Dictionary<string, string> dirs = new Dictionary<string, string>()
            {
                { "YuGiOh", "Yu-Gi-Oh!" },
                { "YuGiOhGX", "Yu-Gi-Oh! GX" },
                { "YuGiOh5D", "Yu-Gi-Oh! 5D's" },
                { "YuGiOhZEXAL", "Yu-Gi-Oh! ZEXAL" },
                { "YuGiOhARCV", "Yu-Gi-Oh! ARC-V" },
                { "YuGiOhVRAINS", "Yu-Gi-Oh! VRAINS" }
            };
            // gate 1 - everything is a practice duel
            // gate 2/3 - shows the wrong card flipping around
            int nextGateId = 4;
            int nextRewardId = 1;
            foreach (KeyValuePair<string, string> dir in dirs)
            {
                int gateId = nextGateId++;

                soloStrings.AppendLine("[IDS_SOLO.GATE" + (gateId.ToString().PadLeft(3, '0')) + "]");
                soloStrings.AppendLine(dir.Value);
                soloStrings.AppendLine("[IDS_SOLO.GATE" + (gateId.ToString().PadLeft(3, '0')) + "_EXPLANATION]]");
                soloStrings.AppendLine("Duels featuring cards from " + dir.Value);

                Dictionary<string, object> gateData = new Dictionary<string, object>();
                gateData["priority"] = gateId;
                gateData["parent_gate"] = 0;
                gateData["view_gate"] = 0;
                gateData["unlock_id"] = 0;
                allGatesData[gateId.ToString()] = gateData;
                Dictionary<string, object> gateChapterData = new Dictionary<string, object>();
                allChaptersData[gateId.ToString()] = gateChapterData;
                Dictionary<int, Dictionary<string, object>> seriesDuelDatas = new Dictionary<int, Dictionary<string, object>>();
                foreach (string file in Directory.GetFiles(Path.Combine(duelsDir, dir.Key)))
                {
                    Dictionary<string, object> duelData = MiniJSON.Json.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                    seriesDuelDatas[GetValue<int>(duelData, "displayIndex")] = duelData;
                }
                int nextChapterId = (gateId * 10000) + 1;
                int parentChapterId = 0;
                foreach (KeyValuePair<int, Dictionary<string, object>> duelOverviewData in seriesDuelDatas.OrderBy(x => x.Key))
                {
                    string deck1File = Path.Combine(leDir, GetValue<string>(duelOverviewData.Value, "playerDeck"));
                    string deck2File = Path.Combine(leDir, GetValue<string>(duelOverviewData.Value, "opponentDeck"));
                    Func<string, bool> validateDeck = (string path) =>
                        {
                            Dictionary<string, object> tempDeckData = MiniJSON.Json.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
                            DeckInfo deck = new DeckInfo();
                            deck.FromDictionaryEx(tempDeckData);
                            bool valid = true;
                            foreach (int cardId in deck.GetAllCards())
                            {
                                if (!CardRare.ContainsKey(cardId))
                                {
                                    Console.WriteLine("Missing card " + cardId);
                                    valid = false;
                                }
                            }
                            return valid;
                        };
                    if (!validateDeck(deck1File) || !validateDeck(deck2File))
                    {
                        Console.WriteLine("Missing cards in '" + GetValue<string>(duelOverviewData.Value, "name") + "'");
                        //continue;
                    }

                    int randSeed = 0;
                    unchecked
                    {
                        byte[] nameBuffer = Encoding.UTF8.GetBytes(GetValue<string>(duelOverviewData.Value, "name"));
                        for (int i = 0; i < nameBuffer.Length; i += 4)
                        {
                            if (i >= nameBuffer.Length - 4)
                            {
                                randSeed += nameBuffer[i];
                            }
                            else
                            {
                                randSeed += BitConverter.ToInt32(nameBuffer, i);
                            }
                        }
                    }
                    Random rand = new Random(randSeed);
                    for (int j = 0; j < 2; j++)
                    {
                        int chapterId = nextChapterId++;
                        Dictionary<string, object> chapterData = new Dictionary<string, object>();

                        Dictionary<string, object> duelData = new Dictionary<string, object>();
                        List<object> decks = new List<object>();
                        for (int i = 0; i < 2; i++)
                        {
                            string targetFile = i == (j == 0 ? 0 : 1) ? deck1File : deck2File;
                            Dictionary<string, object> inputDeckData = MiniJSON.Json.Deserialize(File.ReadAllText(targetFile)) as Dictionary<string, object>;
                            DeckInfo deck = new DeckInfo();
                            deck.FromDictionaryEx(inputDeckData);
                            foreach (int cid in new List<int>(deck.MainDeckCards.GetIds()))
                            {
                                if (!CardRare.ContainsKey(cid))
                                {
                                    deck.MainDeckCards.RemoveAll(cid);
                                    deck.MainDeckCards.Add(4844);// Pot of greed
                                }
                            }
                            foreach (int cid in new List<int>(deck.ExtraDeckCards.GetIds()))
                            {
                                if (!CardRare.ContainsKey(cid))
                                {
                                    deck.ExtraDeckCards.RemoveAll(cid);
                                }
                            }
                            foreach (int cid in new List<int>(deck.SideDeckCards.GetIds()))
                            {
                                if (!CardRare.ContainsKey(cid))
                                {
                                    deck.SideDeckCards.RemoveAll(cid);
                                }
                            }
                            decks.Add(deck.ToDictionary(true));

                            int rewardId = nextRewardId++;
                            allRewardData[rewardId.ToString()] = new Dictionary<string, object>()
                            {
                                { ((int)ItemID.Category.CONSUME).ToString(), new Dictionary<string, object>() {
                                    { ((int)ItemID.CONSUME.ID0001).ToString(), rand.Next(5, 50) }
                                }}
                            };
                            // NOTE: This should probably be pulling the reward from the opponent deck only (currently it pulls from both)
                            /*const int numCardRewards = 1;// Can increase this but the client only shows 1 reward...
                            int rewardId = nextRewardId++;
                            Dictionary<string, object> cardRewards = new Dictionary<string, object>();
                            allRewardData[rewardId.ToString()] = new Dictionary<string, object>()
                            {
                                { ((int)ItemID.Category.CARD).ToString(), cardRewards }
                            };
                            List<int> deckCardIds = deck.GetAllCards(main: true, extra: true);
                            for (int k = 0; k < numCardRewards; k++)
                            {
                                if (deckCardIds.Count > 0)
                                {
                                    int targetCardId = deckCardIds[rand.Next(deckCardIds.Count)];
                                    deckCardIds.RemoveAll(x => x == targetCardId);
                                    cardRewards[targetCardId.ToString()] = 1;
                                }
                            }*/
                            if (i == 0)
                            {
                                chapterData["set_id"] = rewardId;
                            }
                            else
                            {
                                chapterData["mydeck_set_id"] = rewardId;
                            }
                        }
                        duelData["Deck"] = decks;
                        duelData["chapter"] = chapterId;
                        int fieldId = ItemID.GetRandomId(rand, ItemID.Category.FIELD);
                        int fieldObjId = ItemID.GetFieldObjFromField(fieldId);
                        int avatarBaseId = ItemID.GetFieldAvatarBaseFromField(fieldId);
                        duelData["mat"] = new List<int>() { fieldId, fieldId };
                        duelData["duel_object"] = new List<int>() { fieldObjId, fieldObjId };
                        duelData["avatar_home"] = new List<int>() { avatarBaseId, avatarBaseId };
                        duelData["name"] = new List<string>() { "", "CPU" };
                        duelData["sleeve"] = new List<int>() { 0, ItemID.GetRandomId(rand, ItemID.Category.PROTECTOR) };
                        duelData["icon"] = new List<int>() { 0, ItemID.GetRandomId(rand, ItemID.Category.ICON) };
                        duelData["icon_frame"] = new List<int>() { 0, ItemID.GetRandomId(rand, ItemID.Category.ICON_FRAME) };
                        duelData["avatar"] = new List<int>() { 0, ItemID.GetRandomId(rand, ItemID.Category.AVATAR) };
                        Dictionary<string, object> duelDataContainer = new Dictionary<string, object>();
                        duelDataContainer["Duel"] = duelData;
                        string outputDuelFileName = Path.Combine(soloDuelsDir, chapterId + ".json");
                        File.WriteAllText(outputDuelFileName, MiniJSON.Json.Serialize(duelDataContainer));

                        string duelName = GetValue<string>(duelOverviewData.Value, "name");
                        string duelDesc = GetValue<string>(duelOverviewData.Value, "description");
                        if (!string.IsNullOrEmpty(duelName))
                        {
                            soloStrings.AppendLine("[IDS_SOLO.CHAPTER" + chapterId + "_EXPLANATION]");
                            soloStrings.AppendLine(duelName + (j > 0 ? " (reverse duel)" : string.Empty));
                            if (!string.IsNullOrEmpty(duelDesc))
                            {
                                soloStrings.AppendLine();
                                soloStrings.AppendLine(duelDesc);
                            }
                        }

                        chapterData["parent_chapter"] = parentChapterId;
                        chapterData["unlock_id"] = 0;
                        chapterData["begin_sn"] = "";
                        chapterData["npc_id"] = 1;// Not sure if this value matter
                        gateChapterData[chapterId.ToString()] = chapterData;

                        if (j == 0)
                        {
                            parentChapterId = chapterId;
                        }
                    }
                }
                if (parentChapterId > 0)
                {
                    gateData["clear_chapter"] = parentChapterId;
                }
            }
            File.WriteAllText(Path.Combine(dataDirectory, "Solo.json"), MiniJSON.Json.Serialize(data));
            File.WriteAllText(Path.Combine(dataDirectory, "IDS_SOLO.txt"), soloStrings.ToString());
        }

        void ExtractStructureDecks()
        {
            string extractFile = Path.Combine(dataDirectory, "StructureDecks.json");
            if (!File.Exists(extractFile))
            {
                return;
            }
            string targetDir = Path.Combine(dataDirectory, "StructureDecks");
            if (!TryCreateDirectory(targetDir))
            {
                return;
            }
            Dictionary<string, object> data = MiniJSON.Json.DeserializeStripped(File.ReadAllText(extractFile)) as Dictionary<string, object>;
            Dictionary<string, object> structureDecksData = GetDictionary(data, "Structure");
            if (structureDecksData != null)
            {
                foreach (object obj in structureDecksData.Values)
                {
                    Dictionary<string, object> deckData = obj as Dictionary<string, object>;
                    int id = GetValue<int>(deckData, "structure_id");
                    if (id > 0)
                    {
                        string path = Path.Combine(targetDir, id + ".json");
                        File.WriteAllText(path, MiniJSON.Json.Serialize(obj));
                    }
                }
            }
        }
    }
}
