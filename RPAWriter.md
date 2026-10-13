# RPAWriter — расширение Visual Studio 2026 для разработки RPA-роботов на Workflow Foundation

## 1. Обзор

**RPAWriter** — VSIX-расширение для Visual Studio 2026 (18.x), которое помогает писать RPA-роботов на базе Windows Workflow Foundation (WF) с использованием AI-агента.

Агент умеет:

- читать технические задания (ТЗ) из **Word** и **Excel**;
- получать **доступ к логам** (локальным и удалённым);
- **искать по другим ТЗ** похожие задачи и предлагать решения на базе аналогичных роботов;
- читать **ошибки компиляции** и **окно вывода VS**;
- собирать проект в **Release за один клик**;
- скачивать и анализировать **логи с удалённых серверов** по заданному роботу;
- работать в режиме **формирования базы знаний** (читать документацию соседних роботов, разбивать её на мелкие задачи и находить решение по каждой задаче в коде робота);
- **переводить роботов с UiPath на чистый Workflow Foundation** (XAML → WF).

## 2. Ключевые решения

| Вопрос | Решение |
|---|---|
| Целевая платформа | Visual Studio 2026 (18.x), VSIX на `net472` |
| Workflow Foundation | `System.Activities` (`net472`) |
| Формат вывода агента | **только два пути**: XAML и C#-код, вызываемый через XAML |
| AI-провайдер | **OpenAI-совместимый** (BaseUrl/Key/Model), заглушка как офлайн-фолбэк |
| Чтение документов | Open XML SDK (`.docx`/`.xlsx`), NPOI для legacy (`.doc`/`.xls`) |
| База знаний | локальный каталог, без БД/векторного хранилища |
| Сходство задач | сначала токенное (TF-IDF/BM25), позже эмбеддинги |
| Удалённые логи | контракт + заглушка, транспорт подключается позже |

## 3. Формат вывода агента (строго два пути)

Агент генерирует **только** два вида артефактов:

### 3.1. XAML (workflow)
Определение workflow из **стандартных** активностей WF:

- управляющие: `Sequence`, `Flowchart`, `If`, `While`, `ForEach`;
- передача данных: **`Assign`** (значения в переменные/аргументы);
- вызов кода: **`InvokeMethod`** (метод C#-класса, `Result` → переменная);
- прочее: `WriteLine`, `Delay` и т.п.

Переменные/аргументы объявляются в XAML (`Variable`/`Argument`).

### 3.2. C#-код, запускаемый через XAML
Обычные классы с методами (бизнес-логика/действия), которые XAML вызывает через стандартную активность **`InvokeMethod`**, а данные передаёт через **`Assign`**.

Пример:

```csharp
public static class RpaActions
{
    public static void OpenBrowser(string url) { /* ... */ }
    public static string ReadTable(string path) { /* ... */ }
}
```

**Ограничение:** не генерируются произвольные C#-программы и не используются кастомные подклассы `CodeActivity`/`NativeActivity` — только стандартные активности WF + обычные C#-методы.

## 4. Архитектура

### 4.1. Контракты (интерфейсы)

| Интерфейс | Назначение |
|---|---|
| `IAiProvider` | анализ ТЗ, декомпозиция на задачи, маппинг задачи на решение, генерация робота |
| `IDocumentReader` | чтение Word/Excel → `SpecModel` |
| `IKnowledgeBase` | добавление/поиск похожих задач, получение решения |
| `ILogAccessService` | локальные логи (ActivityLog, Event Log, файлы) |
| `IRemoteLogService` | скачивание и анализ удалённых логов |
| `IBuildFeedbackService` | 1-клик Release-сборка, ошибки компиляции, окно вывода |
| `IRobotGenerator` | `EmitWorkflowXaml(plan)` + `EmitHelperCode(plan)` |

### 4.2. Сервисы

- `OpenAiCompatibleProvider` — реализация `IAiProvider` через `POST {BaseUrl}/chat/completions` (OpenAI / Azure OpenAI / Ollama / LM Studio / vLLM).
- `StubAiProvider` — детерминированная заглушка (офлайн/демо-режим).
- `OpenXmlDocumentReader` / `NpoiDocumentReader` — чтение документов.
- `FileKnowledgeBase` + `SimilarityIndex` — хранилище и поиск сходства.
- `KnowledgeBaseFormationService` — режим формирования БЗ.
- `SolutionTemplateEngine` — параметризация решения-аналога под новое ТЗ.
- `LogAccessService`, `RemoteLogCollector` + `LogAnalyzer` — работа с логами.
- `BuildFeedbackService` — `IErrorList`, `IVsOutputWindow`, `DTE2.BuildEvents`.
- `RobotGenerator` — эмиттеры XAML и C#.
- `RpaPipelineService` — оркестратор сквозного конвейера.

### 4.3. Команды и UI

| Команда | Назначение |
|---|---|
| `ImportSpecCommand` | импорт ТЗ (Word/Excel) |
| `FindSimilarCommand` | поиск похожих ТЗ |
| `GenerateRobotCommand` | генерация робота |
| `FormKnowledgeBaseCommand` | формирование базы знаний |
| `BuildReleaseCommand` | сборка Release в 1 клик |
| `DownloadAnalyzeLogsCommand` | скачивание/анализ удалённых логов |
| `ShowLogsCommand` | просмотр логов |

Tool Windows:

- **«RPA Writer»** — чат, список похожих ТЗ, предпросмотр кода, кнопки.
- **«Логи»** — просмотр локальных и удалённых логов.

Options-страница: настройки провайдера, каталог базы знаний, конфиги роботов/серверов, источники логов.

## 5. Режимы работы агента

1. **Генерация с нуля** — ТЗ → анализ → XAML + C#.
2. **Переиспользование по аналогии** — поиск похожих ТЗ → предложение решения из аналогичного робота.
3. **Гибрид** — анализ нового ТЗ + контекст найденных похожих решений.
4. **Формирование базы знаний** — документация соседних роботов → мелкие задачи → решение в коде робота.

## 6. Модель данных

| Модель | Описание |
|---|---|
| `SpecModel` | нормализованное ТЗ (шаги, таблицы, входные/выходные данные) |
| `TaskDescriptor` | атомарная задача: действие, объект, вход/выход, доменные ключи, тип активности |
| `SpecAnalysis` | результат анализа ТЗ агентом |
| `RobotPlan` | дерево WF-активностей (`Sequence/If/While/ForEach/Assign/InvokeMethod`) |
| `SimilarTaskMatch` | найденная похожая задача + оценка сходства + объяснение |
| `SolutionRecord` | задача + решение (XAML/C#-фрагмент) + исходный робот + документация |
| `RobotDeploymentConfig` | настройки доступа к серверу робота (транспорт, креды, пути к логам) |
| `BuildError` | ошибка/предупреждение компиляции (код, сообщение, файл, строка) |
| `LogEntry` | запись лога (метка времени, уровень, источник, сообщение) |

## 7. Режим формирования базы знаний (детали)

1. Читается документация соседних роботов (Word/текст/markdown; PDF — позже).
2. Агент разбивает документацию на мелкие задачи (`TaskDescriptor[]`).
3. Для каждой задачи агент находит решение в роботе (`MapTaskToSolutionAsync`):
   - локализует фрагмент XAML;
   - локализует C#-метод (по связям `InvokeMethod`/`Assign`).
4. Сохраняет `SolutionRecord` в базу знаний.

Благодаря этому режим «поиск похожих ТЗ» предлагает не просто похожий робот, а конкретный фрагмент кода.

## 8. Сквозной сценарий работы

1. Импорт ТЗ (Word/Excel) → `SpecModel` + `TaskDescriptor`.
2. Поиск похожих ТЗ в базе знаний → предложение решения по аналогии.
3. Генерация: XAML (стандартные активности, `InvokeMethod`/`Assign`) + C#-классы.
4. **1-клик Release-сборка** (`BuildReleaseCommand`).
5. Агент читает **ошибки компиляции + окно вывода** → исправляет XAML/C# → повторная сборка.
6. **Кнопка «Скачать и проанализировать логи»** → логи с удалённых серверов → анализ → в контекст агента.
7. Локальные логи (ActivityLog, Event Log) — также в контекст.

## 9. Целевая структура проекта

```
RPAWriter/
  RPAWriter.csproj                    (net472, VSIX)
  source.extension.vsixmanifest
  RPAWriterPackage.cs
  Commands/
    ImportSpecCommand.cs
    FindSimilarCommand.cs
    GenerateRobotCommand.cs
    FormKnowledgeBaseCommand.cs
    BuildReleaseCommand.cs
    DownloadAnalyzeLogsCommand.cs
    ShowLogsCommand.cs
  ToolWindows/
    RpaWriterToolWindow.cs (+ control)
    LogsToolWindow.cs
  Contracts/
    IAiProvider.cs
    IDocumentReader.cs
    IKnowledgeBase.cs
    ILogAccessService.cs
    IRemoteLogService.cs
    IBuildFeedbackService.cs
    IRobotGenerator.cs
  Models/
    SpecModel.cs, TaskDescriptor.cs, SpecAnalysis.cs,
    RobotPlan.cs, SimilarTaskMatch.cs, SolutionRecord.cs,
    RobotDeploymentConfig.cs, BuildError.cs, LogEntry.cs
  Services/
    OpenAiCompatibleProvider.cs
    StubAiProvider.cs
    OpenXmlDocumentReader.cs
    FileKnowledgeBase.cs, SimilarityIndex.cs
    KnowledgeBaseFormationService.cs
    SolutionTemplateEngine.cs
    LogAccessService.cs, RemoteLogCollector.cs, LogAnalyzer.cs
    BuildFeedbackService.cs
    RobotGenerator.cs
    RpaPipelineService.cs
  Options/
    RpaWriterOptionsPage.cs
```

## 10. Порядок реализации

1. **VS 2026 (18.x)** + контракты/модели + `OpenAiCompatibleProvider` (+ `StubAiProvider`).
2. **Чтение ТЗ** (Word/Excel) → `SpecModel` + `TaskDescriptor`.
3. **База знаний + `SimilarityIndex`** (поиск похожих).
4. **Режим формирования БЗ** (`KnowledgeBaseFormationService`).
5. **Сборка/выводы**: `BuildFeedbackService` + 1-клик Release.
6. **Локальные + удалённые логи** (заглушка транспорта).
7. **WF-генератор**: XAML + C# (`InvokeMethod`/`Assign`).
8. **UI + оркестратор**, сквозной контур.
9. Подключение реального транспорта логов за контрактом.

## 11. Конвертация UiPath → Workflow Foundation

`IUiPathConverter` / `UiPathToWfConverter` читает UiPath `.xaml` и строит `RobotPlan`:

- **стандартные активности** (`Sequence`, `Flowchart`, `If`, `While`, `DoWhile`, `ForEach`, `Assign`, `Delay`, `WriteLine`) переносятся в соответствующие узлы WF;
- **UiPath-специфичные активности** (`OpenBrowser`, `TypeInto`, `Click`, `ReadRange`, `WriteRange`, `LogMessage`, …) сопоставляются с `InvokeMethod` на C#-методах класса `RpaActions`, атрибуты (Url/Selector/Text/…) становятся аргументами;
- переменные/аргументы (`<Variable>`, `<Argument>`) и служебные свойства (`DisplayName`, `x:*`) пропускаются;
- результат генерируется в XAML + C# через `RobotGenerator` (с диагностикой по упрощённым конструкциям).

Команда в меню: **«Конвертировать UiPath → WF»** — выбирает `.xaml`, создаёт рядом `.wf.xaml` и `.wf.cs`.

## 12. Допущения и ограничения

- Роботы исполняются на `net472` (`System.Activities`).
- База знаний — локальный каталог (`%AppData%\RPAWriter\KnowledgeBase\`), без БД/векторного хранилища.
- Сходство задач на старте — токенное (TF-IDF/BM25), позже эмбеддинги через `IAiProvider`.
- На машине разработки нет полной IDE VS 2026 (есть VS 2022 17.13 + Build Tools 2026 18.5) — сборка VSIX возможна, отладка F5 потребует полной VS 2026.
- Удалённые логи и реальный AI-провайдер реализуются за контрактами, чтобы транспорт/провайдера можно было подключать позже без изменения остального кода.


