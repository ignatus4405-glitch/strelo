# Руководство для ИИ-агентов и разработчиков (AGENTS.md)

Этот документ содержит полное описание архитектуры проекта **StreloReminder** (десктопного приложения Windows + мобильного приложения Android с двусторонней облачной синхронизацией и автообновлением через GitHub Releases), технических ограничений и обязательного регламента работы. Любой агент, продолжающий разработку в этом репозитории, **обязан** ознакомиться с этим файлом перед внесением изменений и строго следовать регламенту документирования и автосборки.

---

## 1. Обзор проекта

**StreloReminder** — кроссплатформенная система напоминаний для **Windows 10/11 (C# WinForms)** и **Android 7.0+ (Java Android SDK)** с полной **двусторонней облачной синхронизацией через интернет** и **автоматическим обновлением версий через GitHub Releases**.
- На **Windows** приложение работает в системном трее (`NotifyIcon`) и выводит всплывающее окно поверх всех окон (`TopMost = true`) со звуковым сигналом и кнопками откладывания.
- На **Android** приложение планирует точные будильники через `AlarmManager`, выводит системные уведомления высокой важности (`IMPORTANCE_HIGH`) и полноэкранное всплывающее окно (`ReminderAlertActivity`) поверх блокировки экрана.
- **Обмен данными между ПК и телефоном** происходит автоматически через облако (**GitHub Gist** или любой **Cloud JSON URL**) с разрешением конфликтов по метке времени `UpdatedAtUnixMs` и поддержкой мягкого удаления (`IsDeleted`). Для мгновенной связки компьютера и телефона используется единая строка подключения **`STRELO1:...`**.
- **Автоматическое обновление версий (GitHub Releases)**: при каждой успешной сборке в GitHub Actions автоматически формируется версия `1.3.<run_number>` и создаётся GitHub Release `v1.3.<run_number>` с прикреплёнными файлами `StreloReminder.exe` и `StreloReminder-Android.apk`. Клиенты Windows и Android автоматически опрашивают `https://api.github.com/repos/ignatus4405-glitch/strelo/releases/latest`, уведомляют пользователя о выходе новой версии и позволяют установить обновление в 1 клик.

---

## 2. Архитектура и карта файлов

```text
StreloReminder/
├── .github/
│   └── workflows/
│       └── build.yml               # CI/CD пайплайн GitHub Actions (Windows EXE + Android APK + публикация GitHub Release)
├── Forms/
│   ├── MainForm.cs                 # Главное окно Windows: редактор, таблица, таймер, трей, синхронизация и проверка обновлений
│   ├── PopupNotificationForm.cs    # Всплывающее окно уведомления поверх всех окон (TopMost + Snooze)
│   └── CloudSyncForm.cs            # Диалог настройки облачной синхронизации ПК ↔ Android (Gist / Cloud URL / код STRELO1)
├── Models/
│   ├── ReminderItem.cs             # Модель данных напоминания (DataContract), UpdatedAtUnixMs, IsDeleted, расчёт повторов
│   └── CloudSyncConfig.cs          # Конфигурация облака и генератор кода быстрого подключения STRELO1:...
├── Services/
│   ├── IconGenerator.cs            # Программная отрисовка иконки колокольчика через System.Drawing (GDI+)
│   ├── StorageService.cs           # Сериализация напоминаний и настроек облака в %APPDATA%\StreloReminder\
│   ├── CloudSyncService.cs         # Двусторонняя синхронизация (Облако в 1 клик, GitHub Gist, Cloud URL)
│   ├── LanSyncService.cs           # Локальный Wi-Fi HTTP/JSON сервер (порт 45892) + UDP автообнаружение ПК (порт 45891)
│   ├── QrCodeGenerator.cs          # Генератор стандартных QR-кодов ISO/IEC 18004 (Bitmap) для привязки телефона
│   └── UpdateService.cs            # Автопроверка обновлений через GitHub Releases API и самообновление StreloReminder.exe
├── android/                        # Нативное мобильное приложение для Android (Java, minSdk 24, targetSdk 34)
│   ├── settings.gradle, build.gradle, gradle.properties
│   └── app/
│       ├── build.gradle
│       └── src/main/
│           ├── AndroidManifest.xml
│           ├── res/drawable/ic_bell.xml
│           └── java/com/strelo/reminder/
│               ├── MainActivity.java           # Главный экран Android: список, редактор 6 единиц времени, облако, Wi-Fi, обновления
│               ├── ReminderAlertActivity.java  # Всплывающее окно уведомления на Android (звук, вибрация, Snooze)
│               ├── AlarmScheduler.java         # Планировщик точных будильников AlarmManager + NotificationChannel
│               ├── ReminderAlarmReceiver.java  # BroadcastReceiver срабатывания будильников и BOOT_COMPLETED
│               ├── ReminderItem.java           # Модель напоминания (100% совместимая по JSON с Windows ReminderItem.cs)
│               ├── CloudSyncConfig.java        # Настройки Облака 1-клик / Gist / URL + Wi-Fi LAN и парсер strelo://sync
│               ├── CloudSyncService.java       # Гибридная синхронизация Android ↔ Wi-Fi ПК + Облако
│               ├── LanSyncService.java         # Автопоиск ПК в сети Wi-Fi (UDP 45891 + TCP 45892) и прямой обмен
│               ├── QrCodeGenerator.java        # Генератор стандартных QR-кодов ISO/IEC 18004 на Android
│               ├── UpdateService.java          # Проверка выхода новой версии APK через GitHub Releases API
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

3. **`Services/UpdateService.cs` (Windows) и `UpdateService.java` (Android)**:
   - Опрашивают `https://api.github.com/repos/ignatus4405-glitch/strelo/releases/latest` и сравнивают `tag_name` релиза с текущей версией (`CurrentVersion` / `CURRENT_VERSION`).
   - На Windows `UpdateService.DownloadAndInstallUpdate` скачивает `StreloReminder.exe` из ассетов релиза во временный файл `.update.exe` и запускает фоновый `.bat`-скрипт, который заменяет основной `.exe` после закрытия процесса и перезапускает обновлённую версию.
   - На Android `MainActivity` отправляет системное уведомление и открывает прямую ссылку на скачивание `StreloReminder-Android.apk` из релиза.

---

## 3. Технические правила и ограничения (ВАЖНО!)

1. **Двойная совместимость C# на Windows**:
   - Код Windows-приложения компилируется как через **.NET 8 SDK**, так и через встроенный в Windows 10/11 компилятор **`.NET Framework 4.x` (`csc.exe`)**.
   - Используйте синтаксис, совместимый с **C# 5.0** (без `$"..."`, `?.`, `nameof`, `out var`).
   - При добавлении нового `.cs` файла обязательно добавляйте его в `build.bat` и в шаг `Build standalone .NET Framework EXE` в `.github/workflows/build.yml`.
   - Все `.cs` файлы должны быть сохранены в кодировке **UTF-8 с BOM (`EF BB BF`)** и окончаниями строк `CRLF`.
2. **Правила вёрстки кнопок на Windows и Android (без обрезания и наложения)**:
   - На **Windows** (`Forms/MainForm.cs`): в горизонтальных панелях (`headerPanel`, `listToolbar`) используйте `AnchorStyles.Top | AnchorStyles.Left` с непересекающимися X-координатами и запасом ширины для русских надписей (например, `btnClearDone` >= `190px`), чтобы кнопки не смещались друг на друга и не переносили текст на вторую строку.
   - На **Android** (`MainActivity.java`, `ReminderAlertActivity.java`): для всех кнопок (`Button`) задавайте высоту `ViewGroup.LayoutParams.WRAP_CONTENT` в сочетании с `setMinHeight(dp(...))`, `setSingleLine(true)` и `setIncludeFontPadding(false)`, чтобы текст никогда не обрезался по вертикали.
3. **Совместимость JSON-протокола Windows ↔ Android**:
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
   - Дождаться запуска workflow `Windows & Android Build & Artifacts` через `gh run list --branch <текущая-ветка>` и проверить успешное завершение всех джобов (`build-windows`, `build-android`, `publish-release`) через `gh run watch <run-id> --exit-status`.
   - Если любая из сборок завершилась с ошибкой — изучить лог через `gh run view <run-id> --log-failed`, исправить ошибку, задокументировать исправление и повторно запустить сборку до получения статуса `completed / success`.
