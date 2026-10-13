using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Удалённые логи: скачивание по контракту (транспорт подключается позже),
    /// анализ — через LogAnalyzer.
    /// </summary>
    public class RemoteLogService : IRemoteLogService
    {
        private readonly LogAnalyzer _analyzer = new LogAnalyzer();

        public Task<IReadOnlyList<string>> DownloadAsync(
            RobotDeploymentConfig config, CancellationToken cancellationToken = default)
        {
            // Транспорт (SSH/HTTP/SMB) ещё не подключён — контракт + заглушка.
            throw new NotSupportedException(
                "Транспорт для скачивания удалённых логов ещё не подключён (контракт + заглушка).");
        }

        public Task<LogAnalysis> AnalyzeAsync(
            IEnumerable<string> lines, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_analyzer.Analyze(lines));
        }
    }
}
