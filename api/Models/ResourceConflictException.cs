namespace financesApi.models;

public sealed class ResourceConflictException(string message) : Exception(message);
