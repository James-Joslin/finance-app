using System;
using System.Data;
using System.Globalization;

namespace financesApi.utilities
{
    public static class Utils
    {
        public static string ToTitleCase(this string source) => ToTitleCase(source, null);

        public static string ToTitleCase ( this string source, CultureInfo culture )
        {
            culture = culture ?? CultureInfo.CurrentUICulture;
            return culture.TextInfo.ToTitleCase(source.ToLower());
        }
    }
}
