using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace Overseer.Hubs
{
    public class ChatHub : Hub
    {
        public async Task JoinSession(long sessionId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId.ToString());
        }

        public async Task LeaveSession(long sessionId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId.ToString());
        }
    }
}
