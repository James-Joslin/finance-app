namespace financesApi.models;

public sealed record PortableImportSummary(
    IReadOnlyDictionary<string, int> Records,
    int Images,
    string Format,
    int Version);

public sealed record PortableExportResult(byte[] Content, string ContentType, string FileName);
