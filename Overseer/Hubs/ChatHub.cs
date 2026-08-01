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

        public ChatHub(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
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

        public Task SubmitToolResult(string requestId, long sessionId, bool success, string content)
        {
            // Resolve the client tool bridge from the service provider
            var clientToolBridge = (Overseer.Services.Tools.SignalRClientToolBridge)Context.GetHttpContext()!.RequestServices.GetService(typeof(Overseer.Services.Tools.SignalRClientToolBridge))!;
            if (clientToolBridge != null)
            {
                clientToolBridge.SubmitToolResult(requestId, success, content);
            }
            return Task.CompletedTask;
        }
    }
}
