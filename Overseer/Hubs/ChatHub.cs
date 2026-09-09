using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Overseer.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly Overseer.Services.Tools.SignalRClientToolBridge _clientToolBridge;
        private readonly EphemeralSessionStore _ephemeralSessions;

        public ChatHub(
            ApplicationDbContext dbContext,
            Overseer.Services.Tools.SignalRClientToolBridge clientToolBridge,
            EphemeralSessionStore ephemeralSessions)
        {
            _dbContext = dbContext;
            _clientToolBridge = clientToolBridge;
            _ephemeralSessions = ephemeralSessions;
        }

        /// <summary>
        /// Whether the caller owns the session this reference names, resolved against whichever
        /// store holds it.
        /// </summary>
        /// <remarks>
        /// Every authorising method used to ask this question the same way — a
        /// <c>ChatSession</c> row lookup — and an ephemeral session has no such row <em>by
        /// construction</em>. The lookup would return null, the caller would never be added to
        /// the group, and the client would receive no streamed tokens, no tool events and no
        /// completion: a hung model rather than a refused connection. Routing both cases
        /// through one helper is what keeps a new hub method from reintroducing that.
        /// </remarks>
        private async Task<bool> IsOwnedByCallerAsync(SessionRef sessionRef)
        {
            var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return false;

            if (sessionRef.IsEphemeral)
            {
                return _ephemeralSessions.IsOwnedBy(sessionRef, userId);
            }

            if (!sessionRef.IsPersistent) return false;

            return await _dbContext.ChatSession
                .AnyAsync(s => s.Id == sessionRef.PersistentId && s.AspNetUserId == userId);
        }

        public async Task JoinSession(string sessionRef)
        {
            if (!SessionRef.TryParse(sessionRef, out var parsed)) return;
            if (!await IsOwnedByCallerAsync(parsed)) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, parsed.GroupName);
        }

        /// <summary>
        /// Removes this connection from a session's group.
        /// </summary>
        /// <remarks>
        /// The one hub method that authorises nothing, and deliberately so: it reads no user id
        /// and looks up no session, because removing your own connection from a group you may
        /// not even be in is not an operation anyone needs permission for. What it does need is
        /// the parse, so the group name matches the one <see cref="JoinSession"/> added — an
        /// ephemeral connection left in its group would keep receiving another turn's events.
        /// </remarks>
        public async Task LeaveSession(string sessionRef)
        {
            if (!SessionRef.TryParse(sessionRef, out var parsed)) return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, parsed.GroupName);
        }

        public async Task SubmitToolResult(string requestId, string sessionRef, bool success, string content)
        {
            if (!SessionRef.TryParse(sessionRef, out var parsed)) return;
            if (!await IsOwnedByCallerAsync(parsed)) return;

            _clientToolBridge.SubmitToolResult(requestId, success, content);
        }

        public async Task CancelTitleGeneration(string sessionRef)
        {
            if (!SessionRef.TryParse(sessionRef, out var parsed)) return;
            if (!await IsOwnedByCallerAsync(parsed)) return;

            Overseer.Services.ChatService.CancelTitleGeneration(parsed);
        }

        public async Task CancelGeneration(string sessionRef)
        {
            if (!SessionRef.TryParse(sessionRef, out var parsed)) return;
            if (!await IsOwnedByCallerAsync(parsed)) return;

            var manager = Context.GetHttpContext()?.RequestServices.GetService(typeof(Overseer.Services.OngoingChatManager)) as Overseer.Services.OngoingChatManager;
            manager?.TryCancelAndRemove(parsed);
        }
    }
}
