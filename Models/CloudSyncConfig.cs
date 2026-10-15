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

        /// <summary>
        /// Формирует компактную ссылку для QR-кода, чтобы QR-матрица имела минимальный размер и легко читалась камерой.
        /// </summary>
        public string ToQrDeepLink()
        {
            if (ProviderType == CloudProviderType.OneClickCloud || ProviderType == CloudProviderType.Disabled)
            {
                StringBuilder sb = new StringBuilder("strelo://sync?v=1");
                sb.Append("&p=").Append(ProviderTypeValue);
                if (!string.IsNullOrWhiteSpace(OneClickRoomId))
                {
                    sb.Append("&r=").Append(Uri.EscapeDataString(OneClickRoomId.Trim()));
                }
                sb.Append("&w=").Append(LanWifiSyncEnabled ? "1" : "0");
                if (!string.IsNullOrWhiteSpace(LanPcIp))
                {
                    sb.Append("&ip=").Append(Uri.EscapeDataString(LanPcIp.Trim()));
                }
                if (LanPort > 0 && LanPort != 45892)
                {
                    sb.Append("&pt=").Append(LanPort);
                }
                sb.Append("&i=").Append(Math.Max(3, AutoSyncIntervalSeconds));
                return sb.ToString();
            }

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

            // Компактный формат QR-кода strelo://sync?v=1&p=...
            if (trimmed.StartsWith("strelo://sync?", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("v=1", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    CloudSyncConfig cfg = new CloudSyncConfig();
                    int qIdx = trimmed.IndexOf('?');
                    string query = qIdx >= 0 ? trimmed.Substring(qIdx + 1) : trimmed;
                    string[] pairs = query.Split('&');
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        string[] kv = pairs[i].Split(new char[] { '=' }, 2);
                        if (kv.Length != 2) continue;
                        string k = kv[0].Trim().ToLowerInvariant();
                        string v = Uri.UnescapeDataString(kv[1].Trim());
                        if (k == "p")
                        {
                            int pv;
                            if (int.TryParse(v, out pv)) cfg.ProviderTypeValue = pv;
                        }
                        else if (k == "r")
                        {
                            cfg.OneClickRoomId = v;
                            if (!string.IsNullOrEmpty(v) && !v.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                cfg.CustomCloudUrl = "https://jsonblob.com/api/jsonBlob/" + v;
                            }
                        }
                        else if (k == "w")
                        {
                            cfg.LanWifiSyncEnabled = (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));
                        }
                        else if (k == "ip")
                        {
                            cfg.LanPcIp = v;
                        }
                        else if (k == "pt")
                        {
                            int pt;
                            if (int.TryParse(v, out pt) && pt > 0) cfg.LanPort = pt;
                        }
                        else if (k == "i")
                        {
                            int iv;
                            if (int.TryParse(v, out iv) && iv >= 3) cfg.AutoSyncIntervalSeconds = iv;
                        }
                    }
                    return cfg;
                }
                catch
                {
                }
            }

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
                        if (parsed.AutoSyncIntervalSeconds < 3) parsed.AutoSyncIntervalSeconds = 15;
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
