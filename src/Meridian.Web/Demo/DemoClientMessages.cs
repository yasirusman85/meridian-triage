namespace Meridian.Web.Demo;

/// <summary>
/// The six messages Dawn forwarded (docs/input/client-input.md, section 2.2), in their original order, with the
/// weekday and time each one shows (Europe/London). The texts are the recorded stub answers' texts byte for byte, so
/// the stub answers them without a key. Senders are the masked addresses as given.
/// </summary>
public static class DemoClientMessages
{
    public static readonly IReadOnlyList<DemoMessage> All = new[]
    {
        new DemoMessage("j.okafor@example.com", "Hi there the tap in the bathroom has been dripping for about two weeks now, it's not urgent but it's quite annoying at night. Also while I'm emailing, the light in the hallway cupboard doesn't work, I think it's just the bulb but I'm not allowed to change it myself am I. Flat 4, 22 Pemberton Road. Thanks, Joy",
            DayOfWeek.Tuesday, new TimeOnly(10, 42)),
        new DemoMessage("m.walsh@example.com", "URGENT URGENT no hot water and no heating at all since this morning, I have a baby in the house, this is unacceptable, I have called three times. 14b Alder Court. Please someone call me TONIGHT.",
            DayOfWeek.Friday, new TimeOnly(19, 15)),
        new DemoMessage("m.walsh@example.com", "Still nothing. Michael Walsh, 14b Alder Court.",
            DayOfWeek.Friday, new TimeOnly(19, 51)),
        new DemoMessage("s.walsh@example.com", "My husband has emailed twice about the heating at 14b Alder Court, no one has responded. We have a 7 month old baby. What is going on.",
            DayOfWeek.Friday, new TimeOnly(20, 30)),
        new DemoMessage("a.nowak@example.com", "Dzień dobry, w mieszkaniu jest pleśń na ścianie w sypialni, coraz gorzej, dziecko ma astmę. Mieszkanie 9, Beechwood House. Dziękuję.",
            DayOfWeek.Wednesday, new TimeOnly(8, 3)),
        new DemoMessage("r.patel@example.com", "The boiler is making a loud banging noise when it comes on. It's still working and we've got hot water. Didn't do this before. 8 Carlton Street.",
            DayOfWeek.Monday, new TimeOnly(14, 20)),
    };

    /// <summary>A message to submit: sender, text, and the weekday and local time it shows.</summary>
    public sealed record DemoMessage(string Sender, string Text, DayOfWeek Day, TimeOnly LocalTime);
}
