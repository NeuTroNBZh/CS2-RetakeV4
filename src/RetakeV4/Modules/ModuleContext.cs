using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using RetakeV4.Domain.Events;
using RetakeV4.Domain.Modules;
using RetakeV4.Domain.Rounds;
using RetakeV4.Localization;

namespace RetakeV4.Modules;

public sealed record ModuleContext(
    BasePlugin Plugin,
    IEventBus Bus,
    ModuleGuard Guard,
    ITextService Text,
    ILogger Logger,
    RoundTracker Rounds,
    ModuleHooks Hooks);
