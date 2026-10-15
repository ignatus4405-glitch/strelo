using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace StreloReminder.Models
{
    public enum CloudProviderType
    {
        Disabled = 0,
        GitHubGist = 1,
        CustomJsonUrl = 2,
        OneClickCloud = 3
    }

    [DataContract]
    public class CloudSyncConfig
    {
        private const string CodePrefix = "STRELO1:";
        private const string DeepLinkPrefix = "strelo://sync?code=";

        [DataMember(Order = 1)]
        public int ProviderTypeValue { get; set; }

        [DataMember(Order = 2)]
        public string GitHubToken { get; set; }

        [DataMember(Order = 3)]
        public string GistId { get; set; }

        [DataMember(Order = 4)]
        public string CustomCloudUrl { get; set; }

        [DataMember(Order = 5)]
        public int AutoSyncIntervalSeconds { get; set; }

        [DataMember(Order = 6)]
        public string OneClickRoomId { get; set; }

        [DataMember(Order = 7)]
        public bool LanWifiSyncEnabled { get; set; }

        [DataMember(Order = 8)]
        public string LanPcIp { get; set; }

        [DataMember(Order = 9)]
        public int LanPort { get; set; }

        public CloudSyncConfig()
        {
            ProviderType = CloudProviderType.Disabled;
            GitHubToken = string.Empty;
            GistId = string.Empty;
            CustomCloudUrl = string.Empty;
            AutoSyncIntervalSeconds = 15;
            OneClickRoomId = string.Empty;
            LanWifiSyncEnabled = true;
            LanPcIp = string.Empty;
            LanPort = 45892;
        }

        [IgnoreDataMember]
        public CloudProviderType ProviderType
        {
            get { return (CloudProviderType)ProviderTypeValue; }
            set { ProviderTypeValue = (int)value; }
        }

        public bool IsCloudConfigured()
        {
            if (ProviderType == CloudProviderType.OneClickCloud)
            {
                return !string.IsNullOrWhiteSpace(OneClickRoomId) || !string.IsNullOrWhiteSpace(CustomCloudUrl);
            }
            if (ProviderType == CloudProviderType.GitHubGist)
            {
                return !string.IsNullOrWhiteSpace(GitHubToken) && !string.IsNullOrWhiteSpace(GistId);
            }
            if (ProviderType == CloudProviderType.CustomJsonUrl)
            {
                return !string.IsNullOrWhiteSpace(CustomCloudUrl) &&
                       (CustomCloudUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        CustomCloudUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        public bool IsConfigured()
        {
            return IsCloudConfigured();
        }

        public bool IsAnySyncConfigured()
        {
            return IsCloudConfigured() || LanWifiSyncEnabled;
        }

        public string GetEffectiveOneClickUrl()
        {
            if (!string.IsNullOrWhiteSpace(CustomCloudUrl) &&
                (CustomCloudUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 CustomCloudUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return CustomCloudUrl.Trim();
            }

            if (!string.IsNullOrWhiteSpace(OneClickRoomId))
            {
                string id = OneClickRoomId.Trim();
                if (id.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    id.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
                return "https://jsonblob.com/api/jsonBlob/" + id;
            }

            return string.Empty;
        }

        public string ToConnectionCode()
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CloudSyncConfig));
                    serializer.WriteObject(ms, this);
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    return CodePrefix + base64;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public string ToQrDeepLink()
        {
            string code = ToConnectionCode();
            if (string.IsNullOrEmpty(code))
                return string.Empty;
            return DeepLinkPrefix + Uri.EscapeDataString(code);
        }

        public static CloudSyncConfig FromConnectionCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            string trimmed = code.Trim();

            // Если отсканирован QR-код со ссылкой strelo://sync?code=...
            int codeParamIdx = trimmed.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
            if (codeParamIdx >= 0)
            {
                trimmed = Uri.UnescapeDataString(trimmed.Substring(codeParamIdx + 5).Trim());
            }

            if (trimmed.StartsWith(CodePrefix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(CodePrefix.Length).Trim();
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(trimmed);
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CloudSyncConfig));
                    CloudSyncConfig parsed = serializer.ReadObject(ms) as CloudSyncConfig;
                    if (parsed != null)
                    {
                        if (parsed.LanPort <= 0) parsed.LanPort = 45892;
                        if (parsed.AutoSyncIntervalSeconds < 10) parsed.AutoSyncIntervalSeconds = 15;
                        return parsed;
                    }
                }
            }
            catch
            {
            }

            // Если пользователь вставил короткий ID комнаты Облака в 1 клик (например UUID jsonblob)
            if (trimmed.Length >= 8 && trimmed.Length <= 64 && !trimmed.Contains(" ") && trimmed.Contains("-"))
            {
                return new CloudSyncConfig
                {
                    ProviderType = CloudProviderType.OneClickCloud,
                    OneClickRoomId = trimmed,
                    CustomCloudUrl = "https://jsonblob.com/api/jsonBlob/" + trimmed,
                    LanWifiSyncEnabled = true,
                    AutoSyncIntervalSeconds = 15
                };
            }

            return null;
        }
    }
}
