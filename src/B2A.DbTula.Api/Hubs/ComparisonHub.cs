using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace B2A.DbTula.Api.Hubs;

[Authorize]
public class ComparisonHub : Hub
{
    public async Task JoinRun(string runId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"run-{runId}");

    public async Task LeaveRun(string runId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run-{runId}");
}
