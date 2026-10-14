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

        public static List<ReminderItem> LoadReminders()
        {
            string path = GetStorageFilePath();
            if (!File.Exists(path))
            {
                return new List<ReminderItem>();
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                {
                    return new List<ReminderItem>();
                }

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

        public static void SaveReminders(List<ReminderItem> reminders)
        {
            try
            {
                string path = GetStorageFilePath();
                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<ReminderItem>));
                    serializer.WriteObject(ms, reminders ?? new List<ReminderItem>());
                    byte[] jsonBytes = ms.ToArray();
                    File.WriteAllBytes(path, jsonBytes);
                }
            }
            catch (Exception)
            {
                // Игнорируем ошибки записи, чтобы не прерывать работу таймера
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
