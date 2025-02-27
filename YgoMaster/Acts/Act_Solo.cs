﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Look into:
// SoloSelectChapterViewController
// YgomGame.Solo.mainScroll.SoloModeViewController (InfinityScrollView)
// YgomSystem.UI.InfinityScroll.InfinityScrollView

// TODO: Pull SoloData into a class (SoloInfo or something) because currently there are several lookups of the same thing
// (NOTE: this is now partially implemented along with an alternative text format... but unused for now. See SoloInfo.cs)

namespace YgoMaster
{
    partial class GameServer
    {
        internal static int GetChapterGateId(int chapterId)
        {
            return chapterId / 10000;
        }

        HashSet<int> GetSkippableChapterIds()
        {
            return new HashSet<int>()
            {
                10001, 10002, 10003,
            };
        }

        HashSet<int> GetAllSoloChapterIds()
        {
            HashSet<int> result = new HashSet<int>();
            Dictionary<string, object> allChapterData = GetDictionary(SoloData, "chapter");
            foreach (KeyValuePair<string, object> gateChapterData in allChapterData)
            {
                Dictionary<string, object> chapters = gateChapterData.Value as Dictionary<string, object>;
                if (chapters != null)
                {
                    foreach (KeyValuePair<string, object> chapter in chapters)
                    {
                        int chapterId;
                        if (int.TryParse(chapter.Key, out chapterId) && chapterId > 0)
                        {
                            result.Add(chapterId);
                        }
                    }
                }
            }
            return result;
        }

        bool IsValidNonDuelChapter(int chapterId)
        {
            int gateId = GetChapterGateId(chapterId);
            Dictionary<string, object> allChapterData = GetDictionary(SoloData, "chapter");
            if (allChapterData == null)
            {
                return false;
            }
            Dictionary<string, object> chapterGateData = GetDictionary(allChapterData, gateId.ToString());
            if (chapterGateData == null)
            {
                return false;
            }
            Dictionary<string, object> chapterData = GetDictionary(chapterGateData, chapterId.ToString());
            if (chapterData == null)
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(GetValue<string>(chapterData, "begin_sn")))
            {
                // This is a scenario chapter
                return true;
            }
            if (GetValue<int>(chapterData, "unlock_id") != 0)
            {
                // This is a lock
                return true;
            }
            // TODO: Change the final check from "clear_chapter" (on the gate) to "npc_id" on the chapter. It should be non-zero for duels
            Dictionary<string, object> allGateData = GetDictionary(SoloData, "gate");
            if (allGateData == null)
            {
                return false;
            }
            Dictionary<string, object> gateData = GetDictionary(allGateData, gateId.ToString());
            if (gateData == null)
            {
                return false;
            }
            if (GetValue<int>(gateData, "clear_chapter") == chapterId)
            {
                // This is the goal/clear chapter
                return true;
            }
            return false;
        }

        bool GetChapterSetIds(GameServerWebRequest request, int chapterId, out int myDeckSetId, out int loanerDeckSetId)
        {
            myDeckSetId = 0;
            loanerDeckSetId = 0;
            int gateId = GetChapterGateId(chapterId);
            Dictionary<string, object> allChapterData = GetDictionary(SoloData, "chapter");
            if (allChapterData == null)
            {
                return false;
            }
            Dictionary<string, object> chapterGateData = GetDictionary(allChapterData, gateId.ToString());
            if (chapterGateData == null)
            {
                return false;
            }
            Dictionary<string, object> chapterData = GetDictionary(chapterGateData, chapterId.ToString());
            if (chapterData == null)
            {
                return false;
            }
            myDeckSetId = GetValue<int>(chapterData, "mydeck_set_id");
            loanerDeckSetId = GetValue<int>(chapterData, "set_id");
            return true;
        }

        void SoloUpdateChapterStatus(GameServerWebRequest request, int chapterId, DuelResultType duelResult)
        {
            int gateId = GetChapterGateId(chapterId);

            Dictionary<string, object> allGateData = GetDictionary(SoloData, "gate");
            if (allGateData == null)
            {
                LogWarning("Failed to get all gate data");
                return;
            }
            Dictionary<string, object> gateData = GetDictionary(allGateData, gateId.ToString());
            if (gateData == null)
            {
                LogWarning("Failed to get gate " + gateId);
                return;
            }
            bool isChapterGoal = GetValue<int>(gateData, "clear_chapter") == chapterId;

            ChapterStatus newStatus = ChapterStatus.OPEN;
            Dictionary<string, object> allChapterData = GetDictionary(SoloData, "chapter");
            if (allChapterData == null)
            {
                LogWarning("Failed to get all chapter data");
                return;
            }
            Dictionary<string, object> chapterGateData = GetDictionary(allChapterData, gateId.ToString());
            if (chapterGateData == null)
            {
                LogWarning("Failed to get gate data");
                return;
            }
            Dictionary<string, object> chapterData = GetDictionary(chapterGateData, chapterId.ToString());
            if (chapterData == null)
            {
                LogWarning("Failed to get chapter data");
                return;
            }

            bool statusChange = true;
            ChapterStatus currentStatus;
            if (request.Player.SoloChapters.TryGetValue(chapterId, out currentStatus))
            {
                switch (currentStatus)
                {
                    case ChapterStatus.MYDECK_CLEAR:
                        if (duelResult != DuelResultType.None && request.Player.Duel.IsMyDeck)
                        {
                            statusChange = false;
                        }
                        break;
                    case ChapterStatus.RENTAL_CLEAR:
                        if (duelResult != DuelResultType.None && !request.Player.Duel.IsMyDeck)
                        {
                            statusChange = false;
                        }
                        break;
                    case ChapterStatus.COMPLETE:
                        statusChange = false;
                        break;
                }
            }

            if (duelResult != DuelResultType.None)
            {
                // NOTE: The location of this means extra rewards will before anything else
                Dictionary<string, object> extraRewards = GetDictionary(chapterData, "extraRewards");
                if (extraRewards != null)
                {
                    DuelRewardInfos rewards = new DuelRewardInfos();
                    rewards.FromDictionary(extraRewards);
                    GiveDuelReward(request, rewards, duelResult, statusChange);
                }
            }

            switch (duelResult)
            {
                case DuelResultType.Win:
                case DuelResultType.None:
                    break;
                default:
                    if (!GetSkippableChapterIds().Contains(request.Player.Duel.ChapterId))
                    {
                        return;
                    }
                    break;
            }
            if (!statusChange)
            {
                return;
            }

            int myDeckSetId = GetValue<int>(chapterData, "mydeck_set_id");
            int loanerDeckSetId = GetValue<int>(chapterData, "set_id");
            int targetSetId = 0;
            if (SoloDuels.ContainsKey(chapterId))
            {
                if (request.Player.Duel.IsMyDeck)
                {
                    if (myDeckSetId == 0)
                    {
                        LogWarning("Completed chapter with own deck but no option for it");
                        return;
                    }
                    targetSetId = myDeckSetId;
                    if (loanerDeckSetId == 0 || currentStatus > ChapterStatus.OPEN)
                    {
                        newStatus = ChapterStatus.COMPLETE;
                    }
                    else
                    {
                        newStatus = ChapterStatus.MYDECK_CLEAR;
                    }
                }
                else
                {
                    if (loanerDeckSetId == 0)
                    {
                        LogWarning("Completed chapter with loaner deck but no option for it");
                        return;
                    }
                    targetSetId = loanerDeckSetId;
                    if (myDeckSetId == 0 || currentStatus > ChapterStatus.OPEN)
                    {
                        newStatus = ChapterStatus.COMPLETE;
                    }
                    else
                    {
                        newStatus = ChapterStatus.RENTAL_CLEAR;
                    }
                }
            }
            else
            {
                if (!IsValidNonDuelChapter(chapterId))
                {
                    LogWarning("Unknown chapter type unknown for " + chapterId + " (but will attempt to set it to complete)");
                }
                targetSetId = loanerDeckSetId != 0 ? loanerDeckSetId : myDeckSetId;
                newStatus = ChapterStatus.COMPLETE;
            }
            DuelRewardInfos duelRewards = null;
            if (SoloRewardsInDuelResult)
            {
                duelRewards = new DuelRewardInfos();
            }
            List<object> resultRewards = new List<object>();
            List<object> resultSubtractItems = new List<object>();
            Dictionary<string, object> allRewardData = GetDictionary(SoloData, "reward");
            if (allRewardData == null)
            {
                LogWarning("Failed to get all reward data");
                return;
            }
            int unlockId = GetValue<int>(chapterData, "unlock_id");
            if (unlockId != 0)
            {
                Dictionary<string, object> allUnlockData = GetDictionary(SoloData, "unlock");
                Dictionary<string, object> allUnlockItemData = GetDictionary(SoloData, "unlock_item");
                if (allUnlockData == null || allUnlockItemData == null)
                {
                    LogWarning("Failed to get all unlock data");
                    return;
                }
                Dictionary<string, object> unlockData = GetDictionary(allUnlockData, unlockId.ToString());
                if (unlockData == null)
                {
                    LogWarning("Failed to get unlock data for unlock_id " + unlockId);
                    return;
                }
                /////////////////////////////////////////////////////////////////////////
                // !!! No return statments below this as we're modifying player state !!!
                /////////////////////////////////////////////////////////////////////////
                foreach (KeyValuePair<string, object> unlockRequirement in unlockData)
                {
                    ChapterUnlockType unlockType;
                    if (Enum.TryParse(unlockRequirement.Key, out unlockType))
                    {
                        switch (unlockType)
                        {
                            case ChapterUnlockType.ITEM:
                                {
                                    List<object> itemSetList = unlockRequirement.Value as List<object>;
                                    if (itemSetList == null)
                                    {
                                        continue;
                                    }
                                    // NOTE: There should be a validation pass before taking any items, but just trust the client knows what it has
                                    foreach (object itemSet in itemSetList)
                                    {
                                        int itemSetId = (int)Convert.ChangeType(itemSet, typeof(int));
                                        Dictionary<string, object> itemsByCategory = GetDictionary(allUnlockItemData, itemSetId.ToString());
                                        if (itemsByCategory == null)
                                        {
                                            LogWarning("Failed to find unlock_item " + itemSetId + " for unlock_id" + unlockId + " on chapter " + chapterId);
                                            continue;
                                        }
                                        foreach (KeyValuePair<string, object> itemCategory in itemsByCategory)
                                        {
                                            Dictionary<string, object> items = itemCategory.Value as Dictionary<string, object>;
                                            ItemID.Category category;
                                            if (!Enum.TryParse(itemCategory.Key, out category))
                                            {
                                                continue;
                                            }
                                            foreach (KeyValuePair<string, object> item in items)
                                            {
                                                int itemId;
                                                int count = (int)Convert.ChangeType(item.Value, typeof(int));
                                                if (!int.TryParse(item.Key, out itemId))
                                                {
                                                    continue;
                                                }
                                                // NOTE: The game only counts CONSUME items (displays all correctly, but doesn't count them)
                                                switch (category)
                                                {
                                                    case ItemID.Category.CONSUME:
                                                        switch ((ItemID.CONSUME)itemId)
                                                        {
                                                            case ItemID.CONSUME.ID0001:
                                                            case ItemID.CONSUME.ID0002:
                                                                request.Player.Gems = Math.Max(0, request.Player.Gems - count);
                                                                break;
                                                            case ItemID.CONSUME.ID0003:
                                                            case ItemID.CONSUME.ID0004:
                                                            case ItemID.CONSUME.ID0005:
                                                            case ItemID.CONSUME.ID0006:
                                                                {
                                                                    CardRarity rarity = CardRarity.None;
                                                                    switch ((ItemID.CONSUME)itemId)
                                                                    {
                                                                        case ItemID.CONSUME.ID0003: rarity = CardRarity.Normal; break;
                                                                        case ItemID.CONSUME.ID0004: rarity = CardRarity.Rare; break;
                                                                        case ItemID.CONSUME.ID0005: rarity = CardRarity.SuperRare; break;
                                                                        case ItemID.CONSUME.ID0006: rarity = CardRarity.UltraRare; break;
                                                                    }
                                                                    if (request.Player.CraftPoints.CanSubtract(rarity, count))
                                                                    {
                                                                        request.Player.CraftPoints.Subtract(rarity, count);
                                                                    }
                                                                    else
                                                                    {
                                                                        request.Player.CraftPoints.Set(rarity, 0);
                                                                    }
                                                                }
                                                                break;
                                                            case ItemID.CONSUME.ID0008:
                                                            case ItemID.CONSUME.ID0009:
                                                            case ItemID.CONSUME.ID0010:
                                                            case ItemID.CONSUME.ID0011:
                                                            case ItemID.CONSUME.ID0012:
                                                            case ItemID.CONSUME.ID0013:
                                                                {
                                                                    OrbType orbType = OrbType.None;
                                                                    switch ((ItemID.CONSUME)itemId)
                                                                    {
                                                                        case ItemID.CONSUME.ID0008: orbType = OrbType.Dark; break;
                                                                        case ItemID.CONSUME.ID0009: orbType = OrbType.Light; break;
                                                                        case ItemID.CONSUME.ID0010: orbType = OrbType.Earth; break;
                                                                        case ItemID.CONSUME.ID0011: orbType = OrbType.Water; break;
                                                                        case ItemID.CONSUME.ID0012: orbType = OrbType.Fire; break;
                                                                        case ItemID.CONSUME.ID0013: orbType = OrbType.Wind; break;
                                                                    }
                                                                    if (request.Player.OrbPoints.CanSubtract(orbType, count))
                                                                    {
                                                                        request.Player.OrbPoints.Subtract(orbType, count);
                                                                    }
                                                                    else
                                                                    {
                                                                        request.Player.OrbPoints.Set(orbType, 0);
                                                                    }
                                                                }
                                                                break;
                                                            default:
                                                                LogWarning("Unhandled CONSUME requirement item " + itemId);
                                                                break;
                                                        }
                                                        break;
                                                    case ItemID.Category.CARD:
                                                        int cardCountRemaining = count;
                                                        for (CardStyleRarity i = CardStyleRarity.Normal; i <= CardStyleRarity.Shine; i++)
                                                        {
                                                            int currentCardCount = request.Player.Cards.GetCount(itemId, PlayerCardKind.Dismantle, i);
                                                            if (currentCardCount > 0)
                                                            {
                                                                int numCardsToTake = Math.Min(cardCountRemaining, currentCardCount);
                                                                request.Player.Cards.Subtract(itemId, numCardsToTake, PlayerCardKind.Dismantle, i);
                                                                cardCountRemaining -= numCardsToTake;
                                                            }
                                                        }
                                                        break;
                                                    case ItemID.Category.STRUCTURE:
                                                        LogWarning("Removing a structure deck isn't supported");
                                                        break;
                                                    case ItemID.Category.AVATAR:
                                                    case ItemID.Category.ICON:
                                                    case ItemID.Category.ICON_FRAME:
                                                    case ItemID.Category.PROTECTOR:
                                                    case ItemID.Category.DECK_CASE:
                                                    case ItemID.Category.AVATAR_HOME:
                                                    case ItemID.Category.FIELD_OBJ:
                                                    case ItemID.Category.FIELD:
                                                        request.Player.Items.Remove(itemId);
                                                        break;
                                                }
                                                WriteItem(request, itemId);
                                                resultSubtractItems.Add(new Dictionary<string, object>()
                                                {
                                                    { "category", (int)category },
                                                    { "item_id", itemId },
                                                    { "num", count },
                                                    { "type", (int)category },
                                                });
                                            }
                                        }
                                    }
                                }
                                break;
                        }
                    }
                }
            }
            if (targetSetId != 0)
            {
                Dictionary<string, object> rewardData = GetDictionary(allRewardData, targetSetId.ToString());
                foreach (KeyValuePair<string, object> reward in rewardData)
                {
                    Dictionary<string, object> rewardItems = reward.Value as Dictionary<string, object>;
                    ItemID.Category category;
                    if (!Enum.TryParse(reward.Key, out category))
                    {
                        continue;
                    }
                    foreach (KeyValuePair<string, object> item in rewardItems)
                    {
                        int itemId;
                        int count = (int)Convert.ChangeType(item.Value, typeof(int));
                        if (!int.TryParse(item.Key, out itemId))
                        {
                            continue;
                        }
                        bool valid = true;
                        switch (category)
                        {
                            case ItemID.Category.CONSUME:
                                {
                                    if (!SoloRewardsInDuelResult)
                                    {
                                        request.Player.AddItem(itemId, count);
                                    }
                                }
                                break;
                            case ItemID.Category.CARD:
                                {
                                    if (!SoloRewardsInDuelResult)
                                    {
                                        request.Player.Cards.Add(itemId, count, PlayerCardKind.NoDismantle, CardStyleRarity.Normal);
                                    }
                                }
                                break;
                            case ItemID.Category.STRUCTURE:
                                {
                                    count = 1;
                                    if (!SoloRewardsInDuelResult)
                                    {
                                        GiveStructureDeck(request, itemId);
                                    }
                                }
                                break;
                            case ItemID.Category.AVATAR:
                            case ItemID.Category.ICON:
                            case ItemID.Category.ICON_FRAME:
                            case ItemID.Category.PROTECTOR:
                            case ItemID.Category.DECK_CASE:
                            case ItemID.Category.AVATAR_HOME:
                            case ItemID.Category.FIELD_OBJ:
                            case ItemID.Category.FIELD:
                                {
                                    count = 1;
                                    if (!SoloRewardsInDuelResult)
                                    {
                                        request.Player.AddItem(itemId, count);
                                    }
                                }
                                break;
                            case ItemID.Category.PROFILE_TAG:
                                // Ignore (currently all tags are auto added)
                                valid = false;
                                break;
                            case ItemID.Category.PACK_TICKET:
                                // Ignore (currently don't handle pack tickets)
                                valid = false;
                                break;
                            default:
                                LogWarning("Unhandled reward category " + category);
                                valid = false;
                                break;
                        }
                        if (valid)
                        {
                            if (SoloRewardsInDuelResult)
                            {
                                DuelRewardInfo duelReward = new DuelRewardInfo();
                                duelReward.MinValue = duelReward.MaxValue = count;
                                duelReward.Rate = 100;
                                duelReward.Rare = SoloRewardsInDuelResultAreRare;
                                duelReward.Ids = new List<int>();
                                duelReward.Ids.Add(itemId);
                                duelReward.CardNoDismantle = true;
                                switch (category)
                                {
                                    case ItemID.Category.CARD:
                                        duelReward.Type = DuelCustomRewardType.Card;
                                        break;
                                    default:
                                        // This can also handle gems
                                        duelReward.Type = DuelCustomRewardType.Item;
                                        break;
                                }
                                duelRewards.Win.Add(duelReward);
                            }
                            else
                            {
                                WriteItem(request, itemId);
                                resultRewards.Add(new Dictionary<string, object>()
                                {
                                    { "category", (int)category },
                                    { "item_id", itemId },
                                    { "num", count },
                                    { "type", (int)category },
                                });
                            }
                        }
                    }
                }
            }
            Dictionary<string, object> unlockedPackData = null;
            if (newStatus == ChapterStatus.COMPLETE)
            {
                object unlockSecretObj;
                if (chapterData.TryGetValue("unlock_secret", out unlockSecretObj))// custom
                {
                    List<int> unlockSecretIds = new List<int>();
                    if (unlockSecretObj is List<int>)
                    {
                        foreach (object idObj in unlockSecretObj as List<int>)
                        {
                            unlockSecretIds.Add((int)Convert.ChangeType(idObj, typeof(int)));
                        }
                    }
                    else
                    {
                        unlockSecretIds.Add((int)Convert.ChangeType(unlockSecretObj, typeof(int)));
                    }
                    foreach (int unlockSecretId in unlockSecretIds)
                    {
                        ShopItemInfo secretPack;
                        if (unlockSecretId > 0 && Shop.PacksByPackId.TryGetValue(unlockSecretId, out secretPack))
                        {
                            switch (secretPack.SecretType)
                            {
                                case ShopItemSecretType.Find:
                                case ShopItemSecretType.FindOrCraft:
                                case ShopItemSecretType.Other:
                                    bool isHidden = request.Player.ShopState.GetAvailability(Shop, secretPack) == PlayerShopItemAvailability.Hidden;
                                    if (isHidden)
                                    {
                                        request.Player.ShopState.New(secretPack);
                                    }
                                    request.Player.ShopState.Unlock(secretPack);
                                    if ((isChapterGoal || SoloShowGateClearForAllSecretPacks) && unlockedPackData == null)
                                    {
                                        unlockedPackData = new Dictionary<string, object>()
                                        {
                                            { "nameTextId", FixIdString(secretPack.NameText) },
                                            { "shopId", secretPack.ShopId },
                                            { "iconMrk", secretPack.IconMrk },
                                            { "is_extend", !isHidden }
                                        };
                                    }
                                    break;
                            }
                        }
                    }
                }
            }

            request.Player.SoloChapters[chapterId] = newStatus;
            SavePlayer(request.Player);

            Dictionary<string, object> soloData = request.GetOrCreateDictionary("Solo");
            Dictionary<string, object> clearedData = GetOrCreateDictionary(soloData, "cleared");
            Dictionary<string, object> gateClearedData = GetOrCreateDictionary(clearedData, gateId.ToString());
            gateClearedData[chapterId.ToString()] = (int)newStatus;

            Dictionary<string, object> resultData = GetOrCreateDictionary(soloData, "Result");
            if (SoloRewardsInDuelResult)
            {
                GiveDuelReward(request, duelRewards, DuelResultType.Win, true);
            }
            // NOTE: You need the "rewards" entry (even if empty) to get the "COMPLETE" popup banner
            if (!resultData.ContainsKey("rewards"))
            {
                resultData["rewards"] = resultRewards;
            }
            else
            {
                (resultData["rewards"] as List<object>).AddRange(resultRewards);
            }
            resultData["sub"] = resultSubtractItems;
            if (isChapterGoal || unlockedPackData != null)
            {
                Dictionary<string, object> gateClear = new Dictionary<string, object>();
                if (isChapterGoal)
                {
                    gateClear["gate"] = gateId;
                };
                if (unlockedPackData != null)
                {
                    gateClear["pack"] = unlockedPackData;
                }
                resultData["gate_clear"] = gateClear;
            }

            request.Remove("Solo.Result");
        }

        void Act_SoloInfo(GameServerWebRequest request)
        {
            if (GetValue<bool>(request.ActParams, "back"))
            {
                return;
            }
            request.Response["Master"] = new Dictionary<string, object>()
            {
                { "Solo", SoloData }
            };
            request.GetOrCreateDictionary("Solo")["cleared"] = request.Player.SoloChaptersToDictionary();
            WriteSolo_deck_info(request);
            request.Remove("Master.solo", "Solo");
        }

        void Act_SoloDetail(GameServerWebRequest request)
        {
            int chapterId;
            if (TryGetValue(request.ActParams, "chapter", out chapterId))
            {
                Dictionary<string, object> chapterInfo = new Dictionary<string, object>();
                DuelSettings duel = GetSoloDuelSettings(request.Player, chapterId);
                if (duel != null)
                {
                    // NOTE: These ids likely link up to IDS_DECKRECIPE so really they shouldn't be generated
                    if (duel.Deck[DuelSettings.PlayerIndex].MainDeckCards.Count > 0)
                    {
                        chapterInfo["story_deck"] = duel.Deck[DuelSettings.PlayerIndex].ToDictionary();
                        chapterInfo["story_deck_id"] = duel.Deck[DuelSettings.PlayerIndex].Id;
                    }
                    if (duel.Deck[DuelSettings.CpuIndex].MainDeckCards.Count > 0)
                    {
                        chapterInfo["npc_deck"] = duel.Deck[DuelSettings.CpuIndex].ToDictionary();
                        chapterInfo["npc_deck_id"] = duel.Deck[DuelSettings.CpuIndex].Id;
                    }
                    if (duel.FirstPlayer >= 0)
                    {
                        chapterInfo["FirstPlayer"] = duel.FirstPlayer;
                    }
                }
                else if (!IsValidNonDuelChapter(chapterId))
                {
                    LogWarning("Failed to find info for chapter " + chapterId);
                }
                request.Response["Solo"] = new Dictionary<string, object>()
                {
                    { "chapter", new Dictionary<string, object>() {
                        { chapterId.ToString(), chapterInfo }
                    }}
                };
            }
        }

        void Act_SoloSetUseDeckType(GameServerWebRequest request)
        {
            int chapterId, deckType;
            if (TryGetValue(request.ActParams, "chapter", out chapterId) && TryGetValue(request.ActParams, "deck_type", out deckType))
            {
                request.Player.Duel.IsMyDeck = (SoloDeckType)deckType == SoloDeckType.POSSESSION;
            }
            request.Remove("Solo.rental_deck");
        }

        void Act_SoloDeckCheck(GameServerWebRequest request)
        {
            WriteSolo_deck_info(request);
        }

        void Act_SoloStart(GameServerWebRequest request)
        {
            int chapterId;
            if (TryGetValue<int>(request.ActParams, "chapter", out chapterId))
            {
                int myDeckSetId;
                int loanerDeckSetId;
                if (GetChapterSetIds(request, chapterId, out myDeckSetId, out loanerDeckSetId) &&
                    (loanerDeckSetId == 0 || (request.Player.Duel.IsMyDeck && myDeckSetId != 0)))
                {
                    DeckInfo deck = request.Player.Duel.GetDeck(GameMode.SoloSingle);
                    request.Player.Duel.IsMyDeck = deck != null;
                }
                DuelSettings duelSettings = CreateSoloDuelSettingsInstance(request.Player, chapterId);
                if (duelSettings != null)
                {
                    request.Response["Duel"] = new Dictionary<string, object>()
                    {
                        { "avatar", duelSettings.avatar },
                        { "icon", duelSettings.icon },
                        { "icon_frame", duelSettings.icon_frame },
                        { "sleeve", duelSettings.sleeve },
                        { "mat", duelSettings.mat },
                        { "duel_object", duelSettings.duel_object },
                        { "avatar_home", duelSettings.avatar_home }
                    };
                }
                else if (IsValidNonDuelChapter(chapterId))
                {
                    SoloUpdateChapterStatus(request, chapterId, DuelResultType.None);
                }
                else
                {
                    request.ResultCode = (int)ResultCodes.SoloCode.INVALID_CHAPTER;
                    LogWarning("Failed to start chapter " + chapterId);
                }
            }
            request.Remove("Solo.Result", "Duel", "DuelResult");
        }

        void Act_SoloSkip(GameServerWebRequest request)
        {
            int chapterId;
            if (TryGetValue<int>(request.ActParams, "chapter", out chapterId))
            {
                // Assumes the client knows what it should / shouldn't skip
                SoloUpdateChapterStatus(request, chapterId, DuelResultType.None);
            }
            request.Remove("Duel", "DuelResult", "Solo.DuelResult");
        }
    }
}
