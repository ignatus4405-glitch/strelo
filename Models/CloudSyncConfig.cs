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
        CustomJsonUrl = 2
    }

    [DataContract]
    public class CloudSyncConfig
    {
        private const string CodePrefix = "STRELO1:";

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

        public CloudSyncConfig()
        {
            ProviderType = CloudProviderType.Disabled;
            GitHubToken = string.Empty;
            GistId = string.Empty;
            CustomCloudUrl = string.Empty;
            AutoSyncIntervalSeconds = 20;
        }

        [IgnoreDataMember]
        public CloudProviderType ProviderType
        {
            get { return (CloudProviderType)ProviderTypeValue; }
            set { ProviderTypeValue = (int)value; }
        }

        public bool IsConfigured()
        {
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

        public static CloudSyncConfig FromConnectionCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            string trimmed = code.Trim();
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
                    return serializer.ReadObject(ms) as CloudSyncConfig;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
