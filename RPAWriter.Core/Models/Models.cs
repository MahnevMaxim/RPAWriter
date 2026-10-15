using System;
using System.Collections.Generic;

namespace RPAWriter.Core.Models
{
    /// <summary>Атомарная задача из ТЗ: действие, объект, вход/выход, ключевые слова, тип активности.</summary>
    public class TaskDescriptor
    {
        public string Action { get; set; }
        public string Target { get; set; }
        public string Description { get; set; }
        public List<string> Inputs { get; set; } = new List<string>();
        public List<string> Outputs { get; set; } = new List<string>();
        public List<string> Keywords { get; set; } = new List<string>();
        public string ActivityType { get; set; }
    }

    /// <summary>Нормализованное техническое задание.</summary>
    public class SpecModel
    {
        public string Name { get; set; }
        public string SourcePath { get; set; }
        public string RawText { get; set; }
        public string MarkdownText { get; set; }
        public List<TaskDescriptor> Tasks { get; set; } = new List<TaskDescriptor>();
    }

    /// <summary>Результат анализа ТЗ агентом.</summary>
    public class SpecAnalysis
    {
        public string Summary { get; set; }
        public List<TaskDescriptor> Tasks { get; set; } = new List<TaskDescriptor>();
        public List<SimilarTaskMatch> ReusedSolutions { get; set; } = new List<SimilarTaskMatch>();
    }

    /// <summary>Виды узлов workflow (стандартные активности WF).</summary>
    public enum ActivityNodeKind
    {
        Sequence,
        If,
        While,
        ForEach,
        Assign,
        InvokeMethod,
        WriteLine,
        Delay
    }

    /// <summary>Аргумент для InvokeMethod.</summary>
    public class ArgumentAssignment
    {
        public string Parameter { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Узел плана робота.</summary>
    public class ActivityNode
    {
        public ActivityNodeKind Kind { get; set; }
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public List<ArgumentAssignment> Arguments { get; set; } = new List<ArgumentAssignment>();
        public string Variable { get; set; }
        public string Expression { get; set; }
        public List<ActivityNode> Children { get; set; } = new List<ActivityNode>();
    }

    /// <summary>План робота — дерево WF-активностей.</summary>
    public class RobotPlan
    {
        public string Name { get; set; }
        public ActivityNode Root { get; set; }
    }

    /// <summary>Запись в базе знаний: задача + решение (XAML/C#) + исходный робот + документация.</summary>
    public class SolutionRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<TaskDescriptor> Tasks { get; set; } = new List<TaskDescriptor>();
        public string XamlFragment { get; set; }
        public string CSharpCode { get; set; }
        public string SourceRobot { get; set; }
        public string Documentation { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    /// <summary>Найденная похожая задача с оценкой сходства.</summary>
    public class SimilarTaskMatch
    {
        public TaskDescriptor Query { get; set; }
        public SolutionRecord Solution { get; set; }
        public double Score { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>Настройки доступа к серверу робота.</summary>
    public class RobotDeploymentConfig
    {
        public string RobotId { get; set; }
        public string Transport { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public List<string> LogPaths { get; set; } = new List<string>();
        public List<string> Filters { get; set; } = new List<string>();
    }

    /// <summary>Ошибка/предупреждение компиляции.</summary>
    public class BuildError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public string Severity { get; set; }
    }

    /// <summary>Запись лога.</summary>
    public class LogEntry
    {
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Source { get; set; }
        public string Message { get; set; }
        public string RawLine { get; set; }
    }

    /// <summary>Результат анализа логов.</summary>
    public class LogAnalysis
    {
        public List<LogEntry> Entries { get; set; } = new List<LogEntry>();
        public List<LogEntry> Errors { get; set; } = new List<LogEntry>();
        public List<LogEntry> Warnings { get; set; } = new List<LogEntry>();
        public string Summary { get; set; }
    }

    /// <summary>Результат сквозного конвейера генерации робота.</summary>
    public class PipelineResult
    {
        public SpecModel Spec { get; set; }
        public SpecAnalysis Analysis { get; set; }
        public RobotPlan Plan { get; set; }
        public string Xaml { get; set; }
        public string CSharpCode { get; set; }
        public List<SimilarTaskMatch> Reused { get; set; } = new List<SimilarTaskMatch>();
    }

    /// <summary>Результат конвертации робота UiPath → Workflow Foundation.</summary>
    public class UiPathConversionResult
    {
        public RobotPlan Plan { get; set; }
        public string Xaml { get; set; }
        public string CSharpCode { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Ответ агента в свободном чате: итоговый текст + «размышления» модели (если провайдер их отдаёт).</summary>
    public class ChatResponse
    {
        public string Reasoning { get; set; }
        public string Answer { get; set; }
    }
}
