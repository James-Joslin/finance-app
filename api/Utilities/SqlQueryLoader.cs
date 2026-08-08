namespace financesApi.utilities
{
    public static class SqlQueryLoader
    {
        public static async Task<string> GetQueryAsync(string queryName)
        {
            if (string.IsNullOrWhiteSpace(queryName) || Path.GetFileName(queryName) != queryName)
            {
                throw new ArgumentException("Query name must be a simple file name.", nameof(queryName));
            }

            var queryPath = Path.Combine(AppContext.BaseDirectory, "SqlQueries", $"{queryName}.sql");
            if (!File.Exists(queryPath))
            {
                throw new FileNotFoundException($"SQL query '{queryName}' was not found.", queryPath);
            }

            return await File.ReadAllTextAsync(queryPath);
        }
    }
}
