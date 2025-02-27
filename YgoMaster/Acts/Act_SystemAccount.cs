﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YgoMaster
{
    partial class GameServer
    {
        void Act_AccountAuth(GameServerWebRequest request)
        {
            request.Response["Server"] = new Dictionary<string, object>()
            {
                { "urls_deck_ext", new Dictionary<string, object>() {
                    { "debug", false },
                    { "com_prefix", "data.ycdb" },
                    { "Cgdb.deck_search", new Dictionary<string, object>() {
                        { "cn", "searchDeckForMd" },
                        { "url", deckSearchUrl }
                    }},
                    { "Cgdb.deck_search_detail", new Dictionary<string, object>() {
                        { "cn", "getDeckDetailForMd" },
                        { "url", deckSearchDetailUrl }
                    }},
                    { "Cgdb.deck_search_init", new Dictionary<string, object>() {
                        { "cn", "getDeckAttributesForMd" },
                        { "url", deckSearchAttributesUrl }
                    }},
                }}
            };
            request.Remove(
                "Persistence.System.token",
                "Server.Agreement",
                "Persistence.System.optout",
                "Master",
                "User",
                "Deck",
                "Tournament",
                "Response");
        }

        void Act_SystemInfo(GameServerWebRequest request)
        {
            Version clientVersion;
            if (!string.IsNullOrEmpty(request.ClientVersion) && Version.TryParse(request.ClientVersion, out clientVersion) &&
                clientVersion > highestSupportedClientVersion)
            {
                request.Response["Operation"] = new Dictionary<string, object>()
                {
                    { "Dialog", new Dictionary<string, object>() {
                        { "title", "Unsupported client version" },
                        { "buttonLabelTid", "IDS_SYS_OK" },
                        { "lowerMessage", "Client version: " + request.ClientVersion + "\nHighest supported version: " + highestSupportedClientVersion },
                        { "upperMessage", "Unsupported client version " + request.ClientVersion }
                    }}
                };
                request.ResultCode = (int)ResultCodes.ErrorCode.MAINTE;
                request.Remove("Server", "Response", "Download", "Operation.Dialog");
                return;
            }

            // NOTE: These aren't normally set here, but doing it here is good enough
            request.Response["Persistence"] = new Dictionary<string, object>()
            {
                { "System", new Dictionary<string, object>() {
                    { "token", Convert.ToBase64String(Encoding.UTF8.GetBytes("sessionToken")) },
                    { "pcode", request.Player.Code },
                }},
            };

            Dictionary<string, object> urls = new Dictionary<string, object>();
            urls["Account.auth"] = serverUrl;
            urls["Account.create"] = serverUrl;
            urls["Account.inherit"] = serverUrl;
            urls["Account.Steam.get_user_id"] = serverUrl;
            urls["Account.Steam.re_auth"] = serverUrl;
            urls["Billing.add_purchased_item"] = serverUrl;
            urls["Billing.cancel"] = serverUrl;
            urls["Billing.history"] = serverUrl;
            urls["Billing.in_complete_item_check"] = serverUrl;
            urls["Billing.product_list"] = serverUrl;
            urls["Billing.purchase"] = serverUrl;
            urls["Billing.re_store"] = serverUrl;
            urls["Billing.reservation"] = serverUrl;
            urls["Billing.in_complete_item_check"] = serverUrl;
            urls["Billing.re_store"] = serverUrl;

            request.Response["Server"] = new Dictionary<string, object>()
            {
                { "status", (int)ServerStatus.NORMAL },
                { "pvp", true },
                { "inherit", true },
                { "urls", urls },
                { "url", serverUrl },
                { "url_polling", serverPollUrl },
                { "debug_tool", false },
                { "langs", new List<object>() {
                    new string[] { "en-US", "English" },
                    new string[] { "fr-FR", "Français" },
                    new string[] { "it-IT", "Italiano" },
                    new string[] { "de-DE", "Deutsch" },
                    new string[] { "es-ES", "Español" },
                    new string[] { "pt-BR", "Português" },
                    new string[] { "ja-JP", "日本語" },
                    new string[] { "ko-KR", "한국어" },
                }}
            };

            request.Remove("Server");
            request.Remove("Response");
            request.Remove("Download");
        }
    }
}
