# Журнал изменений (CHANGELOG.md)

В этом файле фиксируется каждое изменение, внесённое в проект **StreloReminder**, с указанием затронутых файлов, архитектурных решений и результатов автосборки в GitHub Actions. Любой агент, продолжающий разработку, обязан добавлять сюда новую секцию при каждом изменении.

---

## [2026-10-15] — Исправление размера окна синхронизации, выбор времени синхронизации, постоянный ключ подписи Android APK и всплывающее окно поверх всех приложений на Android

### Что сделано
1. **Исправлены размеры окна синхронизации и убрано обрезание текста (`Forms/CloudSyncForm.cs`, `Models/CloudSyncConfig.cs`, `CloudSyncConfig.java`)**:
   - Размер окна `Forms/CloudSyncForm.cs` увеличен с `690x610` до **`780x650`**, а ширина внутренних контейнеров увеличена с `654px` до **`744px`**.
   - Надпись с локальным IP-адресом в блоке Wi-Fi (`_lblLanIpInfo`) переведена с `AutoSize = true` на фиксированный двухстрочный блок `Size(714, 40)` (`AutoSize = false`), благодаря чему текст `«Локальный Wi-Fi сервер ПК активен. IP этого компьютера: ...»` больше никогда не уходит за правую границу рамки.
   - Увеличен размер QR-кода (`172x172` на ПК, `170dp` на Android), а формат deep-link для QR-кода оптимизирован в компактный вид `strelo://sync?v=1&p=...&r=...&w=...&ip=...&i=...` без двойного Base64-кодирования — модули QR-кода стали крупными и мгновенно считываются любой камерой.
2. **Добавлен выбор времени (интервала) синхронизации между устройствами (`Forms/CloudSyncForm.cs`, `Forms/MainForm.cs`, `MainActivity.java`)**:
   - На **Windows (`Forms/CloudSyncForm.cs`)** и **Android (`MainActivity.java`)** добавлен удобный выбор периода синхронизации:
     - Выпадающий список готовых пресетов: **«Каждые 5 секунд (мгновенно)»**, **«Каждые 10 секунд»**, **«Каждые 15 секунд (рекомендуется)»**, **«Каждые 30 секунд»**, **«Каждую 1 минуту»**, **«Каждые 5 минут»**, **«Каждые 15 минут»**, **«Каждый 1 час»**, **«Свой интервал...»**.
     - Поле точной настройки своего интервала (число + выбор единицы: **`сек.`**, **`мин.`**, **`ч.`**) с поддержкой интервалов от **3 секунд** до **24 часов**.
   - Выбранный интервал синхронизации автоматически передаётся на второе устройство в составе QR-кода и кода подключения `STRELO1:...` и отображается в строке состояния обоих приложений.
3. **Постоянный ключ подписи Android APK для обновления поверх (`android/app/strelo-release.p12`, `.gitattributes`, `android/app/build.gradle`)**:
   - Сгенерирован и зафиксирован в репозитории постоянный криптографический ключ `android/app/strelo-release.p12` (PKCS12, RSA-2048, срок действия 10 000 дней, alias `strelo`).
   - В `android/app/build.gradle` настроен блок `signingConfigs.release`, который применяется и к `release`, и к `debug` сборкам. Теперь любой новый `StreloReminder-Android.apk` из GitHub Releases устанавливается поверх уже установленного приложения с полным сохранением всех напоминаний и настроек (без ошибки `INSTALL_FAILED_UPDATE_INCOMPATIBLE`).
   - В `AndroidManifest.xml` и `ReminderAlarmReceiver.java` добавлена обработка системного события `ACTION_MY_PACKAGE_REPLACED` для автоматического перепланирования будильников сразу после установки обновления поверх.
4. **Реальное всплывающее окно поверх всех остальных приложений на Android (`OverlayAlertManager.java`, `AlarmScheduler.java`, `ReminderAlarmReceiver.java`, `MainActivity.java`, `AndroidManifest.xml`)**:
   - Создан класс `OverlayAlertManager.java`, использующий системный сервис `WindowManager` с типом окна `WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY` (`SYSTEM_ALERT_WINDOW`) и флагами `FLAG_KEEP_SCREEN_ON | FLAG_SHOW_WHEN_LOCKED | FLAG_TURN_SCREEN_ON`.
   - При срабатывании напоминания окно с текстом задачи, звуковым сигналом и кнопками **«+30 сек»**, **«+5 мин»**, **«+15 мин»**, **«+1 час»**, **«✓ Понятно (Выполнено)»** и **«Ещё...»** всплывает прямо поверх любого открытого приложения на телефоне (браузер, мессенджер, видео, игры).
   - В `MainActivity.java` добавлен заметный баннер в 1 клик **«⚠️ Включить всплытие ПОВЕРХ ДРУГИХ ОКОН (нажмите здесь)»**, который открывает системный экран разрешения `Settings.ACTION_MANAGE_OVERLAY_PERMISSION` для пакета `com.strelo.reminder` и автоматически скрывается после включения.
   - Обновлён канал уведомлений `strelo_alerts_popup_v2` в `AlarmScheduler.java` (`IMPORTANCE_HIGH`, `CATEGORY_ALARM`, `USAGE_ALARM`, `WakeLock` для включения экрана и быстрые кнопки `✓ Выполнено` / `+10 мин` прямо в шторке).

### Затронутые файлы
- `android/app/strelo-release.p12` (создан — постоянный ключ подписи APK)
- `.gitattributes` (обновлён — `*.p12 binary`)
- `android/app/build.gradle` (обновлён — `signingConfigs.release`)
- `android/app/src/main/AndroidManifest.xml` (обновлён — `SYSTEM_ALERT_WINDOW`, `WAKE_LOCK`, `VIBRATE`, `MY_PACKAGE_REPLACED`)
- `android/app/src/main/java/com/strelo/reminder/OverlayAlertManager.java` (создан)
- `android/app/src/main/java/com/strelo/reminder/AlarmScheduler.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/ReminderAlarmReceiver.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/ReminderAlertActivity.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/MainActivity.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/CloudSyncConfig.java` (обновлён)
- `Models/CloudSyncConfig.cs` (обновлён)
- `Forms/CloudSyncForm.cs` (обновлён)
- `Forms/MainForm.cs` (обновлён)
- `AGENTS.md`, `README.md`, `CHANGELOG.md` (обновлены)

---

## [2026-10-15] — Реализация «Облака в 1 клик + QR-код» (без токенов) и «Локальной синхронизации по Wi-Fi с автопоиском ПК»

### Что сделано
1. **«Облако в 1 клик» без регистрации и токенов (`Models/CloudSyncConfig.cs`, `Services/CloudSyncService.cs`, `CloudSyncConfig.java`, `CloudSyncService.java`)**:
   - Добавлен новый провайдер облачной синхронизации `CloudProviderType.OneClickCloud = 3` (`PROVIDER_ONE_CLICK_CLOUD = 3`).
   - Реализован метод `CreateOneClickCloudRoom` / `createOneClickCloudRoom`, который по нажатию одной кнопки **«☁ Создать новое Облако в 1 клик»** автоматически выделяет приватную облачную JSON-комнату (`https://jsonblob.com/api/jsonBlob/<roomId>`) без ввода каких-либо токенов, паролей или регистрации.
   - На втором устройстве достаточно отсканировать QR-код, вставить код подключения или ввести короткий ID комнаты.
2. **Генерация стандартизированного QR-кода и автопривязка камерой (`Services/QrCodeGenerator.cs`, `QrCodeGenerator.java`, `AndroidManifest.xml`)**:
   - Написан полностью автономный генератор QR-кодов стандарта **ISO/IEC 18004** (Byte Mode, коррекция ошибок Reed-Solomon в поле Галуа GF(256), версии 1..20) без сторонних библиотек для Windows (`System.Drawing.Bitmap`) и Android (`android.graphics.Bitmap`).
   - QR-код кодирует deep-link `strelo://sync?code=STRELO1:...`, содержащий одновременно параметры Облака в 1 клик и локальный Wi-Fi IP-адрес компьютера.
   - В `AndroidManifest.xml` зарегистрирован `<intent-filter>` для схемы `strelo://sync`, а в `MainActivity.java` добавлен обработчик `handleIncomingDeepLink`: при наведении стандартной камеры телефона на экран ПК автоматически открывается приложение `StreloReminder`, сохраняет настройки и запускает синхронизацию.
3. **Локальная синхронизация по домашнему Wi-Fi с автопоиском ПК (`Services/LanSyncService.cs`, `LanSyncService.java`, `Forms/MainForm.cs`, `MainActivity.java`)**:
   - На **Windows (`Services/LanSyncService.cs`)**:
     - Запускается фоновый **UDP-ответчик автообнаружения** на порту `45891` (`STRELO_DISCOVER_V1` -> `STRELO_PC_V1|<ИмяПК>|45892|<IP>`) и встроенный **HTTP/JSON сервер на базе `TcpListener`** (порт `45892`, не требует прав администратора Windows).
     - При обращении телефона к `POST /strelo/sync` ПК мгновенно выполняет двустороннее слияние (`MergeReminders`), обновляет таблицу на экране ПК и возвращает объединённый список напоминаний телефону за ~15–20 мс.
   - На **Android (`LanSyncService.java`, `MainActivity.java`)**:
     - Добавлена кнопка **«📡 Найти компьютер в сети Wi-Fi автоматически»**: отправляет широковещательный UDP-запрос на порт `45891`, а если роутер блокирует UDP Broadcast — выполняет быстрое многопоточное TCP-сканирование локальной подсети `/24` по порту `45892`.
     - Реализован **гибридный режим синхронизации**: когда телефон находится дома в одной сети Wi-Fi с ПК, синхронизация идёт напрямую по локальной сети без задержек, а вне дома — автоматически переключается на Облако в 1 клик.

### Затронутые файлы
- `Models/CloudSyncConfig.cs` (обновлён)
- `Services/CloudSyncService.cs` (обновлён)
- `Services/QrCodeGenerator.cs` (создан)
- `Services/LanSyncService.cs` (создан)
- `Forms/CloudSyncForm.cs` (обновлён)
- `Forms/MainForm.cs` (обновлён)
- `build.bat`, `.github/workflows/build.yml` (обновлены)
- `android/app/src/main/AndroidManifest.xml` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/CloudSyncConfig.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/CloudSyncService.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/QrCodeGenerator.java` (создан)
- `android/app/src/main/java/com/strelo/reminder/LanSyncService.java` (создан)
- `android/app/src/main/java/com/strelo/reminder/MainActivity.java` (обновлён)
- `AGENTS.md`, `README.md`, `CHANGELOG.md` (обновлены)

---

## [2026-10-15] — Исправление размеров кнопок и перекрытий (Windows + Android) и система автоматического обновления версий через GitHub Releases

### Что сделано
1. **Исправлены размеры кнопок и перекрытие элементов в Windows-приложении (`Forms/MainForm.cs`)**:
   - **Шапка окна (`headerPanel`)**: все элементы переведены на фиксированную горизонтальную привязку `AnchorStyles.Top | AnchorStyles.Left` с точными непересекающимися координатами (`_btnSyncNow` X=268, `_btnCloudSettings` X=350, `_btnCheckUpdate` X=490, `_chkDoNotDisturb` X=626, `_chkAutoStart` X=845), что полностью устранило наложение кнопки `«Облако (Android)»` на чекбокс `«Режим „Не беспокоить“»` при любом DPI и масштабировании экрана.
   - **Панель кнопок над списком (`listToolbar`)**: ширина кнопки **«Очистить выполненные»** увеличена с `152px` до **`194px`** (`Font = 8.5pt Bold`, `AnchorStyles.Top | AnchorStyles.Left`), благодаря чему оба слова гарантированно помещаются в одну строку и второе слово никогда не переносится и не обрезается по вертикали.
2. **Исправлены размеры кнопок и вертикальное обрезание текста в Android-приложении (`MainActivity.java`, `ReminderAlertActivity.java`)**:
   - Панель основных действий в `MainActivity.java` перестроена в **два ряда с динамической высотой `WRAP_CONTENT`** (`setIncludeFontPadding(false)`, `setSingleLine(true)`):
     - Ряд 1: полноширинная кнопка **`+ Новое напоминание`** (`MATCH_PARENT`, `minHeight = 46dp`).
     - Ряд 2: две равные кнопки по 50% ширины экрана — **`Тест уведомления`** и **`Очистить выполненные`** (`minHeight = 42dp`).
   - Все кнопки в шапке (`Синхр.`, `Облако (ПК)`, `Обновл.`), карточках напоминаний (`Изменить`, `Пауза`, `+10 мин`, `Удалить`), диалогах и на экране тревоги (`ReminderAlertActivity.java`) переведены с жёсткой высоты `dp(...)` на `ViewGroup.LayoutParams.WRAP_CONTENT` с `minHeight`, чтобы текст никогда не обрезался снизу.
3. **Автоматическое обновление версий через GitHub Releases с уведомлением клиента (`Services/UpdateService.cs`, `android/.../UpdateService.java`, `.github/workflows/build.yml`)**:
   - **Автоматическая публикация релизов в CI (`.github/workflows/build.yml`)**:
     - При каждой сборке в GitHub Actions номер версии автоматически формируется как `1.3.<run_number>` и прописывается в `Services/UpdateService.cs`, `UpdateService.java` и `android/app/build.gradle`.
     - Добавлен джоб `publish-release` (`needs: [build-windows, build-android]`), который автоматически создаёт GitHub Release `v1.3.<run_number>` и прикрепляет к нему бинарные файлы **`StreloReminder.exe`** и **`StreloReminder-Android.apk`**.
   - **Клиент автообновления для Windows (`Services/UpdateService.cs`, `Forms/MainForm.cs`)**:
     - Автоматически проверяет последний релиз через `https://api.github.com/repos/ignatus4405-glitch/strelo/releases/latest` через 4 секунды после запуска и далее каждые 30 минут, а также вручную по кнопке **«Обновление»** в шапке и из меню системного трея.
     - При выходе новой версии показывает всплывающее уведомление в трее (`ShowBalloonTip`), подсвечивает кнопку зелёным цветом (`«Обновить v1.3.X»`) и предлагает в 1 клик автоматически скачать новый `StreloReminder.exe`, заменить текущий файл через вспомогательный скрипт и перезапустить обновлённую программу.
   - **Клиент автообновления для Android (`UpdateService.java`, `MainActivity.java`)**:
     - Автоматически проверяет последний релиз на GitHub через 3 секунды после запуска и каждые 30 минут, а также вручную по кнопке **«Обновл.»** в шапке.
     - При обнаружении новой версии отправляет системное уведомление Android (`NotificationManager`), меняет кнопку в шапке на зелёную `«v1.3.X ⬇»` и показывает диалог с кнопкой **«Скачать APK»**, открывающей прямую ссылку на `StreloReminder-Android.apk` из последнего релиза GitHub.

### Затронутые файлы
- `Services/UpdateService.cs` (создан)
- `Forms/MainForm.cs` (обновлён)
- `build.bat` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/UpdateService.java` (создан)
- `android/app/src/main/java/com/strelo/reminder/MainActivity.java` (обновлён)
- `android/app/src/main/java/com/strelo/reminder/ReminderAlertActivity.java` (обновлён)
- `android/app/build.gradle` (обновлён)
- `.github/workflows/build.yml` (обновлён)
- `AGENTS.md`, `README.md`, `CHANGELOG.md` (обновлены)

---

## [2026-10-14] — Разработка мобильного приложения Android и двусторонней облачной синхронизации (ПК Windows ↔ Android)

### Что сделано
1. **Модель двусторонней синхронизации без потери данных (`Models/ReminderItem.cs` и `android/.../ReminderItem.java`)**:
   - В модель напоминания добавлены поля `UpdatedAtUnixMs` (`Order = 11`, время последнего изменения в миллисекундах UTC) и `IsDeleted` (`Order = 12`, флаг мягкого удаления / tombstone).
   - Формат JSON-объекта `ReminderItem` на 100% идентичен между Windows (`.NET DataContractJsonSerializer`) и Android (`org.json.JSONObject`), что позволяет напрямую обмениваться списком напоминаний через облако.
   - При слиянии списков (`MergeReminders`) для каждого `Id` побеждает запись с более новым `UpdatedAtUnixMs`, а новые записи с любого устройства автоматически добавляются на второе устройство.
2. **Облачная синхронизация на Windows (`Models/CloudSyncConfig.cs`, `Services/CloudSyncService.cs`, `Forms/CloudSyncForm.cs`, `Forms/MainForm.cs`)**:
   - Поддержаны два облачных режима:
     1. **GitHub Gist** (`https://api.github.com/gists`) — бесплатное секретное облачное хранилище. В окно настроек добавлена кнопка **«Создать новое облако Gist»**, которая в один клик создаёт Gist и сохраняет его ID.
     2. **Прямой облачный JSON URL** (`GET` / `PUT`, например Firebase Realtime Database или собственный REST-эндпоинт).
   - Реализован **Код быстрого подключения (`STRELO1:...`)**: пользователь может нажать «Скопировать код для телефона» на ПК и вставить его одной кнопкой в Android-приложении (или наоборот).
   - В шапку главного окна Windows добавлены кнопки **«Синхр.»** и **«Облако (Android)»**, индикатор статуса синхронизации, а также фоновая автосинхронизация в пуле потоков (`ThreadPool`) каждые `N` секунд и мгновенная отправка изменений при создании, редактировании, откладывании, выполнении или удалении напоминания.
   - Если напоминание выполнено или отложено на телефоне, открытое всплывающее окно на ПК автоматически закрывается при следующей синхронизации.
3. **Нативное мобильное приложение для Android (`android/`)**:
   - Создан полноценный Android-проект (`com.strelo.reminder`, `minSdk 24`, `targetSdk 34`) со следующими компонентами:
     - **`MainActivity.java`** — главный экран с живыми часами, статусом облака, кнопками `Синхр.`, `Облако (ПК)`, переключателем `Пауза`, списком карточек напоминаний с посекундным обратным отсчётом и диалогом создания/редактирования напоминаний (включая выбор **Годы, Месяцы, Дни, Часы, Минуты, Секунды** + пресеты + точная дата и время).
     - **`ReminderAlertActivity.java`** — всплывающее полноэкранное окно уведомления поверх блокировки экрана со звуком, вибрацией, быстрыми кнопками откладывания (`+30 сек`, `+5 мин`, `+15 мин`, `+1 час`, `+1 день`), произвольным выбором (`сек.`, `мин.`, `ч.`, `дн.`, `мес.`, `лет`) и кнопкой «Понятно (Выполнено)».
     - **`AlarmScheduler.java` и `ReminderAlarmReceiver.java`** — планировщик точных будильников `AlarmManager` (`setExactAndAllowWhileIdle`), канал уведомлений высокой важности (`IMPORTANCE_HIGH`) и восстановление расписания после перезагрузки телефона (`BOOT_COMPLETED`).
     - **`CloudSyncConfig.java`, `CloudSyncService.java`, `StorageService.java`** — локальное хранение и двусторонняя облачная синхронизация с ПК.
4. **Автосборка Android APK и Windows EXE в GitHub Actions (`.github/workflows/build.yml`)**:
   - В пайплайн добавлен параллельный джоб `build-android`, который собирает подписанный установочный файл **`StreloReminder-Android.apk`** и публикует его в артефакт **`StreloReminder-Android-APK`** вместе с Windows-сборками.

### Затронутые файлы
- `Models/ReminderItem.cs` (обновлён)
- `Models/CloudSyncConfig.cs` (создан)
- `Services/StorageService.cs` (обновлён)
- `Services/CloudSyncService.cs` (создан)
- `Forms/CloudSyncForm.cs` (создан)
- `Forms/MainForm.cs` (обновлён)
- `build.bat` (обновлён)
- `android/settings.gradle`, `android/build.gradle`, `android/gradle.properties`, `android/app/build.gradle` (созданы)
- `android/app/src/main/AndroidManifest.xml`, `android/app/src/main/res/drawable/ic_bell.xml` (созданы)
- `android/app/src/main/java/com/strelo/reminder/*.java` (8 классов созданы)
- `.github/workflows/build.yml` (обновлён)
- `.gitignore`, `AGENTS.md`, `README.md`, `CHANGELOG.md` (обновлены)

---

## [2026-10-14] — Увеличение размера главного окна для полного отображения заголовка списка напоминаний и шапки

### Что сделано
1. **Полная видимость текста `«Список напоминаний (Всего: X, Активных: Y)»` (`Forms/MainForm.cs`)**:
   - Для метки `_lblSummary` включён `AutoSize = true` (вместо обрезания через `AutoEllipsis`), чтобы счётчик общего и активного числа напоминаний никогда не урезался многоточием (`...`).
   - Клиентский размер главного окна увеличен до `ClientSize = new Size(1060, 700)` (`MinimumSize = new Size(1076, 738)`), а ширина панелей `_grpEditor`, `listToolbar` и таблицы `_lvReminders` увеличена до `1028px`.
   - Кнопки панели управления списком сдвинуты в диапазон `X = 408..1028`, оставляя более `400px` свободного пространства слева под полный текст заголовка `«Список напоминаний (Всего: X, Активных: Y)»`.
2. **Восстановлена видимость переключателей в верхней шапке (`Forms/MainForm.cs`)**:
   - Для `headerPanel` явно задан `Size = new Size(1060, 60)` **до** добавления дочерних элементов с `AnchorStyles.Right` (`_chkDoNotDisturb` и `_chkAutoStart`), чтобы при стыковке (`Dock = DockStyle.Top`) WinForms не смещал чекбоксы за правую границу окна.

### Затронутые файлы
- `Forms/MainForm.cs` (обновлён)
- `AGENTS.md` (обновлён)
- `CHANGELOG.md` (обновлён)

---

## [2026-10-14] — Исправление видимости и вёрстки кнопок в главном и всплывающем окнах

### Что сделано
1. **Исправлено перекрытие заголовка списка и кнопок панели управления (`Forms/MainForm.cs`)**:
   - Метка `_lblSummary` (`«Список напоминаний (Всего: X, Активных: Y)»`) переведена на фиксированную ширину (`320px`, `AutoSize = false`, `AutoEllipsis = true`), чтобы длинный текст никогда не наезжал на кнопку **«Изменить»**.
   - Все 6 кнопок панели над таблицей (`Изменить`, `Вкл / Выкл`, `+10 мин`, `Удалить`, `Очистить выполненные`, `Папка`) сдвинуты правее (`X >= 328`), получили привязку `AnchorStyles.Top | AnchorStyles.Right`, увеличенную ширину и `Padding = new Padding(0)`, а также избавлены от широких Unicode-эмодзи, которые вызывали перенос слов на вторую строку (например, в кнопке `Очистить выполненные`).
2. **Исправлено обрезание текста в быстрых кнопках таймера (`Forms/MainForm.cs`)**:
   - Ширина главного окна увеличена до `1000x720` (`MinimumSize = 960x660`), а ширина панели `_pnlTimerControls` увеличена с `438px` до `478px`.
   - Ширина каждой из 8 быстрых кнопок (`30 сек`, `5 мин`, `15 мин`, `1 час`, `1 день`, `1 мес`, `1 год`, `Сброс`) увеличена с `51px` до `55px` с нулевым внутренним отступом (`Padding = 0`), чтобы текст `15 мин` полностью помещался в одну строку.
3. **Улучшена геометрия кнопок во всплывающем окне (`Forms/PopupNotificationForm.cs`)**:
   - Ширина клиентской области всплывающего окна увеличена до `540px`, а ширина быстрых кнопок откладывания (`+30 сек`, `+5 мин`, `+15 мин`, `+1 час`, `+1 день`) увеличена до `64px` (`Padding = 0`) для исключения переноса текста при любом масштабировании экрана Windows.

### Затронутые файлы
- `Forms/MainForm.cs` (обновлён)
- `Forms/PopupNotificationForm.cs` (обновлён)
- `CHANGELOG.md` (обновлён)

---

## [2026-10-14] — Добавлен выбор смещения через Годы, Месяцы, Дни, Часы, Минуты и Секунды

### Что сделано
1. **Панель выбора «Через сколько» в главном окне (`Forms/MainForm.cs`)**:
   - Расширена панель `_pnlTimerControls`: вместо двух полей (часы и минуты) добавлены **6 полей `NumericUpDown`** для точного указания смещения:
     - **Годы** (`_numTimerYears`: `0..100`)
     - **Месяцы** (`_numTimerMonths`: `0..120`)
     - **Дни** (`_numTimerDays`: `0..3650`)
     - **Часы** (`_numTimerHours`: `0..999`)
     - **Минуты** (`_numTimerMinutes`: `0..999`, по умолчанию `15`)
     - **Секунды** (`_numTimerSeconds`: `0..999`)
   - Добавлены быстрые кнопки-пресеты для всех диапазонов: `30 сек`, `5 мин`, `15 мин`, `1 час`, `1 день`, `1 мес`, `1 год` и кнопка `Сброс`.
   - Добавлен живой индикатор `_lblTimerPreview` (`Сработает: dd.MM.yyyy HH:mm:ss`), который динамически пересчитывает точную дату и время срабатывания при изменении любого из 6 полей и каждую секунду по таймеру.
   - При переходе к редактированию существующего напоминания (`OnEditSelectedClick`) оставшееся время автоматически раскладывается на годы, месяцы, дни, часы, минуты и секунды.
2. **Календарный расчёт оставшегося времени и откладывания (`Models/ReminderItem.cs`)**:
   - Добавлен статический метод `CalculateCalendarDiff(DateTime from, DateTime to, out int years, out int months, out int days, out int hours, out int minutes, out int seconds)` для точного вычисления календарной разницы с учётом високосных лет и разной длины месяцев.
   - Обновлён метод `GetRemainingTimeText(DateTime now)` для отображения оставшихся лет, месяцев, дней, часов, минут и секунд в таблице.
   - Добавлен метод `SnoozeByOffset(int years, int months, int days, int hours, int minutes, int seconds)` для откладывания напоминания на любую комбинацию единиц времени.
3. **Расширен блок «Отложить напоминание» во всплывающем окне (`Forms/PopupNotificationForm.cs`)**:
   - Быстрые кнопки откладывания теперь включают: `+30 сек`, `+5 мин`, `+15 мин`, `+1 час`, `+1 день`.
   - В произвольном откладывании добавлен выпадающий список `_cmbCustomUnit` с выбором единицы измерения: **`сек.`**, **`мин.`**, **`ч.`**, **`дн.`**, **`мес.`**, **`лет`**.

### Затронутые файлы
- `Models/ReminderItem.cs` (обновлён)
- `Forms/MainForm.cs` (обновлён)
- `Forms/PopupNotificationForm.cs` (обновлён)
- `AGENTS.md` (обновлён)
- `README.md` (обновлён)
- `CHANGELOG.md` (обновлён)

---

## [2026-10-14] — Настройка CI/CD автосборки в GitHub Actions и документации для агентов

### Что сделано
1. **Добавлен пайплайн автосборки GitHub Actions (`.github/workflows/build.yml`)**:
   - Автоматический запуск при любом `push`, `pull_request` и вручную через `workflow_dispatch`.
   - Среда выполнения: `windows-latest`.
   - **Сборка №1 (Автономный `.NET Framework 4.8` EXE)**: компиляция через встроенный в Windows `csc.exe` в один независимый исполняемый файл `StreloReminder.exe` (не требует установки .NET Runtime на Windows 10/11). Публикуется как артефакт `StreloReminder-Standalone-Win10-11`.
   - **Сборка №2 (`.NET 8` WinForms `win-x64`)**: сборка и публикация через `dotnet build` и `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true`. Публикуется как артефакт `StreloReminder-Net8-WinX64`.
2. **Создано руководство для агентов (`AGENTS.md`)**:
   - Описаны архитектура проекта, назначение каждого класса и метода, ограничения совместимости C# 5 / .NET 8 и обязательный регламент работы для всех последующих агентов.
3. **Создан журнал изменений (`CHANGELOG.md`)** и обновлена пользовательская документация (**`README.md`**).

### Затронутые файлы
- `.github/workflows/build.yml` (создан)
- `AGENTS.md` (создан)
- `CHANGELOG.md` (создан)
- `README.md` (обновлён раздел об автосборке в GitHub Actions и документации для агентов)

---

## [2026-10-14] — Разработка базовой версии приложения StreloReminder (v1.0.0)

### Что сделано
1. **Модель данных (`Models/ReminderItem.cs`)**:
   - Реализован класс `ReminderItem` с поддержкой сериализации через `DataContractJsonSerializer`.
   - Реализованы режимы повтора (`RecurrenceType`: однократно, каждые N минут, ежедневно, по будням, еженедельно) и приоритеты (`ReminderPriority`: обычное, важное).
   - Добавлены методы вычисления оставшегося времени (`GetRemainingTimeText`), перехода к следующему повтору (`AdvanceAfterComplete`) и откладывания (`Snooze`).
2. **Сервис хранения и автозапуска (`Services/StorageService.cs`)**:
   - Реализовано сохранение и чтение списка напоминаний в `%APPDATA%\StreloReminder\reminders.json`.
   - Реализовано управление автозапуском в реестре Windows (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) с ключом `--minimized`.
3. **Программный генератор иконок (`Services/IconGenerator.cs`)**:
   - Реализована векторная отрисовка иконки колокольчика (`32x32`) для главного окна, всплывающего окна и системного трея в двух состояниях (активный режим и режим «Не беспокоить»).
4. **Всплывающее окно поверх всех окон (`Forms/PopupNotificationForm.cs`)**:
   - Реализовано окно уведомления с `TopMost = true`, Win32 `SetWindowPos` (`HWND_TOPMOST`) и миганием в панели задач (`FlashWindow`).
   - Добавлено звуковое оповещение (`SystemSounds.Asterisk` / `SystemSounds.Exclamation`) с повтором каждые 15 секунд.
   - Добавлены быстрые кнопки откладывания (`+5 мин`, `+10 мин`, `+15 мин`, `+30 мин`, `+1 час`), ввод произвольного интервала в минутах и кнопка завершения «Понятно (Выполнено)».
5. **Главное окно и планировщик (`Forms/MainForm.cs`)**:
   - Реализован интерфейс добавления и редактирования напоминаний (режим таймера «Через N ч. M мин.» с пресетами и режим точной даты/времени).
   - Реализована таблица `ListView` с живым посекундным обновлением обратного отсчёта и цветовой индикацией статусов.
   - Реализовано сворачивание в системный трей (`NotifyIcon`) с контекстным меню быстрого создания напоминаний и режимом «Не беспокоить».
6. **Точка входа и сборка (`Program.cs`, `StreloReminder.csproj`, `StreloReminder.sln`, `build.bat`)**:
   - Добавлена защита от повторного запуска приложения через `Mutex`.
   - Созданы файлы проекта для `.NET 8` / Visual Studio и локальный скрипт `build.bat`.

### Затронутые файлы
- `Program.cs` (создан)
- `Models/ReminderItem.cs` (создан)
- `Services/StorageService.cs` (создан)
- `Services/IconGenerator.cs` (создан)
- `Forms/PopupNotificationForm.cs` (создан)
- `Forms/MainForm.cs` (создан)
- `StreloReminder.csproj` (создан)
- `StreloReminder.sln` (создан)
- `build.bat` (создан)
- `.gitignore`, `.gitattributes` (созданы)
