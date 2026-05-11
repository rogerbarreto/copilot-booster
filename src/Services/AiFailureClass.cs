namespace CopilotBooster.Services;

internal enum AiFailureClass
{
    Timeout,
    ProcessSpawn,
    ProcessFailure,
    MalformedJson,
    SchemaViolation,
    NoCandidates,
}
