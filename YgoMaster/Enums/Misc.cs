﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    /// <summary>
    /// TopicsBannerResourceBinder.BannerPatern
    /// </summary>
    enum TopicsBannerPatern
    {
        NOTIFY = 1,
        ACCESSORY,
        PACK ,
        GEM,
        SPECIAL,
        STANDARD,
        EVENT,
        DUELPASS,
        MAINTENANCE,
        UPDATE,
        DUELLIVE,
        STRUCTURE
    }

    /// <summary>
    /// YgomGame.Card.Content.Rarity
    /// </summary>
    enum CardRarity
    {
        None,
        Normal,
        Rare,
        SuperRare,
        UltraRare
    }

    /// <summary>
    /// YgomGame.Deck.SearchFilter.Setting.STYLE (sort of)
    /// </summary>
    enum CardStyleRarity
    {
        None,
        Normal,
        Shine,
        Royal
    }

    /// <summary>
    /// YgomGame.Colosseum.ColosseumUtil.StandardRank
    /// </summary>
    enum StandardRank
    {
        ROOKIE = 1,
        BRONZE,
        SILVER,
        GOLD,
        PLATINUM,
        DIAMOND,
        KING
    }

    /// <summary>
    /// YgomSystem.Network.ServerStatus
    /// </summary>
    enum ServerStatus
    {
        NORMAL,
        INCIDENT,
        MAINTENANCE,
        MAINTENANCE_TEAM
    }

    enum OrbType
    {
        None,
        Dark,
        Light,
        Earth,
        Water,
        Fire,
        Wind
    }

    /// <summary>
    /// YgomGame.Duel.Util.GameMode
    /// </summary>
    enum GameMode
    {
        Normal,
        Free,
        Single,
        Rank,
        Tournament,
        TournamentSingle,
        Audience,
        Replay,
        RankSingle,
        SoloSingle,
        Room,
        Exhibition,
        Null
    }

    /// <summary>
    /// YgomGame.Solo.SoloModeUtil.ChapterStatus
    /// </summary>
    enum ChapterStatus
    {
        UNOPEN = -1,
        OPEN,
        RENTAL_CLEAR,
        MYDECK_CLEAR,
        COMPLETE
    }

    /// <summary>
    /// YgomGame.Solo.SoloModeViewController.UnlockType (defines the type of unlock requirements for a given unlock)
    /// </summary>
    enum ChapterUnlockType
    {
        USER_LEVEL = 1,
        CHAPTER_OR,
        ITEM,
        CHAPTER_AND
    }

    /// <summary>
    /// YgomGame.Solo.SoloModeUtil.DeckType
    /// </summary>
    enum SoloDeckType
    {
        POSSESSION,
        STORY
    }

    /// <summary>
    /// YgomGame.Duel.Engine.ResultType
    /// </summary>
    enum DuelResultType
    {
        None,
        Win,
        Lose,
        Draw,
        Time
    }

    /// <summary>
    /// IDS_DECKEDIT.HOWTOGET_CATEGORY (off by 1?)
    /// </summary>
    enum HowToObtainCard
    {
        None = 0,
        Pack = 1,
        Solo = 2,
        Tournament = 3,// Assumed
        Exhibition = 4,// Assumed
        //Craft = 5,// Assumed removed
        InitialDistributionStructure = 5,
        SalesStructure = 6,
        Mission = 7,// Assumed
        DuelResult = 8,
        BundleDeals = 9
    }

    /// <summary>
    /// IDS_SCORE (IDS_SCORE.DETAIL_XXX)
    /// </summary>
    enum DuelResultScore
    {
        None,
        DuelVistory,
        Draw,
        ComebackVictory,
        QuickVictory,
        DeckOutVictory,
        SpecialVictories,
        NoDamage,
        LowLP,
        LPOnTheBrink,
        FewCardsLeft,
        CardsOnTheBrink,
        Over3000Damage,
        Over5000Damage,
        Over9999Damage,
        VictoryByEffectDamageOnly,
        Destroyed5Monsters,
        Destroyed7Monsters,
        Destroyed10Monsters,
        ActivatedSpell,
        ActivatedTrap,
        PerformedSpecialSummon,
        PerformedTributeSummon,
        PerformedFusionSummon,
        PerformedRitualSummon,
        PerformedSynchroSummon,
        PerformedXyzSummon,
        PerformedPendulumSummon,
        LinkSummon
    }
}
