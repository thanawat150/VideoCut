using AutoCutStudio.Core.Interfaces;
using AutoCutStudio.Core.Models;

namespace AutoCutStudio.Core.Services;

public sealed class AgentEventBus : IAgentEventBus
{
    public event EventHandler<AgentEvent>? Published;

    public void Publish(AgentEvent agentEvent)
    {
        ArgumentNullException.ThrowIfNull(agentEvent);
        Published?.Invoke(this, agentEvent);
    }
}
