using System;
using System.Text.Json.Serialization;
using Stl.Collections;
using Stl.Text;

namespace Stl.Fusion.Authentication
{
    public sealed record SessionInfo : IHasId<Symbol>
    {
        public Symbol Id { get; init; } = Symbol.Empty;
        public DateTime CreatedAt { get; init; }
        public DateTime LastSeenAt { get; init; }
        public string IPAddress { get; init; } = "";
        public string UserAgent { get; init; } = "";
        public ImmutableOptionSet Options { get; init; } = ImmutableOptionSet.Empty;

        // Authentication
        public UserIdentity AuthenticatedIdentity { get; init; }
        public string UserId { get; init; } = "";
        public bool IsSignOutForced { get; init; }
        [JsonIgnore]
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

        public SessionInfo() { }
        public SessionInfo(string id) => Id = id;
        public SessionInfo(string id, DateTime now)
            : this(id)
        {
            CreatedAt = now;
            LastSeenAt = now;
        }
    }
}
