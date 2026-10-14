# RPAWriter — расширение Visual Studio 2026 для разработки RPA-роботов на Workflow Foundation

## 1. Обзор

**RPAWriter** — VSIX-расширение для Visual Studio, которое помогает писать RPA-роботов на базе Windows Workflow Foundation (WF) с использованием AI-агента.

Агент умеет (реализовано):

- читать технические задания (ТЗ) из **Word** (`.docx`/`.doc`) и **Excel** (`.xlsx`/`.xls`), а также текст (`.txt`) и markdown (`.md`/`.markdown`);
- **искать по базе знаний** похожие задачи и предлагать решения на базе аналогичных роботов;
- работать в режиме **формирования базы знаний** (читать документацию роботов, разбивать её на мелкие задачи и находить решение по каждой задаче в коде робота);
- собирать проект в **Release за один клик**;
- анализировать **логи** (локальные файлы);
- **переводить роботов с UiPath на Workflow Foundation** (XAML → WF);
- общаться с агентом в **свободном чате**.

В планах (см. § 13 «Необходимые доработки»):

- скачивать и анализировать **логи с удалённых серверов**;
- читать локальные логи **ActivityLog / Event Log**;
- читать **ошибки компиляции** и **окно вывода VS** и исправлять код по ним.

## 2. Ключевые решения

| Вопрос | Решение |
|---|---|
| Целевая платформа | Visual Studio, VSIX на `net472` |
| Workflow Foundation | `System.Activities` (`net472`) |
| Формат вывода агента | **только два пути**: XAML и C#-код, вызываемый через XAML |
| AI-провайдер | **OpenAI-совместимый** (BaseUrl/Key/Model), заглушка как офлайн-фолбэк |
| Чтение документов | Open XML SDK (`.docx`/`.xlsx`) + NPOI (legacy `.doc`/`.xls`); `.txt`/`.md` — как текст |
| База знаний | локальный каталог (JSON-индекс), без БД/векторного хранилища |
| Сходство задач | токенное (Jaccard) с бустами за тип активности/действие |
| Удалённые логи | контракт + заглушка, транспорт подключается позже |

## 3. Формат вывода агента (строго два пути)

Агент генерирует **только** два вида артефактов:

### 3.1. XAML (workflow)
Определение workflow из **стандартных** активностей WF:

- управляющие: `Sequence`, `If`, `While`, `ForEach`;
- передача данных: **`Assign`** (значения в переменные/аргументы);
- вызов кода: **`InvokeMethod`** (метод C#-класса);
- прочее: `WriteLine`, `Delay`.

Переменные/аргументы объявляются в XAML (`Variable`/`Argument`).

### 3.2. C#-код, запускаемый через XAML
Обычные классы с методами (бизнес-логика/действия), которые XAML вызывает через стандартную активность **`InvokeMethod`**, а данные передаёт через **`Assign`**.

Сейчас генератор создаёт методы-заглушки без параметров:

```csharp
public static class RpaActions
{
    public static void OpenBrowser() { /* TODO: implement */ }
}
```

**Ограничение:** не генерируются произвольные C#-программы и не используются кастомные подклассы `CodeActivity`/`NativeActivity` — только стандартные активности WF + обычные C#-методы.

## 4. Архитектура

### 4.1. Контракты (интерфейсы)

| Интерфейс | Назначение |
|---|---|
| `IAiProvider` | анализ ТЗ, декомпозиция на задачи, маппинг задачи на решение, генерация робота, чат |
| `IDocumentReader` | чтение Word/Excel/текста → `SpecModel` |
| `IKnowledgeBase` | добавление/поиск похожих задач, получение решения |
| `ILogAccessService` | локальные логи (файлы) |
| `IRemoteLogService` | скачивание и анализ удалённых логов |
| `IBuildFeedbackService` | 1-клик Release-сборка, ошибки компиляции, окно вывода |
| `IRobotGenerator` | `EmitWorkflowXaml(plan)` + `EmitHelperCode(plan)` |
| `IUiPathConverter` | конвертация UiPath (XAML) → `RobotPlan` |

### 4.2. Сервисы

- `OpenAiCompatibleProvider` — реализация `IAiProvider` через `POST {BaseUrl}/chat/completions` (OpenAI / Azure OpenAI / Ollama / LM Studio / vLLM).
- `StubAiProvider` — детерминированная заглушка (офлайн/демо-режим).
- `OpenXmlDocumentReader` — чтение документов (`.docx`/`.xlsx`/`.txt`/`.md`/`.markdown`); legacy `.doc`/`.xls` делегирует в `NpoiDocumentReader`.
- `NpoiDocumentReader` — чтение legacy-документов (`.doc`/`.xls`) через NPOI.
- `FileKnowledgeBase` + `SimilarityIndex` — хранилище (JSON-индекс) и поиск сходства.
- `KnowledgeBaseFormationService` — режим формирования БЗ.
- `SolutionTemplateEngine` — параметризация решения-аналога (пока не подключён к конвейеру).
- `LocalLogAccessService`, `RemoteLogService` + `LogAnalyzer` — работа с логами.
- `BuildFeedbackService` — 1-клик Release-сборка (`DTE2`).
- `RobotGenerator` — эмиттеры XAML и C#.
- `UiPathToWfConverter` — конвертация UiPath → WF.
- `RpaPipelineService` — оркестратор сквозного конвейера.
- `TextNormalizer` — токенизация и стоп-слова (RU/EN).

### 4.3. UI: единое окно «RPA Writer»

Расширение предоставляет **одно окно** (`ToolWindow1` + `ToolWindow1Control`) с вкладками **«Чат»**, **«ТЗ»** и **«Настройки»**:

- **чат с агентом** — история сообщений + поле ввода;
- **кнопки** на вкладке «ТЗ»: «Импорт ТЗ», «Поиск похожих», «Сгенерировать робота», «Сформировать БЗ», «Опубликовать» (Release-сборка), «Анализ логов», «UiPath → WF»;
- **настройки прямо в окне** (Base URL, API-ключ, модель, каталог БЗ) с кнопкой «Сохранить настройки» (хранятся в JSON: `%AppData%\RPAWriter2\settings.json`);
- **предпросмотр результата** (сгенерированный XAML/C#, найденные похожие решения, сводка по логам).

Меню **Tools** содержит кнопку **«RPA Writer»**, открывающую это окно.

## 5. Режимы работы агента

1. **Генерация с нуля** — ТЗ → анализ → XAML + C#.
2. **Переиспользование по аналогии** — поиск похожих ТЗ → предложение решения из аналогичного робота (сейчас решения только собираются в `ReusedSolutions`; параметризация через `SolutionTemplateEngine` — в планах).
3. **Гибрид** — анализ нового ТЗ + контекст найденных похожих решений.
4. **Формирование базы знаний** — документация соседних роботов → мелкие задачи → решение в коде робота.

## 6. Модель данных

| Модель | Описание |
|---|---|
| `SpecModel` | нормализованное ТЗ (имя, путь, текст, задачи) |
| `TaskDescriptor` | атомарная задача: действие, объект, вход/выход, ключевые слова, тип активности |
| `SpecAnalysis` | результат анализа ТЗ агентом |
| `ActivityNodeKind` | вид узла workflow (`Sequence/If/While/ForEach/Assign/InvokeMethod/WriteLine/Delay`) |
| `ActivityNode` | узел плана робота (вид, имя, класс/метод, аргументы, переменная, выражение, дети) |
| `ArgumentAssignment` | аргумент для `InvokeMethod` (параметр/значение) |
| `RobotPlan` | дерево WF-активностей |
| `SimilarTaskMatch` | найденная похожая задача + оценка сходства + объяснение |
| `SolutionRecord` | задача + решение (XAML/C#-фрагмент) + исходный робот + документация |
| `RobotDeploymentConfig` | настройки доступа к серверу робота (транспорт, креды, пути к логам) |
| `BuildError` | ошибка/предупреждение компиляции (код, сообщение, файл, строка, важность) |
| `LogEntry` | запись лога (метка времени, уровень, источник, сообщение) |
| `LogAnalysis` | результат анализа логов (записи, ошибки, предупреждения, сводка) |
| `PipelineResult` | результат сквозного конвейера (ТЗ, анализ, план, XAML, C#, переиспользованное) |
| `UiPathConversionResult` | результат конвертации UiPath → WF (план, XAML, C#, предупреждения) |

## 7. Режим формирования базы знаний (детали)

1. Читается документация соседних роботов (Word/текст/markdown; PDF — позже).
2. Агент разбивает документацию на мелкие задачи (`TaskDescriptor[]`).
3. Для каждой задачи агент находит решение в роботе (`MapTaskToSolutionAsync`):
   - локализует фрагмент XAML;
   - локализует C#-метод (по связям `InvokeMethod`/`Assign`).
4. Сохраняет `SolutionRecord` в базу знаний.

Благодаря этому режим «поиск похожих ТЗ» предлагает не просто похожий робот, а конкретный фрагмент кода.

## 8. Сквозной сценарий работы

1. Импорт ТЗ (Word/Excel/текст) → `SpecModel` + `TaskDescriptor`.
2. Поиск похожих ТЗ в базе знаний → предложение решения по аналогии.
3. Генерация: XAML (стандартные активности, `InvokeMethod`/`Assign`) + C#-классы.
4. **1-клик Release-сборка** (кнопка «Опубликовать»).
5. *(в планах)* Агент читает **ошибки компиляции + окно вывода** → исправляет XAML/C# → повторная сборка.
6. *(в планах)* **Кнопка «Скачать и проанализировать логи»** → логи с удалённых серверов → анализ → в контекст агента.
7. *(в планах)* Локальные логи (ActivityLog, Event Log) — также в контекст.

## 9. Структура проекта

```
RPAWriter.slnx
RPAWriter.Core/                         (netstandard2.0 — контракты, модели, сервисы)
  RPAWriter.Core.csproj
  Contracts/Contracts.cs
  Models/Models.cs
  Services/
    OpenAiCompatibleProvider.cs
    StubAiProvider.cs
    OpenXmlDocumentReader.cs
    NpoiDocumentReader.cs
    FileKnowledgeBase.cs
    SimilarityIndex.cs
    KnowledgeBaseFormationService.cs
    SolutionTemplateEngine.cs
    LocalLogAccessService.cs
    RemoteLogService.cs
    LogAnalyzer.cs
    TextNormalizer.cs
    RobotGenerator.cs
    UiPathToWfConverter.cs
    RpaPipelineService.cs
RPAWriter2/                             (net472 — VSIX)
  RPAWriter2.csproj
  source.extension.vsixmanifest
  RPAWriter2Package.cs
  RPAWriter2Package.vsct
  ToolWindow1.cs
  ToolWindow1Command.cs
  ToolWindow1Control.xaml / .xaml.cs
  Services/
    AiProviderFactory.cs
    BuildFeedbackService.cs
    SettingsStore.cs
RPAWriter.Tests/                        (тесты)
  RPAWriter.Tests.csproj
```

## 10. Порядок реализации

1. Контракты/модели + `OpenAiCompatibleProvider` (+ `StubAiProvider`).
2. **Чтение ТЗ** (Word/Excel, вкл. legacy `.doc`/`.xls`) → `SpecModel` + `TaskDescriptor`.
3. **База знаний + `SimilarityIndex`** (поиск похожих).
4. **Режим формирования БЗ** (`KnowledgeBaseFormationService`).
5. **Сборка/выводы**: `BuildFeedbackService` + 1-клик Release.
6. **Локальные + удалённые логи** (заглушка транспорта).
7. **WF-генератор**: XAML + C# (`InvokeMethod`/`Assign`).
8. **UI + оркестратор**, сквозной контур.
9. Подключение реального транспорта логов за контрактом.

## 11. Конвертация UiPath → Workflow Foundation

`IUiPathConverter` / `UiPathToWfConverter` читает UiPath `.xaml` и строит `RobotPlan`:

- **стандартные активности** (`Sequence`, `If`, `While`, `ForEach`, `Assign`, `Delay`, `WriteLine`) переносятся в соответствующие узлы WF; `Flowchart` и `DoWhile` упрощаются до `Sequence`/`While` (с предупреждением);
- **UiPath-специфичные активности** (`OpenBrowser`, `TypeInto`, `Click`, `ReadRange`, `WriteRange`, `LogMessage`, …) сопоставляются с `InvokeMethod` на C#-методах класса `RpaActions`, атрибуты (Url/Selector/Text/…) становятся аргументами; `LogMessage` → `WriteLine`;
- переменные/аргументы (`<Variable>`, `<Argument>`) и служебные свойства (`DisplayName`, `x:*`) пропускаются;
- результат генерируется в XAML + C# через `RobotGenerator` (с диагностикой по упрощённым конструкциям).

Кнопка **«UiPath → WF»** выбирает `.xaml`, создаёт рядом `.wf.xaml` и `.wf.cs`.

## 12. Допущения и ограничения

- Роботы исполняются на `net472` (`System.Activities`).
- База знаний — локальный каталог, задаваемый пользователем (JSON-индекс `index.json`), без БД/векторного хранилища.
- Сходство задач — токенное (Jaccard) с бустами за тип активности/действие; позже — эмбеддинги через `IAiProvider`.
- Манифест VSIX целится в `InstallationTarget [17.14, )`; отладка F5 требует полной IDE Visual Studio.
- Удалённые логи и реальный AI-провайдер реализуются за контрактами, чтобы транспорт/провайдера можно было подключать позже без изменения остального кода.
- Legacy `.doc` читается в режиме best-effort: NPOI не поставляет парсер HWPF (Word 97-2003), поэтому текст извлекается из OLE2-контейнера (поток «WordDocument») по флагу Unicode в FIB; `.xls` читается полностью через HSSF.

## 13. Необходимые доработки

- [ ] **Подключить `SolutionTemplateEngine`.** Сейчас похожие решения только собираются в `ReusedSolutions`, но не параметризуются под новое ТЗ (режим «Переиспользование по аналогии»).
- [ ] **Скачивание удалённых логов.** `RemoteLogService.DownloadAsync` бросает `NotSupportedException` и не вызывается в UI; «Анализ логов» читает только локальные файлы.
- [ ] **Чтение локальных логов ActivityLog / Event Log.** `ILogAccessService.ReadLocalLogsAsync` читает только файлы по путям.
- [ ] **Чтение ошибок компиляции и окна вывода.** `GetCompilationErrorsAsync` и `GetOutputWindowTextAsync` возвращают пустые заглушки (IErrorList/IVsOutputWindow не подключены); цикл «ошибки → исправить → пересобрать» не реализован.
- [ ] **Поддержка активности `Flowchart`.** В `ActivityNodeKind`/`RobotGenerator` её нет; конвертер UiPath понижает `Flowchart` → `Sequence`.
- [ ] **Эмитить `Result`/`OutArgument` в `InvokeMethod`** (связь результата с переменной).
- [ ] **Генерировать C#-методы с параметрами и возвратом.** Сейчас `EmitHelperCode` генерирует `public static void Метод()` без параметров и с `// TODO: implement`.