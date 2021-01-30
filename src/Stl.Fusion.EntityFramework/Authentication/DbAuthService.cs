using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Async;
using Stl.CommandR;
using Stl.CommandR.Commands;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;
using Stl.Fusion.Authentication.Internal;
using Stl.Fusion.Operations;

namespace Stl.Fusion.EntityFramework.Authentication
{
    public class DbAuthService<TDbContext> : DbServiceBase<TDbContext>, IServerSideAuthService
        where TDbContext : DbContext
    {
        public class Options
        {
            public TimeSpan MinUpdatePresencePeriod { get; set; } = TimeSpan.FromMinutes(3);
        }

        protected IDbUserRepo<TDbContext> Users { get; }
        protected IDbSessionInfoRepo<TDbContext> Sessions { get; }
        protected TimeSpan MinUpdatePresencePeriod { get; }

        public DbAuthService(Options options, IServiceProvider services) : base(services)
        {
            MinUpdatePresencePeriod = options.MinUpdatePresencePeriod;
            Users = services.GetRequiredService<IDbUserRepo<TDbContext>>();
            Sessions = services.GetRequiredService<IDbSessionInfoRepo<TDbContext>>();
        }

        // Commands

        public virtual async Task SignInAsync(
            SignInCommand command, CancellationToken cancellationToken = default)
        {
            var (user, authenticatedIdentity, session) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                GetSessionInfoAsync(session, default).Ignore();
                var invSessionInfo = context.Items.Get<OperationItem<SessionInfo>>().Value;
                TryGetUserAsync(invSessionInfo.UserId, default).Ignore();
                GetUserSessionsAsync(invSessionInfo.UserId, default).Ignore();
                return;
            }

            if (!user.Identities.ContainsKey(authenticatedIdentity))
                throw new ArgumentOutOfRangeException(
                    $"{nameof(command)}.{nameof(SignInCommand.AuthenticatedIdentity)}");
            var sessionInfo = await GetSessionInfoAsync(session, cancellationToken).ConfigureAwait(false);
            if (sessionInfo.IsSignOutForced)
                throw Errors.ForcedSignOut();

            await using var dbContext = await CreateCommandDbContextAsync(cancellationToken).ConfigureAwait(false);

            var dbUser = await Users
                .FindOrCreateOnSignInAsync(dbContext, user, cancellationToken)
                .ConfigureAwait(false);
            sessionInfo = sessionInfo with {
                LastSeenAt = Clock.Now,
                AuthenticatedIdentity = authenticatedIdentity,
                UserId = dbUser.Id.ToString(),
            };
            context.Items.Set(OperationItem.New(sessionInfo));
            await Sessions.CreateOrUpdateAsync(dbContext, sessionInfo, cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task SignOutAsync(
            SignOutCommand command, CancellationToken cancellationToken = default)
        {
            var (force, session) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                GetSessionInfoAsync(session, default).Ignore();
                var invSessionInfo = context.Items.Get<OperationItem<SessionInfo>>().Value;
                TryGetUserAsync(invSessionInfo.UserId, default).Ignore();
                GetUserSessionsAsync(invSessionInfo.UserId, default).Ignore();
                return;
            }

            await using var dbContext = await CreateCommandDbContextAsync(cancellationToken).ConfigureAwait(false);

            var dbSessionInfo = await Sessions.FindOrCreateAsync(dbContext, session, cancellationToken).ConfigureAwait(false);
            var sessionInfo = dbSessionInfo.ToModel();
            context.Items.Set(OperationItem.New(sessionInfo));
            sessionInfo = sessionInfo with {
                LastSeenAt = Clock.Now,
                AuthenticatedIdentity = "",
                UserId = "",
                IsSignOutForced = force,
            };
            await Sessions.CreateOrUpdateAsync(dbContext, sessionInfo, cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task<SessionInfo> SetupSessionAsync(
            SetupSessionCommand command, CancellationToken cancellationToken = default)
        {
            var (ipAddress, userAgent, session) = command;
            var context = CommandContext.GetCurrent();
            if (Computed.IsInvalidating()) {
                GetSessionInfoAsync(session, default).Ignore();
                var invSessionInfo = context.Items.Get<OperationItem<SessionInfo>>().Value;
                if (invSessionInfo.IsAuthenticated)
                    GetUserSessionsAsync(invSessionInfo.UserId, default).Ignore();
                return null!;
            }
            await using var dbContext = await CreateCommandDbContextAsync(cancellationToken).ConfigureAwait(false);

            var dbSessionInfo = await Sessions.FindAsync(dbContext, session.Id, cancellationToken).ConfigureAwait(false);
            var now = Clock.Now;
            var oldSessionInfo = dbSessionInfo?.ToModel() ?? new SessionInfo(session.Id, now);
            var newSessionInfo = oldSessionInfo with {
                LastSeenAt = now,
                IPAddress = string.IsNullOrEmpty(ipAddress) ? oldSessionInfo.IPAddress : ipAddress,
                UserAgent = string.IsNullOrEmpty(userAgent) ? oldSessionInfo.UserAgent : userAgent,
            };
            dbSessionInfo = await Sessions.CreateOrUpdateAsync(dbContext, newSessionInfo, cancellationToken).ConfigureAwait(false);
            var sessionInfo = dbSessionInfo.ToModel();
            context.Items.Set(OperationItem.New(sessionInfo));
            return sessionInfo;
        }

        public async Task UpdatePresenceAsync(
            Session session, CancellationToken cancellationToken = default)
        {
            var sessionInfo = await GetSessionInfoAsync(session, cancellationToken).ConfigureAwait(false);
            var now = Clock.Now.ToDateTime();
            var delta = now - sessionInfo.LastSeenAt;
            if (delta < MinUpdatePresencePeriod)
                return; // We don't want to update this too frequently
            var command = new SetupSessionCommand(session).MarkServerSide();
            await SetupSessionAsync(command, cancellationToken).ConfigureAwait(false);
        }

        // Compute methods

        public virtual async Task<bool> IsSignOutForcedAsync(
            Session session, CancellationToken cancellationToken = default)
        {
            var sessionInfo = await GetSessionInfoAsync(session, cancellationToken).ConfigureAwait(false);
            return sessionInfo.IsSignOutForced;
        }

        public virtual async Task<SessionInfo> GetSessionInfoAsync(
            Session session, CancellationToken cancellationToken = default)
        {
            await using var dbContext = CreateDbContext();
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var dbSession = await Sessions.FindAsync(dbContext, session.Id, cancellationToken).ConfigureAwait(false);
            if (dbSession == null)
                return new(session.Id, Clock.Now);
            return dbSession.ToModel();
        }

        public virtual async Task<User> GetUserAsync(
            Session session, CancellationToken cancellationToken = default)
        {
            var sessionInfo = await GetSessionInfoAsync(session, cancellationToken).ConfigureAwait(false);
            if (sessionInfo.IsSignOutForced || !sessionInfo.IsAuthenticated)
                return new User(session.Id);
            var user = await TryGetUserAsync(sessionInfo.UserId, cancellationToken).ConfigureAwait(false);
            return (user ?? new User(session.Id)).ToClientSideUser();
        }

        public virtual async Task<User?> TryGetUserAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            await using var dbContext = CreateDbContext();
            dbContext.EnableChangeTracking();
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var dbUser = await Users.FindAsync(dbContext, long.Parse(userId), cancellationToken).ConfigureAwait(false);
            if (dbUser == null)
                return null;
            await dbContext.Entry(dbUser).Collection(nameof(DbUser.Identities))
                .LoadAsync(cancellationToken).ConfigureAwait(false);
            return dbUser.ToModel();
        }

        public virtual async Task<SessionInfo[]> GetUserSessionsAsync(
            Session session, CancellationToken cancellationToken = default)
        {
            var user = await GetUserAsync(session, cancellationToken).ConfigureAwait(false);
            if (!user.IsAuthenticated)
                return Array.Empty<SessionInfo>();
            return await GetUserSessionsAsync(user.Id, cancellationToken).ConfigureAwait(false);
        }

        // Protected methods

        [ComputeMethod]
        protected virtual async Task<SessionInfo[]> GetUserSessionsAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            if (!long.TryParse(userId, out var longUserId))
                return Array.Empty<SessionInfo>();

            await using var dbContext = CreateDbContext();
            await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var dbSessions = await Sessions.ListByUserAsync(dbContext, longUserId, cancellationToken).ConfigureAwait(false);
            var sessions = new SessionInfo[dbSessions.Length];
            for (var i = 0; i < dbSessions.Length; i++)
                sessions[i] = dbSessions[i].ToModel();
            return sessions;
        }
    }
}
