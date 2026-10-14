# Несоответствия RPAWriter.md ↔ реализация

Список расхождений между документом `RPAWriter.md` и фактическим кодом (проекты `RPAWriter.Core`, `RPAWriter2`, `RPAWriter.Tests`).

- [ ] **`NpoiDocumentReader` не реализован.** Док (§ 2): «NPOI для legacy (.doc/.xls)». Фактически пакета NPOI нет; `OpenXmlDocumentReader` читает только `.docx/.xlsx/.txt/.md/.markdown`.

- [ ] **`SolutionTemplateEngine` не подключён.** Не используется ни в `RpaPipelineService`, ни в UI — похожие решения (`ReusedSolutions`) только собираются, но не параметризуются (режим «Переиспользование по аналогии», § 5 п.2).

- [ ] **Удалённые логи не подключены.** Док (§ 8 п.6): кнопка скачивает логи с удалённых серверов через `IRemoteLogService`. Фактически `RemoteLogService.DownloadAsync` бросает `NotSupportedException` и в UI не вызывается; «Анализ логов» читает локальные файлы напрямую `LogAnalyzer`.
- [ ] **Локальные логи (ActivityLog/Event Log) не реализованы.** `ILogAccessService.ReadLocalLogsAsync` читает только файлы по путям; ActivityLog/Event Log не читаются, сервис в UI не используется.

- [ ] **Чтение ошибок и окна вывода — заглушки.** `GetCompilationErrorsAsync` и `GetOutputWindowTextAsync` возвращают пустые значения с `TODO` (IErrorList/IVsOutputWindow не подключены). Реализован только `BuildReleaseAsync`. Цикл «ошибки → исправить → пересобрать» не реализован.

- [ ] **`Flowchart` не поддерживается.** В enum `ActivityNodeKind` нет `Flowchart`; `RobotGenerator` не эмитит его (конвертер UiPath понижает `Flowchart` → `Sequence`).
- [ ] **`InvokeMethod` → `Result` → переменная не реализовано.** `EmitInvokeMethod` не эмитит `Result`/`OutArgument`.
- [ ] **Сигнатуры C#-методов.** Док (§ 3.2): методы с параметрами/возвратом. Фактически `EmitHelperCode` генерирует `public static void Метод()` без параметров и с `// TODO: implement`.
