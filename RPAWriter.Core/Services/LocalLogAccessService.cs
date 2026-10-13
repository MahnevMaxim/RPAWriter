using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;

namespace RPAWriter.Core.Services
{
    /// <summary>Чтение локальных логов из файлов.</summary>
    public class LocalLogAccessService : ILogAccessService
    {
        public Task<IReadOnlyList<string>> ReadLocalLogsAsync(
            IEnumerable<string> paths, CancellationToken cancellationToken = default)
        {
            var result = new List<string>();
            if (paths == null) return Task.FromResult<IReadOnlyList<string>>(result);

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path))
                    result.AddRange(File.ReadAllLines(path));
            }
            return Task.FromResult<IReadOnlyList<string>>(result);
        }
    }
}
