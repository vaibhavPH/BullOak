namespace BullOak.Console.Infrastructure;

/// <summary>
/// Maps to the "Infrastructure" section of appsettings.json.
/// </summary>
public class InfrastructureSettings
{
    public DatabaseSettings PostgreSql { get; set; } = new();
    public DatabaseSettings EventStore { get; set; } = new();
}

public class DatabaseSettings
{
    /// <summary>
    /// When true, a TestContainers Docker container is started automatically
    /// and stays alive until the application exits.
    /// When false, the ConnectionString below is used directly.
    /// </summary>
    public bool UseTestContainers { get; set; } = true;

    /// <summary>
    /// Connection string used when UseTestContainers is false.
    /// Ignored when UseTestContainers is true (container provides its own).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
