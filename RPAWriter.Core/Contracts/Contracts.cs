using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Contracts
{
    /// <summary>
    /// AI-агент: анализ ТЗ, декомпозиция на задачи, маппинг задачи на решение,
    /// генерация плана робота. Реализация OpenAI-совместимая или заглушка.
    /// </summary>
    public interface IAiProvider
    {
        Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default);
        Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default);
        Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, string robotCode, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Чтение технических заданий из Word/Excel/текста.
    /// </summary>
    public interface IDocumentReader
    {
        SpecModel Read(string path);
    }

    /// <summary>
    /// База знаний: хранение решений и поиск похожих задач.
    /// </summary>
    public interface IKnowledgeBase
    {
        Task AddAsync(SolutionRecord record, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SolutionRecord>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SimilarTaskMatch>> FindSimilarTasksAsync(TaskDescriptor query, int topK, CancellationToken cancellationToken = default);
        Task<SolutionRecord> GetSolutionAsync(string id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Доступ к локальным логам (файлы, ActivityLog, Event Log).
    /// </summary>
    public interface ILogAccessService
    {
        Task<IReadOnlyList<string>> ReadLocalLogsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Скачивание и анализ удалённых логов по роботу.
    /// </summary>
    public interface IRemoteLogService
    {
        Task<IReadOnlyList<string>> DownloadAsync(RobotDeploymentConfig config, CancellationToken cancellationToken = default);
        Task<LogAnalysis> AnalyzeAsync(IEnumerable<string> lines, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Сборка Release в 1 клик, чтение ошибок компиляции и окна вывода VS.
    /// </summary>
    public interface IBuildFeedbackService
    {
        Task BuildReleaseAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default);
        Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Генерация артефактов робота: XAML (стандартные WF-активности) и C#-код,
    /// вызываемый через InvokeMethod/Assign.
    /// </summary>
    public interface IRobotGenerator
    {
        string EmitWorkflowXaml(RobotPlan plan);
        string EmitHelperCode(RobotPlan plan);
    }

    /// <summary>
    /// Конвертация робота из UiPath (XAML) в чистый Workflow Foundation.
    /// </summary>
    public interface IUiPathConverter
    {
        UiPathConversionResult Convert(string uipathXaml);
    }
}
