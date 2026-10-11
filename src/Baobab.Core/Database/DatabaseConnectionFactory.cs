using System.Data.Common;

namespace Baobab.Core.Database;

public static class DatabaseConnectionFactory
{
    public static DbConnection Create(string providerInvariantName, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerInvariantName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var factory = DbProviderFactories.GetFactory(providerInvariantName);
        var connection = factory.CreateConnection()
            ?? throw new InvalidOperationException(
                $"The database provider '{providerInvariantName}' did not create a connection.");

        connection.ConnectionString = connectionString;
        return connection;
    }
}
