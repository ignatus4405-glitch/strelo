using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace StreloReminder.Services
{
    [DataContract]
    public class GitHubReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
    }

    [DataContract]
    public class GitHubReleaseInfo
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "body")]
        public string Body { get; set; }

        [DataMember(Name = "html_url")]
        public string HtmlUrl { get; set; }

        [DataMember(Name = "assets")]
        public List<GitHubReleaseAsset> Assets { get; set; }
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
        public string ExeDownloadUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class UpdateService
    {
        /// <summary>
        /// Текущая версия приложения. Автоматически обновляется в GitHub Actions при каждой сборке.
        /// </summary>
        public const string CurrentVersion = "1.0.0";

        public const string GitHubRepoOwner = "ignatus4405-glitch";
        public const string GitHubRepoName = "strelo";

        static UpdateService()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch
            {
            }
        }

        public static UpdateCheckResult CheckForUpdates()
        {
            UpdateCheckResult result = new UpdateCheckResult
            {
                Success = false,
                IsUpdateAvailable = false,
                CurrentVersion = NormalizeVersion(CurrentVersion),
                LatestVersion = NormalizeVersion(CurrentVersion),
                ReleaseUrl = string.Format("https://github.com/{0}/{1}/releases", GitHubRepoOwner, GitHubRepoName)
            };

            try
            {
                string apiUrl = string.Format(
                    "https://api.github.com/repos/{0}/{1}/releases/latest",
                    GitHubRepoOwner,
                    GitHubRepoName);

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiUrl);
                req.Method = "GET";
                req.UserAgent = "StreloReminder-Updater/" + CurrentVersion;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 12000;
                req.ReadWriteTimeout = 12000;

                string json;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    using (Stream stream = resp.GetResponseStream())
                    {
                        if (stream == null)
                        {
                            result.ErrorMessage = "Пустой ответ от сервера обновлений GitHub.";
                            return result;
                        }
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            json = reader.ReadToEnd();
                        }
                    }
                }

                GitHubReleaseInfo release = DeserializeRelease(json);
                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                {
                    result.ErrorMessage = "Не удалось прочитать информацию о релизе.";
                    return result;
                }

                string latestVer = NormalizeVersion(release.TagName);
                result.Success = true;
                result.LatestVersion = latestVer;
                result.ReleaseNotes = release.Body ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
                {
                    result.ReleaseUrl = release.HtmlUrl;
                }

                if (release.Assets != null)
                {
                    for (int i = 0; i < release.Assets.Count; i++)
                    {
                        GitHubReleaseAsset asset = release.Assets[i];
                        if (asset != null &&
                            !string.IsNullOrWhiteSpace(asset.Name) &&
                            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            result.ExeDownloadUrl = asset.BrowserDownloadUrl;
                            break;
                        }
                    }
                }

                result.IsUpdateAvailable = CompareVersions(latestVer, result.CurrentVersion) > 0;
                return result;
            }
            catch (WebException wex)
            {
                HttpWebResponse errResp = wex.Response as HttpWebResponse;
                if (errResp != null && errResp.StatusCode == HttpStatusCode.NotFound)
                {
                    // Релизов ещё нет или репозиторий в процессе публикации первого релиза
                    result.Success = true;
                    result.IsUpdateAvailable = false;
                    return result;
                }
                result.ErrorMessage = "Ошибка проверки обновлений: " + wex.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Ошибка проверки обновлений: " + ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Скачивает новый StreloReminder.exe из релиза GitHub, создаёт скрипт замены и перезапускает приложение.
        /// </summary>
        public static bool DownloadAndInstallUpdate(string exeDownloadUrl, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(exeDownloadUrl))
            {
                errorMessage = "Ссылка на скачивание .exe не найдена в релизе.";
                return false;
            }

            try
            {
                string currentExePath = Application.ExecutablePath;
                string exeDir = Path.GetDirectoryName(currentExePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string tempNewExe = Path.Combine(exeDir, "StreloReminder_update.exe");
                string updaterBat = Path.Combine(exeDir, "strelo_apply_update.bat");

                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(exeDownloadUrl);
                req.Method = "GET";
                req.UserAgent = "StreloReminder-Updater/" + CurrentVersion;
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    using (Stream respStream = resp.GetResponseStream())
                    {
                        if (respStream == null)
                        {
                            errorMessage = "Не удалось получить поток файла обновления.";
                            return false;
                        }
                        using (FileStream fs = new FileStream(tempNewExe, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[16384];
                            int read;
                            while ((read = respStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                fs.Write(buffer, 0, read);
                            }
                        }
                    }
                }

                // Создаём bat-скрипт, который дождётся закрытия текущего процесса, заменит exe и запустит новую версию
                string batContent =
                    "@echo off\r\n" +
                    "chcp 65001 >nul\r\n" +
                    "ping 127.0.0.1 -n 3 >nul\r\n" +
                    "move /Y \"" + tempNewExe + "\" \"" + currentExePath + "\" >nul\r\n" +
                    "start \"\" \"" + currentExePath + "\"\r\n" +
                    "del \"%~f0\"\r\n";

                File.WriteAllText(updaterBat, batContent, Encoding.UTF8);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = updaterBat,
                    WorkingDirectory = exeDir,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Не удалось установить обновление автоматически: " + ex.Message;
                return false;
            }
        }

        public static string NormalizeVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return "1.0.0";

            string s = v.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(1).Trim();
            }
            return s;
        }

        public static int CompareVersions(string v1, string v2)
        {
            string[] parts1 = NormalizeVersion(v1).Split('.');
            string[] parts2 = NormalizeVersion(v2).Split('.');
            int len = Math.Max(parts1.Length, parts2.Length);

            for (int i = 0; i < len; i++)
            {
                int n1 = 0;
                int n2 = 0;
                if (i < parts1.Length) int.TryParse(parts1[i], out n1);
                if (i < parts2.Length) int.TryParse(parts2[i], out n2);

                if (n1 != n2)
                {
                    return n1 > n2 ? 1 : -1;
                }
            }
            return 0;
        }

        private static GitHubReleaseInfo DeserializeRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubReleaseInfo));
                return serializer.ReadObject(ms) as GitHubReleaseInfo;
            }
        }
    }
}
