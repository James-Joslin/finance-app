using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Npgsql;

namespace financesApi.utilities
{
    // PostgreSQL Data Querier - replaces your SQL Server version
    public static class PostgreSqlQuerier
    {
        public static NpgsqlConnection BuildConnection()
        {
            try
            {
                var connectionString = new NpgsqlConnectionStringBuilder
                {
                    Host = Environment.GetEnvironmentVariable("POSTGRES_HOST"),
                    Port = int.Parse(Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432"),
                    Database = Environment.GetEnvironmentVariable("POSTGRES_DB"),
                    Username = Environment.GetEnvironmentVariable("POSTGRES_USER"),
                    Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"),
                    SslMode = Enum.Parse<SslMode>(Environment.GetEnvironmentVariable("POSTGRES_SSL_MODE") ?? "Prefer"),
                    Timeout = 30
                }.ConnectionString;

                return new NpgsqlConnection(connectionString);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostgreSQL Connection Error: {ex}");
                throw;
            }
        }

        // Generic read method
        public static async Task<DataTable> ExecuteQueryAsync(string query)
        {
            try
            {
                using var connection = BuildConnection();
                await connection.OpenAsync();

                using var command = new NpgsqlCommand(query, connection);

                using var reader = await command.ExecuteReaderAsync();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostgreSQL Query Error: {ex}");
                throw;
            }
        }

        public static async Task<DataTable> ExecuteParameterisedQueryAsync(string query, Dictionary<string, object>? parameters = null)
        {
            try
            {
                using var connection = BuildConnection();
                await connection.OpenAsync();

                using var command = new NpgsqlCommand(query, connection);

                foreach (var param in parameters ?? [])
                {
                    command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }

                using var reader = await command.ExecuteReaderAsync();
                var dataTable = new DataTable();
                dataTable.Load(reader);
                return dataTable;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostgreSQL Write Error: {ex}");
                throw;
            }
        }

        // Generic write method (INSERT, UPDATE, DELETE)
        public static async Task<int> ExecuteNonQueryAsync(string query, Dictionary<string, object>? parameters = null)
        {
            try
            {
                using var connection = BuildConnection();
                await connection.OpenAsync();

                using var command = new NpgsqlCommand(query, connection);

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                return await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostgreSQL Write Error: {ex}");
                throw;
            }
        }

        // Execute and return single value (useful for getting IDs after INSERT)
        public static async Task<T?> ExecuteScalarAsync<T>(string query, Dictionary<string, object>? parameters = null)
        {
            try
            {
                using var connection = BuildConnection();
                await connection.OpenAsync();

                using var command = new NpgsqlCommand(query, connection);

                if (parameters != null)
                {
                    foreach (var param in parameters)
                    {
                        command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                    }
                }

                var result = await command.ExecuteScalarAsync();
                return result is T ? (T)result : default(T);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostgreSQL Scalar Error: {ex}");
                throw;
            }
        }

        // Transaction support for multiple operations
        public static async Task ExecuteTransactionAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> operations)
        {
            using var connection = BuildConnection();
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await operations(connection, transaction);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
