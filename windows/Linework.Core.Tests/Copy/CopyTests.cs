using Linework.Core.Copy;
using Linework.Core.Plan;

namespace Linework.Core.Tests;

public class SeaNotesTests
{
    [Theory]
    [InlineData(60, "1 minute"), InlineData(1500, "25 minutes"), InlineData(3600, "1 hour"), InlineData(4200, "1 hour 10 minutes"), InlineData(7200, "2 hours")]
    public void TimeWording(int s, string e) => Assert.Equal(e, SeaNotes.TimeText(s));

    [Fact]
    public void UsualNeedsThree()
    {
        Assert.Null(SeaNotes.UsualSeconds([]));
        Assert.Null(SeaNotes.UsualSeconds([100, 200]));
        Assert.Equal(200, SeaNotes.UsualSeconds([100, 200, 300]));
    }

    [Fact]
    public void IntervalsInRange()
    {
        var rng = new Random(1);
        for (var i = 0; i < 1000; i++) Assert.InRange(SeaNotes.NextInterval(rng), 600, 1200);
    }

    [Fact]
    public void NoTimeNoteBefore20Min()
    {
        var rng = new Random(2);
        var state = new SeaNoteState();
        var general = SeaNotes.GeneralLines.Select(l => l.Replace("{name}", "Ana")).ToList();
        for (var i = 0; i < 200; i++) Assert.Contains(SeaNotes.Next(1199, "Ana", null, state, rng), general);
    }

    [Fact]
    public void TwentyMinuteNoteOnce()
    {
        var rng = new Random(3);
        var state = new SeaNoteState();
        Assert.Equal("You've been drawing for 25 minutes.", SeaNotes.Next(1500, "", null, state, rng));
        Assert.Contains(SeaNotes.Next(1500, "", null, state, rng), SeaNotes.GeneralLines);
    }

    [Fact]
    public void HourNote()
    {
        var rng = new Random(4);
        var state = new SeaNoteState();
        SeaNotes.Next(1500, "", null, state, rng);
        Assert.Equal("1 hour already. Maybe stretch for a second?", SeaNotes.Next(3600, "", null, state, rng));
    }

    [Fact]
    public void HourMarksTwentyShownToo()
    {
        var rng = new Random(5);
        var state = new SeaNoteState();
        Assert.Equal("1 hour already. Maybe stretch for a second?", SeaNotes.Next(3600, "", null, state, rng));
        Assert.Contains(SeaNotes.Next(3600, "", null, state, rng), SeaNotes.GeneralLines);
    }

    [Fact]
    public void UsualComesFirst()
    {
        var rng = new Random(6);
        var state = new SeaNoteState();
        Assert.Equal("You've been drawing for 25 minutes. That's longer than your usual practice.", SeaNotes.Next(1500, "", 1000, state, rng));
        Assert.Equal("1 hour already. Maybe stretch for a second?", SeaNotes.Next(3600, "", 1000, state, rng));
    }

    [Fact]
    public void UsualNeedsPassingAverage()
    {
        var rng = new Random(7);
        var state = new SeaNoteState();
        Assert.Contains(SeaNotes.Next(500, "", 1000, state, rng), SeaNotes.GeneralLines);
        for (var i = 0; i < 300; i++) Assert.DoesNotContain("usual practice", SeaNotes.Next(1500, "", null, state, rng));
    }

    [Fact]
    public void NameLineNeedsName()
    {
        var rng = new Random(8);
        var state = new SeaNoteState();
        for (var i = 0; i < 500; i++)
        {
            var l = SeaNotes.Next(500, "", null, state, rng);
            Assert.DoesNotContain("{name}", l);
            Assert.DoesNotContain("Still with you", l);
        }
        state = new SeaNoteState();
        var saw = false;
        for (var i = 0; i < 500; i++)
        {
            var l = SeaNotes.Next(500, "Ana", null, state, rng);
            Assert.DoesNotContain("{name}", l);
            saw |= l == "Still with you, Ana.";
        }
        Assert.True(saw);
    }

    [Theory]
    [InlineData(""), InlineData("Ana")]
    public void GeneralDoesNotRepeatWithinFive(string name)
    {
        var rng = new Random(9);
        var state = new SeaNoteState();
        var history = new List<string>();
        for (var i = 0; i < 500; i++) history.Add(SeaNotes.Next(500, name, null, state, rng));
        for (var i = 0; i < history.Count; i++)
            Assert.DoesNotContain(history[i], history.Skip(Math.Max(0, i - 5)).Take(Math.Min(i, 5)));
    }

    static Dictionary<int, string> Milestones(int poses)
    {
        var state = new PracticeNoteState();
        var d = new Dictionary<int, string>();
        for (var i = 0; i < poses; i++)
            if (SeaNotes.PracticeMilestone(i, poses, state) is { } line) d[i] = line;
        return d;
    }

    const string Began = "You just began, you can do it.", Last = "Last one. Make it count.", Three = "Only 3 left.";

    [Fact]
    public void PracticeMilestonesFollowTheSession() =>
        Assert.Equal(new Dictionary<int, string> { [0] = Began, [5] = "Halfway there. Keep going.", [7] = Three, [9] = Last }, Milestones(10));

    [Fact]
    public void ShortSessionsDontCrowd()
    {
        Assert.Equal(new Dictionary<int, string> { [0] = Began }, Milestones(1));
        Assert.Equal(new Dictionary<int, string> { [0] = Began, [1] = Last }, Milestones(2));
        Assert.Equal(new Dictionary<int, string> { [0] = Began, [2] = Last }, Milestones(3));
        Assert.Equal(new Dictionary<int, string> { [0] = Began, [3] = Three, [5] = Last }, Milestones(6));
    }

    [Fact]
    public void GoingBackDoesntRepeatAMilestone()
    {
        var state = new PracticeNoteState();
        Assert.Equal(Three, SeaNotes.PracticeMilestone(7, 10, state));
        Assert.Null(SeaNotes.PracticeMilestone(6, 10, state));
        Assert.Null(SeaNotes.PracticeMilestone(7, 10, state));
    }

    [Fact]
    public void PracticeTiming()
    {
        Assert.Equal(120, SeaNotes.PracticeFirstDelay);
        var rng = new Random(1);
        for (var i = 0; i < 50; i++) Assert.InRange(SeaNotes.NextPracticeInterval(rng), 300, 480);
    }

    [Fact]
    public void PracticeLinesDontRepeatWithinFive()
    {
        var state = new PracticeNoteState();
        var rng = new Random(1);
        var last = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var line = SeaNotes.PracticeLine("", state, rng);
            Assert.DoesNotContain(line, last);
            Assert.DoesNotContain("{name}", line);
            last.Add(line);
            if (last.Count > 5) last.RemoveAt(0);
        }
    }

    [Fact]
    public void PracticeLineUsesName()
    {
        var rng = new Random(1);
        var seen = new HashSet<string>();
        for (var i = 0; i < 60; i++) seen.Add(SeaNotes.PracticeLine("Ana", new PracticeNoteState(), rng));
        Assert.Contains("Still with you, Ana.", seen);
    }
}

public class LineworkCopyTests
{
    [Theory]
    [InlineData(5, "Good morning"), InlineData(11, "Good morning"), InlineData(12, "Good afternoon"), InlineData(18, "Good afternoon"),
     InlineData(19, "Good evening"), InlineData(4, "Good evening"), InlineData(0, "Good evening")]
    public void GreetingHours(int hour, string prefix) => Assert.StartsWith(prefix, LineworkCopy.Greeting(hour, ""));

    [Fact]
    public void GreetingName()
    {
        Assert.Equal("Good evening, Ana. Take care of s.e.a.", LineworkCopy.Greeting(20, "Ana"));
        Assert.Equal("Good evening. Take care of s.e.a.", LineworkCopy.Greeting(20, ""));
    }

    [Fact]
    public void FixedStrings()
    {
        Assert.Equal("A few minutes of practice a day keeps s.e.a. growing — and your hand steady.", LineworkCopy.UnderGreeting);
        Assert.Equal("Your canvas is empty. Drag your images here.", LineworkCopy.FreehandEmpty);
        Assert.Equal("Rest today. You've earned it.", LineworkCopy.RestDay);
        Assert.Equal("Drag your photos here", LineworkCopy.EmptyLibrary);
        Assert.Equal("Right-click a photo to make it your benchmark.", LineworkCopy.EmptyBenchmarks);
        Assert.Equal("That one was too short to count on your calendar.", LineworkCopy.SessionTooShort);
    }

    [Fact]
    public void Celebrations()
    {
        Assert.Equal("You finished today.", LineworkCopy.Celebrate(Celebration.Day));
        Assert.Equal("You finished this week.", LineworkCopy.Celebrate(Celebration.Week));
        Assert.Equal("You finished this book.", LineworkCopy.Celebrate(Celebration.Book));
        Assert.Equal("You finished that day.", LineworkCopy.Celebrate(Celebration.Day, false));
        Assert.Equal("You finished that week.", LineworkCopy.Celebrate(Celebration.Week, false));
        Assert.Equal("You finished that book.", LineworkCopy.Celebrate(Celebration.Book, false));
    }

    [Fact]
    public void Names()
    {
        Assert.Equal("Ana", LineworkCopy.NormalizedName("  Ana  "));
        Assert.Equal(40, LineworkCopy.NormalizedName(new string('a', 60)).Length);
        Assert.Equal("", LineworkCopy.NormalizedName("   \n "));
    }

    [Fact]
    public void Welcome()
    {
        Assert.True(LineworkCopy.ShouldShowWelcome("", false));
        Assert.False(LineworkCopy.ShouldShowWelcome("", true));
        Assert.False(LineworkCopy.ShouldShowWelcome("Ana", false));
    }

    [Theory]
    [InlineData(0.0, 1.0), InlineData(0.33, 0.67), InlineData(0.8, 0.2), InlineData(2.0, 0.2), InlineData(-1.0, 1.0)]
    public void Tint(double t, double e) => Assert.True(Math.Abs(Glass.TintOpacity(t) - e) < 1e-9);

    [Fact]
    public void Clock24()
    {
        Assert.Equal(new ClockParts("20:44", "13", null), ClockFormat.Parts(new TimeOnly(20, 44, 13), true));
        Assert.Equal(new ClockParts("00:05", "09", null), ClockFormat.Parts(new TimeOnly(0, 5, 9), true));
    }

    [Fact]
    public void Clock12()
    {
        Assert.Equal(new ClockParts("08:44", "13", "PM"), ClockFormat.Parts(new TimeOnly(20, 44, 13), false));
        Assert.Equal(new ClockParts("12:05", "09", "AM"), ClockFormat.Parts(new TimeOnly(0, 5, 9), false));
        Assert.Equal(new ClockParts("12:00", "00", "PM"), ClockFormat.Parts(new TimeOnly(12, 0, 0), false));
    }
}
