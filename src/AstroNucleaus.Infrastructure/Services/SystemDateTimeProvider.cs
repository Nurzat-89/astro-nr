using AstroNucleaus.Application.Abstractions;

namespace AstroNucleaus.Infrastructure.Services;

internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
