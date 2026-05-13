using System.Text.Json.Serialization;

namespace EasySave.Shared;

// Request frame sent by the remote console to the engine over the v3 socket.
// Every action in this version of the protocol targets a specific job, so
// JobName is required.
public sealed record CommandDto(
    string JobName,
    CommandType Action,
    // Injected server-side after deserialization — never sent over the wire.
    [property: JsonIgnore] string? SourceIp = null
);
