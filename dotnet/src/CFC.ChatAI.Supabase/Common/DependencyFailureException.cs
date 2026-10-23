namespace CFC.ChatAI.Supabase.Common;

/// <summary>A sanitized failure from an external dependency, safe for application-level handling.</summary>
public class DependencyFailureException : Exception
{
    public DependencyFailureException(string dependency, string operation)
        : base($"The {dependency} dependency failed during {operation}.")
    {
        Dependency = dependency;
        Operation = operation;
    }

    public string Dependency { get; }
    public string Operation { get; }
}
