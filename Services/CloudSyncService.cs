using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using StreloReminder.Models;

namespace StreloReminder.Services
{
    public class CloudSyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<ReminderItem> MergedReminders { get; set; }
        public bool LocalListUpdated { get; set; }
    }

    public static class CloudSyncService
    {
        private const string GistFileName = "strelo_reminders.json";
        private const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
        private const string SyncMarkerRaw = "STRELO_SYNC_V1:";
        private const string SyncMarkerGzip = "STRELO_SYNC_GZ:";
        private const string SyncMarkerEnd = "|END_STRELO";

        private static readonly string[] OneClickRelayBases = new string[]
        {
            "https://ntfy.sh",
            "https://ntfy.envs.net",
            "https://ntfy.tedomum.net"
        };

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, string> LastPushedByRoom = new Dictionary<string, string>();

        [DataContract]
        private class GistPayload
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

        [DataContract]
        private class GistFilesMap
        {
            [DataMember(Name = "strelo_reminders.json", EmitDefaultValue = false)]
            public GistFileContent StreloReminders { get; set; }
        }

        [DataContract]
        private class GistFileContent
        {
            [DataMember(Name = "content", EmitDefaultValue = false)]
            public string Content { get; set; }
        }

        static CloudSyncService()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public static string GenerateNewRoomId()
        {
            return "strelo-" + Guid.NewGuid().ToString("N").Substring(0, 10).ToLowerInvariant();
        }

        /// <summary>
        /// Создаёт «Облако в 1 клик» без регистрации и токенов.
        /// Генерирует уникальный ID комнаты локально (мгновенно, без риска ошибки 403) и отправляет начальное состояние в пул релеев.
        /// </summary>
        public static bool CreateOneClickCloudRoom(
            List<ReminderItem> initialReminders,
            out string roomId,
            out string cloudUrl,
            out string errorMessage)
        {
            roomId = GenerateNewRoomId();
            cloudUrl = "https://ntfy.sh/strelo_room_" + roomId;
            errorMessage = string.Empty;

            string remindersJson = StorageService.SerializeRemindersToJson(initialReminders ?? new List<ReminderItem>());
            if (string.IsNullOrWhiteSpace(remindersJson))
            {
                remindersJson = "[]";
            }

            try
            {
                PushOneClickCloudRoom(roomId, remindersJson);
            }
            catch
            {
                // Даже если внешний интернет временно недоступен, комната и QR-код создаются успешно
            }

            return true;
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
                    requestBody,
                    "application/json; charset=utf-8");

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
                bool remoteHadExistingData;
                string remoteJson = FetchRemoteRemindersJson(config, out remoteHadExistingData);
                List<ReminderItem> remoteList = StorageService.DeserializeRemindersFromJson(remoteJson);

                bool hasRemoteChanges;
                bool needsPushToCloud;
                List<ReminderItem> merged = MergeReminders(
                    localSnapshot ?? new List<ReminderItem>(),
                    remoteList,
                    out hasRemoteChanges,
                    out needsPushToCloud);

                string mergedJson = StorageService.SerializeRemindersToJson(merged);
                bool shouldPush = needsPushToCloud;

                if (!shouldPush && forcePush)
                {
                    if (!remoteHadExistingData && merged.Count > 0)
                    {
                        shouldPush = true;
                    }
                    else if (!string.Equals( NormalizeJson(mergedJson), NormalizeJson(remoteJson), StringComparison.Ordinal))
                    {
                        shouldPush = true;
                    }
                }

                if (shouldPush)
                {
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

        private static string NormalizeJson(string json)
        {
            return string.IsNullOrWhiteSpace(json) ? "[]" : json.Trim();
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

        private static string FetchRemoteRemindersJson(CloudSyncConfig config, out bool remoteHadExistingData)
        {
            remoteHadExistingData = false;

            if (config.ProviderType == CloudProviderType.OneClickCloud)
            {
                string roomId = CloudSyncConfig.SanitizeRoomId(config.OneClickRoomId);
                if (string.IsNullOrWhiteSpace(roomId))
                {
                    return "[]";
                }
                return FetchOneClickCloudRoom(roomId, out remoteHadExistingData);
            }

            if (config.ProviderType == CloudProviderType.GitHubGist)
            {
                string url = "https://api.github.com/gists/" + config.GistId.Trim();
                string response = SendHttpRequest(url, "GET", config.GitHubToken.Trim(), null, null);
                GistPayload payload = DeserializeGistPayload(response);
                if (payload != null && payload.Files != null && payload.Files.StreloReminders != null)
                {
                    remoteHadExistingData = true;
                    return payload.Files.StreloReminders.Content ?? "[]";
                }
                return "[]";
            }

            if (config.ProviderType == CloudProviderType.CustomJsonUrl)
            {
                string response = SendHttpRequest(config.CustomCloudUrl.Trim(), "GET", null, null, null);
                if (string.IsNullOrWhiteSpace(response) || response.Trim() == "null")
                {
                    return "[]";
                }
                remoteHadExistingData = true;
                return response;
            }

            return "[]";
        }

        private static void PushRemoteRemindersJson(CloudSyncConfig config, string remindersJson)
        {
            string normalized = string.IsNullOrWhiteSpace(remindersJson) ? "[]" : remindersJson;

            if (config.ProviderType == CloudProviderType.OneClickCloud)
            {
                string roomId = CloudSyncConfig.SanitizeRoomId(config.OneClickRoomId);
                if (!string.IsNullOrWhiteSpace(roomId))
                {
                    PushOneClickCloudRoom(roomId, normalized);
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
                            Content = normalized
                        }
                    }
                };
                string body = SerializeGistPayload(payload);
                SendHttpRequest(url, "PATCH", config.GitHubToken.Trim(), body, "application/json; charset=utf-8");
            }
            else if (config.ProviderType == CloudProviderType.CustomJsonUrl)
            {
                SendHttpRequest(config.CustomCloudUrl.Trim(), "PUT", null, normalized, "application/json; charset=utf-8");
            }
        }

        private static string FetchOneClickCloudRoom(string roomId, out bool hasExistingData)
        {
            hasExistingData = false;
            string topic = "strelo_room_" + CloudSyncConfig.SanitizeRoomId(roomId);
            Exception lastException = null;
            bool anyRelayReachable = false;

            for (int i = 0; i < OneClickRelayBases.Length; i++)
            {
                string url = OneClickRelayBases[i] + "/" + topic + "/raw?poll=1&since=all";
                try
                {
                    string rawResponse = SendHttpRequest(url, "GET", null, null, null, 8000);
                    anyRelayReachable = true;

                    string decodedJson;
                    if (TryExtractLatestOneClickJson(rawResponse, out decodedJson))
                    {
                        hasExistingData = true;
                        lock (CacheLock)
                        {
                            LastPushedByRoom[roomId] = decodedJson;
                        }
                        return decodedJson;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            if (anyRelayReachable)
            {
                return "[]";
            }

            if (lastException != null)
            {
                throw lastException;
            }
            return "[]";
        }

        private static void PushOneClickCloudRoom(string roomId, string remindersJson)
        {
            string cleanRoom = CloudSyncConfig.SanitizeRoomId(roomId);
            if (string.IsNullOrEmpty(cleanRoom))
                return;

            lock (CacheLock)
            {
                string lastPushed;
                if (LastPushedByRoom.TryGetValue(cleanRoom, out lastPushed) &&
                    string.Equals(lastPushed, remindersJson, StringComparison.Ordinal))
                {
                    return;
                }
            }

            string topic = "strelo_room_" + cleanRoom;
            string encodedBody = EncodeOneClickPayload(remindersJson);
            Exception lastException = null;
            bool pushedAtLeastOnce = false;

            for (int i = 0; i < OneClickRelayBases.Length; i++)
            {
                string url = OneClickRelayBases[i] + "/" + topic;
                try
                {
                    SendHttpRequest(url, "POST", null, encodedBody, "text/plain; charset=utf-8", 8000);
                    pushedAtLeastOnce = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }

            if (pushedAtLeastOnce)
            {
                lock (CacheLock)
                {
                    LastPushedByRoom[cleanRoom] = remindersJson;
                }
                return;
            }

            if (lastException != null)
            {
                throw lastException;
            }
        }

        public static string EncodeOneClickPayload(string json)
        {
            string safeJson = string.IsNullOrWhiteSpace(json) ? "[]" : json.Trim();
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(safeJson);

            if (utf8Bytes.Length <= 2200)
            {
                return SyncMarkerRaw + Convert.ToBase64String(utf8Bytes) + SyncMarkerEnd;
            }

            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress, true))
                {
                    gz.Write(utf8Bytes, 0, utf8Bytes.Length);
                }
                return SyncMarkerGzip + Convert.ToBase64String(ms.ToArray()) + SyncMarkerEnd;
            }
        }

        public static bool TryExtractLatestOneClickJson(string responseBody, out string decodedJson)
        {
            decodedJson = "[]";
            if (string.IsNullOrWhiteSpace(responseBody))
                return false;

            int lastRawIdx = responseBody.LastIndexOf(SyncMarkerRaw, StringComparison.Ordinal);
            int lastGzIdx = responseBody.LastIndexOf(SyncMarkerGzip, StringComparison.Ordinal);

            if (lastRawIdx < 0 && lastGzIdx < 0)
            {
                string trimmed = responseBody.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    decodedJson = trimmed;
                    return true;
                }
                return false;
            }

            bool isGzip = lastGzIdx > lastRawIdx;
            int markerStart = isGzip ? lastGzIdx : lastRawIdx;
            string marker = isGzip ? SyncMarkerGzip : SyncMarkerRaw;
            int payloadStart = markerStart + marker.Length;

            int endIdx = responseBody.IndexOf(SyncMarkerEnd, payloadStart, StringComparison.Ordinal);
            string base64Part;
            if (endIdx >= payloadStart)
            {
                base64Part = responseBody.Substring(payloadStart, endIdx - payloadStart).Trim();
            }
            else
            {
                int lineEnd = responseBody.IndexOfAny(new char[] { '\r', '\n', '"', ' ' }, payloadStart);
                base64Part = (lineEnd >= payloadStart)
                    ? responseBody.Substring(payloadStart, lineEnd - payloadStart).Trim()
                    : responseBody.Substring(payloadStart).Trim();
            }

            if (string.IsNullOrEmpty(base64Part))
                return false;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64Part);
                if (!isGzip)
                {
                    decodedJson = Encoding.UTF8.GetString(bytes);
                    return true;
                }

                using (MemoryStream inMs = new MemoryStream(bytes))
                using (GZipStream gz = new GZipStream(inMs, CompressionMode.Decompress))
                using (MemoryStream outMs = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outMs.Write(buffer, 0, read);
                    }
                    decodedJson = Encoding.UTF8.GetString(outMs.ToArray());
                    return true;
                }
            }
            catch
            {
                return false;
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

        private static string SendHttpRequest(
            string url,
            string method,
            string bearerToken,
            string body,
            string contentType,
            int timeoutMs = 12000)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.UserAgent = BrowserUserAgent;
            request.Accept = "application/json, text/plain, */*";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers["Authorization"] = "Bearer " + bearerToken;
            }

            if (!string.IsNullOrEmpty(body))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = string.IsNullOrWhiteSpace(contentType)
                    ? "application/json; charset=utf-8"
                    : contentType;
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
