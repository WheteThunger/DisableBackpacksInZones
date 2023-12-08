using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("Disable Backpacks In Zones", "WhiteThunder", "0.1.0")]
    [Description("Prevents players from accessing backpacks while in configured zones.")]
    internal class DisableBackpacksInZones : CovalencePlugin
    {
        #region Fields

        private const string PermissionBypass = "disablebackpacksinzones.bypass";

        private Configuration _config;

        [PluginReference]
        private readonly Plugin ZoneManager;

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermissionBypass, this);
        }

        private object CanOpenBackpack(BasePlayer looter, ulong ownerId)
        {
            // Don't block admins from accessing other players' backpacks.
            if (looter.userID != ownerId)
                return null;

            // Allow users with the bypass permission to open their backpack regardless of the zone.
            if (permission.UserHasPermission(looter.UserIDString, PermissionBypass))
                return null;

            if (!IsPlayerInRestrictedZone(looter))
                return null;

            return GetMessage(looter.UserIDString, LangEntry.DeniedZone);
        }

        #endregion

        #region Helpers

        private bool IsPlayerInRestrictedZone(BasePlayer player)
        {
            var zoneIdList = ZoneManager?.Call("GetPlayerZoneIDs", player) as string[];
            if (zoneIdList == null)
            {
                // Player is not in a zone, or Zone Manager is not loaded.
                return false;
            }

            foreach (var zoneId in zoneIdList)
            {
                if (_config.RestrictedZoneIds.Contains(zoneId))
                    return true;
            }

            return false;
        }

        #endregion

        #region Configuration

        [JsonObject(MemberSerialization.OptIn)]
        private class Configuration : BaseConfiguration
        {
            [JsonProperty("Disable backpacks in these zone IDs")]
            public string[] RestrictedZoneIds = Array.Empty<string>();
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #region Configuration Helpers

        private class BaseConfiguration
        {
            public string ToJson() => JsonConvert.SerializeObject(this);
            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                            changed = true;
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #endregion

        #region Localization

        private class LangEntry
        {
            public static readonly List<LangEntry> AllLangEntries = new List<LangEntry>();

            public static readonly LangEntry DeniedZone = new LangEntry("BackpackDisabledInZone", "You cannot access your backpack in this zone.");

            public string Name;
            public string English;

            public LangEntry(string name, string english)
            {
                Name = name;
                English = english;

                AllLangEntries.Add(this);
            }
        }

        private string GetMessage(string playerId, LangEntry langEntry) =>
            lang.GetMessage(langEntry.Name, this, playerId);

        private void ChatMessage(BasePlayer player, LangEntry langEntry) =>
            player.ChatMessage(GetMessage(player.UserIDString, langEntry));

        protected override void LoadDefaultMessages()
        {
            var englishLangKeys = new Dictionary<string, string>();

            foreach (var langEntry in LangEntry.AllLangEntries)
            {
                englishLangKeys[langEntry.Name] = langEntry.English;
            }

            lang.RegisterMessages(englishLangKeys, this, "en");
        }

        #endregion
    }
}
