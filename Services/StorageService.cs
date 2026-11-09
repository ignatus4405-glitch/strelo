using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using StreloReminder.Models;

namespace StreloReminder.Services
{
    public static class StorageService
    {
        private const string AppFolderName = "StreloReminder";
        private const string FileName = "reminders.json";
        private const string CloudConfigFileName = "cloud_sync.json";
        private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegistryValueName = "StreloReminder";

        public static string GetStorageDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, AppFolderName);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public static string GetStorageFilePath()
        {
            return Path.Combine(GetStorageDirectory(), FileName);
        }

        public static string GetCloudConfigFilePath()
        {
            return Path.Combine(GetStorageDirectory(), CloudConfigFileName);
        }

        public static string SerializeRemindersToJson(List<ReminderItem> reminders)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<ReminderItem>));
                serializer.WriteObject(ms, reminders ?? new List<ReminderItem>());
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static List<ReminderItem> DeserializeRemindersFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<ReminderItem>();
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json.Trim());
                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<ReminderItem>));
                    List<ReminderItem> list = serializer.ReadObject(ms) as List<ReminderItem>;
                    return list ?? new List<ReminderItem>();
                }
            }
            catch (Exception)
            {
                return new List<ReminderItem>();
            }
        }

        public static List<ReminderItem> LoadReminders()
        {
            string path = GetStorageFilePath();
            if (!File.Exists(path))
            {
                return new List<ReminderItem>();
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                return DeserializeRemindersFromJson(json);
            }
            catch (Exception)
            {
                return new List<ReminderItem>();
            }
        }

        public static void SaveReminders(List<ReminderItem> reminders)
        {
            try
            {
                string path = GetStorageFilePath();
                string json = SerializeRemindersToJson(reminders);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Игнорируем ошибки записи, чтобы не прерывать работу таймера
            }
        }

        public static CloudSyncConfig LoadCloudConfig()
        {
            string path = GetCloudConfigFilePath();
            if (!File.Exists(path))
            {
                return new CloudSyncConfig();
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                {
                    return new CloudSyncConfig();
                }

                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CloudSyncConfig));
                    CloudSyncConfig cfg = serializer.ReadObject(ms) as CloudSyncConfig;
                    return cfg ?? new CloudSyncConfig();
                }
            }
            catch (Exception)
            {
                return new CloudSyncConfig();
            }
        }

        public static void SaveCloudConfig(CloudSyncConfig config)
        {
            try
            {
                string path = GetCloudConfigFilePath();
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CloudSyncConfig));
                    serializer.WriteObject(ms, config ?? new CloudSyncConfig());
                    File.WriteAllBytes(path, ms.ToArray());
                }
            }
            catch (Exception)
            {
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(RegistryValueName);
                    return val != null && !string.IsNullOrEmpty(val.ToString());
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        string exePath = Application.ExecutablePath;
                        key.SetValue(RegistryValueName, "\"" + exePath + "\" --minimized");
                    }
                    else
                    {
                        if (key.GetValue(RegistryValueName) != null)
                        {
                            key.DeleteValue(RegistryValueName, false);
                        }
                    }
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
