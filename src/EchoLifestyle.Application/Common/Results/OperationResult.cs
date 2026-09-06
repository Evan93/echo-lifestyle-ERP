namespace EchoLifestyle.Application.Common.Results;

/// <summary>
/// The outcome of a use case that can fail for business reasons.
///
/// Business failures - a duplicate code, an unauthorised branch, a closed
/// period - are expected outcomes, not exceptions. Exceptions stay for genuine
/// faults, which keeps the logs meaningful and stops "handled" failures being
/// dressed up as errors.
/// </summary>
public class OperationResult
{
    protected OperationResult(bool succeeded, string? error, string? field)
    {
        Succeeded = succeeded;
        Error = error;
        Field = field;
    }

    public bool Succeeded { get; }

    public string? Error { get; }

    /// <summary>Form field the error belongs to, when it maps to one.</summary>
    public string? Field { get; }

    public static OperationResult Success() => new(true, null, null);

    public static OperationResult Failure(string error, string? field = null) =>
        new(false, error, field);
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool succeeded, T? value, string? error, string? field)
        : base(succeeded, error, field)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value) => new(true, value, null, null);

    public static new OperationResult<T> Failure(string error, string? field = null) =>
        new(false, default, error, field);
}
