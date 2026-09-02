using Microsoft.JSInterop;

namespace Simplz.Babytracker.Services;

/// <summary>
/// A round trip the browser can make over its own SignalR circuit, to find out whether that
/// circuit is still alive. A page whose circuit has quietly died looks completely normal but
/// does nothing when tapped; this is how it finds out. See wwwroot/circuit-watchdog.js.
/// </summary>
public static class CircuitPing
{
    [JSInvokable]
    public static bool Ping() => true;
}
