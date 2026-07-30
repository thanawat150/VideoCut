using AutoCutStudio.Core.Models;

namespace AutoCutStudio.App;

internal sealed record JobViewRow
{
    public required JobDocument Job { get; init; }
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string Progress { get; init; }
    public required string Agent { get; init; }
    public required string Message { get; init; }
    public required string Output { get; init; }
}
