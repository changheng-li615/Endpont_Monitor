using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Interfaces;

public interface IProcessReportWriter
{
    Task WriteSnapshotAsync(ProcessSnapshot snapshot, CancellationToken cancellationToken);
}
