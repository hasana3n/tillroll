using Tillroll.Emulator;
using Tillroll.Emulator.Enums;

namespace Tillroll.Controls;

/// <summary>Whether the simulated printer would print right now.</summary>
public enum PrinterHealth
{
    /// <summary>Prints and answers normally.</summary>
    Ready,

    /// <summary>Prints, but something needs attention soon (paper near its end).</summary>
    Warning,

    /// <summary>Drops prints, the way real hardware does in this state.</summary>
    Blocked,
}

/// <summary>Sums up a <see cref="PrinterState"/> in a few words for a status line.</summary>
public static class PrinterReadiness
{
    /// <summary>
    /// The printer's health and one phrase naming why. When several things block printing, the phrase names the
    /// first one in the order a person would fix them: power (online), cover, paper, then errors.
    /// </summary>
    public static (PrinterHealth Health, string Text) Describe(PrinterState state)
    {
        if (!state.Online)
            return (PrinterHealth.Blocked, "Offline");
        if (state.CoverOpen)
            return (PrinterHealth.Blocked, "Cover open");
        if (state.Paper == PaperLevel.Out)
            return (PrinterHealth.Blocked, "Out of paper");
        if (state.Error == PrinterErrorState.Unrecoverable)
            return (PrinterHealth.Blocked, "Unrecoverable error");
        if (state.Error == PrinterErrorState.Recoverable)
            return (PrinterHealth.Blocked, "Recoverable error");
        if (state.Paper == PaperLevel.NearEnd)
            return (PrinterHealth.Warning, "Ready · paper near end");
        return (PrinterHealth.Ready, "Ready");
    }
}
