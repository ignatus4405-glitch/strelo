# Руководство для ИИ-агентов и разработчиков (AGENTS.md)

Этот документ содержит полное описание архитектуры проекта **StreloReminder** (десктопного приложения Windows + мобильного приложения Android с двусторонней облачной синхронизацией), технических ограничений и обязательного регламента работы. Любой агент, продолжающий разработку в этом репозитории, **обязан** ознакомиться с этим файлом перед внесением изменений и строго следовать регламенту документирования и автосборки.

---

## 1. Обзор проекта

**StreloReminder** — кроссплатформенная система напоминаний для **Windows 10/11 (C# WinForms)** и **Android 7.0+ (Java Android SDK)** с полной **двусторонней облачной синхронизацией через интернет**.
- На **Windows** приложение работает в системном трее (`NotifyIcon`) и выводит всплывающее окно поверх всех окон (`TopMost = true`) со звуковым сигналом и кнопками откладывания.
- На **Android** приложение планирует точные будильники через `AlarmManager`, выводит системные уведомления высокой важности (`IMPORTANCE_HIGH`) и полноэкранное всплывающее окно (`ReminderAlertActivity`) поверх блокировки экрана.
- **Обмен данными между ПК и телефоном** происходит автоматически через облако (**GitHub Gist** или любой **Cloud JSON URL**) с разрешением конфликтов по метке времени `UpdatedAtUnixMs` и поддержкой мягкого удаления (`IsDeleted`). Для мгновенной связки компьютера и телефона используется единая строка подключения **`STRELO1:...`**.

---

## 2. Архитектура и карта файлов

```text
StreloReminder/
├── .github/
│   └── workflows/
│       └── build.yml               # CI/CD пайплайн GitHub Actions (автосборка Windows EXE + Android APK)
├── Forms/
│   ├── MainForm.cs                 # Главное окно Windows: редактор, таблица, таймер, трей, фоновая синхронизация
│   ├── PopupNotificationForm.cs    # Всплывающее окно уведомления поверх всех окон (TopMost + Snooze)
│   └── CloudSyncForm.cs            # Диалог настройки облачной синхронизации ПК ↔ Android (Gist / Cloud URL / код STRELO1)
├── Models/
│   ├── ReminderItem.cs             # Модель данных напоминания (DataContract), UpdatedAtUnixMs, IsDeleted, расчёт повторов
│   └── CloudSyncConfig.cs          # Конфигурация облака и генератор кода быстрого подключения STRELO1:...
├── Services/
│   ├── IconGenerator.cs            # Программная отрисовка иконки колокольчика через System.Drawing (GDI+)
│   ├── StorageService.cs           # Сериализация напоминаний и настроек облака в %APPDATA%\StreloReminder\
│   └── CloudSyncService.cs         # Двусторонняя синхронизация и слияние (MergeReminders) через GitHub Gist / Cloud URL
├── android/                        # Нативное мобильное приложение для Android (Java, minSdk 24, targetSdk 34)
│   ├── settings.gradle, build.gradle, gradle.properties
│   └── app/
│       ├── build.gradle
│       └── src/main/
│           ├── AndroidManifest.xml
│           ├── res/drawable/ic_bell.xml
│           └── java/com/strelo/reminder/
│               ├── MainActivity.java           # Главный экран Android: список, редактор 6 единиц времени, настройка облака
│               ├── ReminderAlertActivity.java  # Всплывающее окно уведомления на Android (звук, вибрация, Snooze)
│               ├── AlarmScheduler.java         # Планировщик точных будильников AlarmManager + NotificationChannel
│               ├── ReminderAlarmReceiver.java  # BroadcastReceiver срабатывания будильников и BOOT_COMPLETED
│               ├── ReminderItem.java           # Модель напоминания (100% совместимая по JSON с Windows ReminderItem.cs)
│               ├── CloudSyncConfig.java        # Настройки облака и парсер кода STRELO1:...
│               ├── CloudSyncService.java       # Двусторонняя синхронизация Android ↔ Облако ↔ ПК
│               └── StorageService.java         # Хранение данных в SharedPreferences
├── Program.cs                      # Точка входа Windows (Main), защита от повторного запуска через именованный Mutex
├── StreloReminder.csproj           # Проектный файл SDK-стиля (net8.0-windows, UseWindowsForms=true)
├── StreloReminder.sln              # Файл решения Visual Studio 2022
├── build.bat                       # Локальный скрипт сборки в 1 клик через встроенный в Windows csc.exe или dotnet
├── .gitattributes                  # Правила окончаний строк (CRLF для .bat, .sln, .cs, .csproj)
├── .gitignore                      # Исключения Git (bin/, obj/, *.exe, *.apk, android/build и т.д.)
├── AGENTS.md                       # Данное руководство для агентов
├── CHANGELOG.md                    # Подробный журнал всех изменений (обязателен к ведению)
└── README.md                       # Пользовательская документация
```

### Детальное описание компонентов

1. **`Models/ReminderItem.cs` (Windows) и `ReminderItem.java` (Android)**:
   - Схема JSON полностью идентична между платформами:
     `Id`, `Title`, `Notes`, `TriggerTimeIso`, `RecurrenceValue`, `IntervalMinutes`, `PriorityValue`, `IsEnabled`, `IsCompleted`, `PlaySound`, `UpdatedAtUnixMs`, `IsDeleted`.
   - При любом изменении вызывается `Touch()` / `touch()`, обновляющий `UpdatedAtUnixMs` (UTC миллисекунды Unix).
   - При удалении напоминания выставляется `IsDeleted = true` (tombstone), чтобы удаление корректно передалось на второе устройство при синхронизации.
   - Методы `CalculateCalendarDiff`, `AdvanceAfterComplete` и `SnoozeByOffset(years, months, days, hours, minutes, seconds)` реализованы идентично на C# и Java.

2. **`Models/CloudSyncConfig.cs` и `Services/CloudSyncService.cs` (и аналоги на Android)**:
   - Поддерживают два облачных провайдера:
     - `GitHubGist` (`1`): хранит JSON в секретном Gist в файле `strelo_reminders.json` через GitHub REST API (`POST / GET / PATCH https://api.github.com/gists`).
     - `CustomJsonUrl` (`2`): хранит JSON по любому HTTPS URL через `GET` / `PUT`.
   - Алгоритм `MergeReminders`: сопоставляет локальный и облачный списки по `Id`. Для каждого `Id` выбирается запись с большим `UpdatedAtUnixMs`.
   - Метод `ToConnectionCode()` кодирует конфигурацию облака в строку `STRELO1:<Base64(JSON)>`, которую можно скопировать в буфер обмена и вставить на другом устройстве одной кнопкой.

---

## 3. Технические правила и ограничения (ВАЖНО!)

1. **Двойная совместимость C# на Windows**:
   - Код Windows-приложения компилируется как через **.NET 8 SDK**, так и через встроенный в Windows 10/11 компилятор **`.NET Framework 4.x` (`csc.exe`)**.
   - Используйте синтаксис, совместимый с **C# 5.0** (без `$"..."`, `?.`, `nameof`, `out var`).
   - При добавлении нового `.cs` файла обязательно добавляйте его в `build.bat` и в шаг `Build standalone .NET Framework EXE` в `.github/workflows/build.yml`.
   - Все `.cs` файлы должны быть сохранены в кодировке **UTF-8 с BOM (`EF BB BF`)** и окончаниями строк `CRLF`.
   - При добавлении дочерних элементов с `AnchorStyles.Right` внутрь контейнера (`Panel`, `GroupBox`) задавайте финальный `Size` контейнера **до** вызова `Controls.Add(...)`.
2. **Совместимость JSON-протокола Windows ↔ Android**:
   - Любое новое поле в `ReminderItem` или `CloudSyncConfig` должно одновременно добавляться и в C#-классы (`Models/`), и в Java-классы (`android/app/src/main/java/com/strelo/reminder/`).

---

## 4. Обязательный регламент работы агента при внесении изменений

После выполнения любой задачи или модификации кода агент **обязан** выполнить следующие шаги:

1. **Проверить кодировку и список файлов**:
   - Убедиться, что все `.cs` файлы сохранены в UTF-8 с BOM, а новые файлы прописаны в `build.bat` и `.github/workflows/build.yml`.
2. **Задокументировать изменения**:
   - Добавить подробную запись в **`CHANGELOG.md`** (дата, описание задачи, список затронутых файлов, суть изменений в коде, ссылка/статус сборки).
   - Если изменилась структура файлов, логика классов или пользовательские функции — обновить **`AGENTS.md`** и **`README.md`**.
3. **Закоммитить и отправить изменения в GitHub для запуска автосборки**:
   - Перед коммитом синхронизировать указатель ветки с удалённым репозиторием: `git fetch origin && git reset --soft origin/<текущая-ветка>`.
   - Выполнить `git add -A && git commit -m "..."` и отправить изменения в текущую рабочую ветку (`git push origin <текущая-ветка>`).
4. **Проконтролировать выполнение GitHub Actions**:
   - Дождаться запуска workflow `Windows & Android Build & Artifacts` через `gh run list --branch <текущая-ветка>` и проверить успешное завершение обоих джобов (`build-windows` и `build-android`) через `gh run watch <run-id> --exit-status`.
   - Если любая из сборок завершилась с ошибкой — изучить лог через `gh run view <run-id> --log-failed`, исправить ошибку, задокументировать исправление и повторно запустить сборку до получения статуса `completed / success`.
