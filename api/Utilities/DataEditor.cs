using System;
using System.Data;
using System.Globalization;

namespace financesApi.utilities
{
    public static class DataEditor
    {
        public static Dictionary<string, List<List<string>>> ConvertData(DataTable dataTable)
        {
            var headers = dataTable.Columns.Cast<DataColumn>()
                .Select(column => column.ColumnName)
                .ToList();

            var rows = dataTable.AsEnumerable()
                .Select(row => dataTable.Columns.Cast<DataColumn>()
                    .Select(column => FormatValue(row[column]))
                    .ToList())
                .ToList();

            return new Dictionary<string, List<List<string>>>
            {
                { "Headers", new List<List<string>> { headers } },
                { "Rows", rows }
            };
        }

        private static string FormatValue(object? value)
        {
            return value switch
            {
                null or DBNull => string.Empty,
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }
    }
}
