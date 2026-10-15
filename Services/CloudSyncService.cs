using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using StreloReminder.Models;

namespace StreloReminder.Services
{
    [DataContract]
    public class GistFileContent
    {
        [DataMember(Name = "content")]
        public string Content { get; set; }
    }

    [DataContract]
    public class GistFilesMap
    {
        [DataMember(Name = "strelo_reminders.json")]
        public GistFileContent StreloReminders { get; set; }
    }

    [DataContract]
    public class GistPayload
    {
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "description", EmitDefaultValue = false)]
        public string Description { get; set; }

        [DataMember(Name = "public", EmitDefaultValue = false)]
        public bool IsPublic { get; set; }

        [DataMember(Name = "files", EmitDefaultValue = false)]
        public GistFilesMap Files { get; set; }
    }

    public class CloudSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<ReminderItem> MergedReminders { get; set; }
        public bool LocalListUpdated { get; set; }
    }

    public static class CloudSyncService
    {
        static CloudSyncService()
        {
            try
            {
                // Включаем поддержку TLS 1.2 (3072) для .NET Framework 4.x
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch
            {
            }
        }

        /// <summary>
        /// Создаёт «Облако в 1 клик» без регистрации и токенов.
        /// Возвращает короткий идентификатор комнаты (roomId) и полный URL облачной ячейки.
        /// </summary>
        public static bool CreateOneClickCloudRoom(
            List<ReminderItem> initialReminders,
            out string roomId,
            out string cloudUrl,
            out string errorMessage)
        {
            roomId = string.Empty;
            cloudUrl = string.Empty;
            errorMessage = string.Empty;

            string remindersJson = StorageService.SerializeRemindersToJson(initialReminders ?? new List<ReminderItem>());
            if (string.IsNullOrWhiteSpace(remindersJson))
            {
                remindersJson = "[]";
            }

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://jsonblob.com/api/jsonBlob");
                request.Method = "POST";
                request.UserAgent = "StreloReminder-SyncClient/1.4";
                request.ContentType = "application/json; charset=utf-8";
                request.Accept = "application/json";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;

                byte[] bodyBytes = Encoding.UTF8.GetBytes(remindersJson);
                request.ContentLength = bodyBytes.Length;
                using (Stream reqStream = request.GetRequestStream())
                {
                    reqStream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    string location = response.Headers["Location"] ?? response.Headers["location"] ?? string.Empty;
                    string blobHeader = response.Headers["X-jsonblob"] ?? response.Headers["x-jsonblob"] ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(blobHeader))
                    {
                        roomId = blobHeader.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(location))
                    {
                        int lastSlash = location.TrimEnd('/').LastIndexOf('/');
                        if (lastSlash >= 0 && lastSlash + 1 < location.Length)
                        {
                            roomId = location.Substring(lastSlash + 1).Trim();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(roomId))
                    {
                        cloudUrl = "https://jsonblob.com/api/jsonBlob/" + roomId;
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(location))
                    {
                        cloudUrl = location.Trim();
                        if (cloudUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                        {
                            cloudUrl = "https://" + cloudUrl.Substring(7);
                        }
                        roomId = cloudUrl;
                        return true;
                    }
                }

                errorMessage = "Облачный сервис не вернул идентификатор созданной комнаты.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = "Не удалось создать облако в 1 клик: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Создаёт новое секретное облачное хранилище GitHub Gist и возвращает его Gist ID.
        /// </summary>
        public static bool CreateNewGist(
            string githubToken,
            List<ReminderItem> initialReminders,
            out string createdGistId,
            out string errorMessage)
        {
            createdGistId = string.Empty;
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(githubToken))
            {
                errorMessage = "Укажите персональный токен GitHub (Personal Access Token с правом gist).";
                return false;
            }

            try
            {
                string remindersJson = StorageService.SerializeRemindersToJson(initialReminders ?? new List<ReminderItem>());
                GistPayload requestPayload = new GistPayload
                {
                    Description = "StreloReminder Cloud Sync (PC <-> Android)",
                    IsPublic = false,
                    Files = new GistFilesMap
                    {
                        StreloReminders = new GistFileContent
                        {
                            Content = string.IsNullOrWhiteSpace(remindersJson) ? "[]" : remindersJson
                        }
                    }
                };

                string requestBody = SerializeGistPayload(requestPayload);
                string responseBody = SendHttpRequest(
                    "https://api.github.com/gists",
                    "POST",
                    githubToken.Trim(),
                    requestBody);

                GistPayload responsePayload = DeserializeGistPayload(responseBody);
                if (responsePayload == null || string.IsNullOrWhiteSpace(responsePayload.Id))
                {
                    errorMessage = "Сервер GitHub не вернул ID созданного Gist.";
                    return false;
                }

                createdGistId = responsePayload.Id.Trim();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Ошибка создания Gist: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Выполняет двустороннюю синхронизацию между локальным списком напоминаний и облаком.
        /// </summary>
        public static CloudSyncResult Synchronize(CloudSyncConfig config, List<ReminderItem> localSnapshot, bool forcePush = false)
        {
            CloudSyncResult result = new CloudSyncResult
            {
                Success = false,
                MergedReminders = localSnapshot ?? new List<ReminderItem>(),
                LocalListUpdated = false
            };

            if (config == null || !config.IsCloudConfigured())
            {
                result.Message = "Облачная синхронизация не настроена.";
                return result;
            }

            try
            {
                string remoteJson = FetchRemoteRemindersJson(config);
                List<ReminderItem> remoteList = StorageService.DeserializeRemindersFromJson(remoteJson);

                bool hasRemoteChanges;
                bool needsPushToCloud;
                List<ReminderItem> merged = MergeReminders(
                    localSnapshot ?? new List<ReminderItem>(),
                    remoteList,
                    out hasRemoteChanges,
                    out needsPushToCloud);

                if (needsPushToCloud || forcePush || string.IsNullOrWhiteSpace(remoteJson))
                {
                    string mergedJson = StorageService.SerializeRemindersToJson(merged);
                    PushRemoteRemindersJson(config, mergedJson);
                }

                result.Success = true;
                result.MergedReminders = merged;
                result.LocalListUpdated = hasRemoteChanges;
                result.Message = string.Format("Облако синхр. ({0:HH:mm:ss})", DateTime.Now);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Ошибка облака: " + ex.Message;
                return result;
            }
        }

        public static List<ReminderItem> MergeReminders(
            List<ReminderItem> localList,
            List<ReminderItem> remoteList,
            out bool hasChangesFromRemote,
            out bool hasChangesForRemote)
        {
            hasChangesFromRemote = false;
            hasChangesForRemote = false;

            Dictionary<string, ReminderItem> localMap = new Dictionary<string, ReminderItem>();
            for (int i = 0; i < localList.Count; i++)
            {
                ReminderItem item = localList[i];
                if (item != null && !string.IsNullOrEmpty(item.Id))
                {
                    localMap[item.Id] = item;
                }
            }

            Dictionary<string, ReminderItem> remoteMap = new Dictionary<string, ReminderItem>();
            for (int i = 0; i < remoteList.Count; i++)
            {
                ReminderItem item = remoteList[i];
                if (item != null && !string.IsNullOrEmpty(item.Id))
                {
                    remoteMap[item.Id] = item;
                }
            }

            List<ReminderItem> merged = new List<ReminderItem>();
            HashSet<string> processedIds = new HashSet<string>();

            foreach (KeyValuePair<string, ReminderItem> kvp in localMap)
            {
                string id = kvp.Key;
                ReminderItem localItem = kvp.Value;
                processedIds.Add(id);

                ReminderItem remoteItem;
                if (!remoteMap.TryGetValue(id, out remoteItem))
                {
                    merged.Add(localItem.Clone());
                    hasChangesForRemote = true;
                }
                else
                {
                    if (remoteItem.UpdatedAtUnixMs > localItem.UpdatedAtUnixMs)
                    {
                        ReminderItem winner = remoteItem.Clone();
                        merged.Add(winner);
                        hasChangesFromRemote = true;
                    }
                    else if (localItem.UpdatedAtUnixMs > remoteItem.UpdatedAtUnixMs)
                    {
                        merged.Add(localItem.Clone());
                        hasChangesForRemote = true;
                    }
                    else
                    {
                        merged.Add(localItem.Clone());
                    }
                }
            }

            foreach (KeyValuePair<string, ReminderItem> kvp in remoteMap)
            {
                if (!processedIds.Contains(kvp.Key))
                {
                    merged.Add(kvp.Value.Clone());
                    hasChangesFromRemote = true;
                }
            }

            return merged;
        }

        private static string FetchRemoteRemindersJson(CloudSyncConfig config)
        {
            if (config.ProviderType == CloudProviderType.OneClickCloud)
            {
                string url = config.GetEffectiveOneClickUrl();
                if (string.IsNullOrWhiteSpace(url))
                    return "[]";
                string response = SendHttpRequest(url, "GET", null, null);
                if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null")
                {
                    return "[]";
                }
                return response;
            }
            if (config.ProviderType == CloudProviderType.GitHubGist)
            {
                string url = "https://api.github.com/gists/" + config.GistId.Trim();
                string response = SendHttpRequest(url, "GET", config.GitHubToken.Trim(), null);
                GistPayload payload = DeserializeGistPayload(response);
                if (payload != null && payload.Files != null && payload.Files.StreloReminders != null)
                {
                    return payload.Files.StreloReminders.Content ?? "[]";
                }
                return "[]";
            }
            if (config.ProviderType == CloudProviderType.CustomJsonUrl)
            {
                string response = SendHttpRequest(config.CustomCloudUrl.Trim(), "GET", null, null);
                if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null")
                {
                    return "[]";
                }
                return response;
            }
            return "[]";
        }

        private static void PushRemoteRemindersJson(CloudSyncConfig config, string remindersJson)
        {
            if (config.ProviderType == CloudProviderType.OneClickCloud)
            {
                string url = config.GetEffectiveOneClickUrl();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    SendHttpRequest(url, "PUT", null, string.IsNullOrWhiteSpace(remindersJson) ? "[]" : remindersJson);
                }
            }
            else if (config.ProviderType == CloudProviderType.GitHubGist)
            {
                string url = "https://api.github.com/gists/" + config.GistId.Trim();
                GistPayload payload = new GistPayload
                {
                    Description = "StreloReminder Cloud Sync (PC <-> Android)",
                    Files = new GistFilesMap
                    {
                        StreloReminders = new GistFileContent
                        {
                            Content = string.IsNullOrWhiteSpace(remindersJson) ? "[]" : remindersJson
                        }
                    }
                };
                string body = SerializeGistPayload(payload);
                SendHttpRequest(url, "PATCH", config.GitHubToken.Trim(), body);
            }
            else if (config.ProviderType == CloudProviderType.CustomJsonUrl)
            {
                SendHttpRequest(config.CustomCloudUrl.Trim(), "PUT", null, remindersJson);
            }
        }

        private static string SerializeGistPayload(GistPayload payload)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GistPayload));
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static GistPayload DeserializeGistPayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GistPayload));
                return serializer.ReadObject(ms) as GistPayload;
            }
        }

        private static string SendHttpRequest(string url, string method, string bearerToken, string jsonBody)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.UserAgent = "StreloReminder-SyncClient/1.4";
            request.Accept = "application/vnd.github+json, application/json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;

            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers["Authorization"] = "Bearer " + bearerToken;
            }

            if (!string.IsNullOrEmpty(jsonBody))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                request.ContentType = "application/json; charset=utf-8";
                request.ContentLength = bodyBytes.Length;
                using (Stream reqStream = request.GetRequestStream())
                {
                    reqStream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (Stream respStream = response.GetResponseStream())
                    {
                        if (respStream == null) return string.Empty;
                        using (StreamReader reader = new StreamReader(respStream, Encoding.UTF8))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    HttpWebResponse errResp = wex.Response as HttpWebResponse;
                    if (errResp != null)
                    {
                        throw new Exception(string.Format("HTTP {0} ({1})", (int)errResp.StatusCode, errResp.StatusDescription));
                    }
                }
                throw;
            }
        }
    }
}
