using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Overseer.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly Overseer.Services.Tools.SignalRClientToolBridge _clientToolBridge;

        public ChatHub(ApplicationDbContext dbContext, Overseer.Services.Tools.SignalRClientToolBridge clientToolBridge)
        {
            _dbContext = dbContext;
            _clientToolBridge = clientToolBridge;
        }

        public async Task JoinSession(long sessionId)
        {
            var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId && s.AspNetUserId == userId);
                if (session != null)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
                }
            }
        }

        public async Task LeaveSession(long sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId.ToString());
        }

        public async Task SubmitToolResult(string requestId, long sessionId, bool success, string content)
        {
            var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId && s.AspNetUserId == userId);
                if (session != null)
                {
                    _clientToolBridge.SubmitToolResult(requestId, success, content);
                }
            }
        }

        public async Task CancelTitleGeneration(long sessionId)
        {
            var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId && s.AspNetUserId == userId);
                if (session != null)
                {
                    Overseer.Services.ChatService.CancelTitleGeneration(sessionId);
                }
            }
        }

        public async Task CancelGeneration(long sessionId)
        {
            var userId = Context.UserIdentifier ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId != null)
            {
                var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == sessionId && s.AspNetUserId == userId);
                if (session != null)
                {
                    var manager = Context.GetHttpContext()?.RequestServices.GetService(typeof(Overseer.Services.OngoingChatManager)) as Overseer.Services.OngoingChatManager;
                    manager?.TryCancelAndRemove(sessionId);
                }
            }
        }
    }
}
