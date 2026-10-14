# План переноса функционала RPAWriter → RPAWriter2

## 1. Исходное состояние и цель

- **RPAWriter.Core** (`netstandard2.0`) — библиотека со всей доменной логикой. Переиспользуется **без изменений**:
  - AI-провайдер: `OpenAiCompatibleProvider`, `StubAiProvider`;
  - чтение ТЗ: `OpenXmlDocumentReader`;
  - база знаний: `FileKnowledgeBase` + `SimilarityIndex`;
  - формирование БЗ: `KnowledgeBaseFormationService`;
  - генерация робота: `RpaPipelineService`, `RobotGenerator`;
  - конвертация UiPath: `UiPathToWfConverter`;
  - логи: `LogAnalyzer`, `LocalLogAccessService`, `RemoteLogService`.
- **RPAWriter** (старый) — VSIX `net472`, окно на WinForms (`RpaWriterControl`), 3 команды меню, Options-страница.
- **RPAWriter2** (новый) — VSIX `net472`, окно на WPF (`ToolWindow1` + `ToolWindow1Control.xaml`), доменной логики нет, ссылки на `RPAWriter.Core` нет.

**Цель:** перенести весь функционал RPAWriter в единое окно `RPAWriter2/ToolWindow1.cs` (плюс WPF-контрол `ToolWindow1Control`), переиспользуя `RPAWriter.Core`. Фичи добавляются по одной, между ними — ручная проверка разработчиком.

## 2. Ключевые решения и ограничения

1. **Тип проекта не меняем.** Не переводим расширение на новый out-of-process SDK (`Microsoft.VisualStudio.Extensibility`); остаёмся на классической in-proc модели VSPackage (`AsyncPackage` + `ToolWindowPane`). `RPAWriter2.csproj` уже в SDK-стиле (`Microsoft.NET.Sdk`) — оставляем как есть, только добавляем ссылки/элементы.
2. **UI переписывается с WinForms на WPF** (XAML), т.к. `ToolWindow1Control` — WPF. Доменные сервисы из `RPAWriter.Core` используются как готовый «чёрный ящик».
3. **Весь функционал — кнопки внутри окна.** В меню остаётся только один пункт для открытия окна; плейсхолдер `Command1` удаляется.
4. **Настройки** редактируются прямо в окне и сохраняются в JSON (`%AppData%\RPAWriter2\settings.json`). Options-страница (Tools → Options) может быть добавлена позже опционально.

## 3. Роли и воркфлоу

- **Агент** — только реализация: пишет код, добавляет файлы, правит `.csproj`/`.vsct`/XAML. Из команд запускает **только unit-тесты и только при необходимости** (для проверки логики из `RPAWriter.Core` / тестового проекта). Интерфейс и сборку VSIX **не запускает**.
- **Разработчик** — вручную собирает и запускает интерфейс (F5 → Experimental Instance → Tools → ToolWindow1) и проверяет результат **глазами**. Пункт «Проверка: …» в каждом этапе — это **ответственность разработчика**, а не агента.
- **Цикл:** внедрение агентом → проверка разработчиком → (если есть замечания) правки агентом → повторная проверка.

## 4. Этапы (каждый — одна фича + ручная проверка разработчиком)

### Этап 0 — Базовый каркас (исходная точка)
- Убедиться, что RPAWriter2 собирается и окно открывается: F5 → Experimental Instance → **Tools → ToolWindow1**.
- *Проверка (разработчик, глазами):* окно открывается с заголовком «RPA Writer».

### Этап 1 — Подключение ядра `RPAWriter.Core`
- В `RPAWriter2.csproj` добавить `ProjectReference` на `..\RPAWriter.Core\RPAWriter.Core.csproj` (тянет `RPAWriter.Core.dll`, а также `DocumentFormat.OpenXml.dll` + `DocumentFormat.OpenXml.Framework.dll` транзитивно).
- Добавить `PackageReference` на `Newtonsoft.Json` (13.0.3) и явно включить его в VSIX через `VSIXSourceItem` (`$(OutputPath)Newtonsoft.Json.dll`) — VSSDK исключает его из VSIX по умолчанию.
- *Проверка (разработчик):* VSIX собирается; в папке расширения есть `RPAWriter.Core.dll`, `Newtonsoft.Json.dll`, `DocumentFormat.OpenXml.dll`, `DocumentFormat.OpenXml.Framework.dll`; расширение грузится без ошибок.

### Этап 2 — Настройки + AI-провайдер + чат
- `Services/SettingsStore.cs` — загрузка/сохранение JSON (`BaseUrl`, `ApiKey`, `Model`, `KnowledgeBasePath`) в `%AppData%\RPAWriter2\settings.json`.
- `Services/AiProviderFactory.cs` — создаёт `StubAiProvider` (нет ключа) или `OpenAiCompatibleProvider`.
- В `ToolWindow1Control`: секция настроек + кнопка «Сохранить»; чат (история + поле ввода + «Отправить»).
- *Проверка (разработчик):* сообщение в чате → ответ заглушки; настройки переживают перезапуск VS; с ключом чат идёт в реальный провайдер.

### Этап 3 — Импорт ТЗ
- Кнопка «Импорт ТЗ» → выбор `.docx/.xlsx/.txt/.md` → `OpenXmlDocumentReader().Read()` + `DecomposeTasksAsync()` → вывод текста и списка задач.
- *Проверка (разработчик):* выбираем `.docx` → в окне текст и список задач.

### Этап 4 — Поиск похожих
- Кнопка «Поиск похожих» → `FileKnowledgeBase(KbPath()).FindSimilarTasksAsync(task, 3)` по задачам текущего ТЗ.
- *Проверка (разработчик):* при непустой БЗ выводятся совпадения со score и причинами.

### Этап 5 — Генерация робота
- Кнопка «Сгенерировать робота» → `RpaPipelineService(reader, ai, kb, new RobotGenerator()).GenerateRobotFromSpecAsync()` → сохранить `.xaml`/`.Actions.cs` рядом с ТЗ, показать в окне.
- *Проверка (разработчик):* создаются файлы XAML/C#.

### Этап 6 — Формирование базы знаний
- Кнопка «Сформировать БЗ» → мультивыбор документации → `KnowledgeBaseFormationService(reader, ai, kb).BuildFromRobotDocsAsync()`.
- *Проверка (разработчик):* в `index.json` появляются записи решений.

### Этап 7 — Сборка Release (1 клик)
- Кнопка «Собрать Release» → DTE2: активировать конфигурацию Release и собрать (через `BuildFeedbackService`).
- *Проверка (разработчик):* открытое решение собирается в Release.

### Этап 8 — Анализ логов
- Кнопка «Анализ логов» → мультивыбор файлов → `LogAnalyzer().Analyze(lines)` → вывод summary/ошибок.
- *Проверка (разработчик):* логи анализируются, ошибки выводятся.

### Этап 9 — Конвертация UiPath → WF
- Кнопка «UiPath → WF» → выбор `.xaml` → `UiPathToWfConverter(new RobotGenerator()).Convert()` → сохранить `.wf.xaml`/`.wf.cs`, показать.
- *Проверка (разработчик):* создаются файлы.

### Этап 10 — Финализация
- Удалить плейсхолдер `Command1.cs` и кнопку «Invoke Command1» из `.vsct`.
- Переименовать пункт меню «ToolWindow1» → «RPA Writer» (или «Открыть RPA Writer»).
- Итоговое сквозное тестирование.
- *Проверка (разработчик):* полный проход сценария из раздела 8 RPAWriter.md.

## 5. Технические примечания

- **WPF:** обновление UI из фоновых потоков — через `Dispatcher.BeginInvoke` (аналог WinForms `InvokeRequired`).
- **Потоки VS:** работа с DTE и окном VS — только на UI-потоке (`ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync`).
- **Обработчики кнопок** — `async void` + `try/catch` с выводом ошибки в окно.
- **VSIX-зависимости:** `Newtonsoft.Json` и `DocumentFormat.OpenXml` (транзитивные от `RPAWriter.Core`) нужно явно включить в контейнер.
- **Диалоги:** предпочтительно WPF-нативные (`Microsoft.Win32.OpenFileDialog`, `System.Windows.MessageBox`); `System.Windows.Forms` тоже доступен в проекте при необходимости.

## 6. Риски

- WPF vs WinForms: интерфейс переписывается с нуля (логика из Core сохраняется).
- Пакет VSIX должен включать транзитивные зависимости (этап 1).
- `RemoteLogService` (удалённые логи) — пока заглушка, подключается позже; не блокирует остальные этапы.
