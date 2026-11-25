namespace AstroNucleaus.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
