using System;

namespace Tillroll.Emulator;

/// <summary>
/// Recognises payloads made only of DLE real-time commands, so a sender that is waiting for another
/// sender's job to finish still gets its status answered at once (real printers answer real-time
/// commands even while busy). Sequences match how <see cref="EscPos.EscPosInterpreter"/> reads them:
/// <c>DLE EOT n</c>, <c>DLE ENQ</c> and <c>DLE DC4 1 m t</c>.
/// </summary>
internal static class RealtimeQuery
{
    private const char Dle = (char)0x10;
    private const char Eot = (char)0x04;
    private const char Enq = (char)0x05;
    private const char Dc4 = (char)0x14;
    private const char GeneratePulse = (char)0x01;

    /// <summary>True when <paramref name="payload"/> is one or more complete real-time commands and nothing else.</summary>
    public static bool IsRealtimeOnly(ReadOnlySpan<char> payload)
    {
        if (payload.IsEmpty)
            return false;

        int i = 0;
        while (i < payload.Length)
        {
            int length = SequenceLength(payload[i..]);
            if (length == 0)
                return false;
            i += length;
        }
        return true;
    }

    /// <summary>Answers every real-time command in a payload accepted by <see cref="IsRealtimeOnly"/>.</summary>
    public static void Dispatch(ReceiptPrinter printer, ReadOnlySpan<char> payload)
    {
        int i = 0;
        while (i < payload.Length)
        {
            var sequence = payload[i..];
            int length = SequenceLength(sequence);
            if (length == 0)
                return;

            switch (sequence[1])
            {
                case Eot: printer.TransmitRealtimeStatus(sequence[2]); break;
                case Enq: printer.RealtimeRecover(); break;
                case Dc4: printer.KickCashDrawer(sequence[3]); break;
            }
            i += length;
        }
    }

    /// <summary>Length of the complete real-time command at the start of <paramref name="s"/>, or 0.</summary>
    private static int SequenceLength(ReadOnlySpan<char> s)
    {
        if (s.Length < 2 || s[0] != Dle)
            return 0;

        return s[1] switch
        {
            Eot when s.Length >= 3 => 3,
            Enq => 2,
            Dc4 when s.Length >= 5 && s[2] == GeneratePulse => 5,
            _ => 0,
        };
    }
}
