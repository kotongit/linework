using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Linework.App.Platform;
using Linework.App.Theme;
using Linework.Core.Copy;
using Linework.Core.Plan;

namespace Linework.App.Views;

/// <summary>Confetti and a banner when a day, week or book is finished. With Windows' animation effects off, the banner only.</summary>
public sealed class CelebrationView : Grid
{
    const double Duration = 2.5;

    public CelebrationView(Celebration kind, bool isToday, bool sound, Action onDone, double? frozenAt = null)
    {
        IsHitTestVisible = false;
        if (SystemParameters.ClientAreaAnimation || frozenAt != null) Children.Add(new Confetti(kind, frozenAt));
        Children.Add(new Border
        {
            Background = Tokens.Ink,
            CornerRadius = new CornerRadius(32),
            Padding = new Thickness(32, 16, 32, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Text(LineworkCopy.Celebrate(kind, isToday), 26, Ui.Medium, Tokens.Ground),
        });
        if (frozenAt != null) return;
        if (sound) SoundCue.Celebrate();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Duration) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            onDone();
        };
        timer.Start();
    }

    sealed class Confetti : FrameworkElement
    {
        readonly int _count;
        readonly double? _frozenAt;
        readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        static readonly Brush Mid = Freeze(new SolidColorBrush(Color.FromRgb(0x5E, 0xC9, 0xBF))), Deep = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x6E, 0x66)));

        static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        public Confetti(Celebration kind, double? frozenAt)
        {
            _frozenAt = frozenAt;
            _count = kind switch { Celebration.Day => 60, Celebration.Week => 140, _ => 260 };
            if (frozenAt != null) return;
            void OnFrame(object? s, EventArgs e) => InvalidateVisual();
            Loaded += (_, _) => CompositionTarget.Rendering += OnFrame;
            Unloaded += (_, _) => CompositionTarget.Rendering -= OnFrame;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var elapsed = _frozenAt ?? _clock.Elapsed.TotalSeconds;
            Brush[] palette = [Tokens.Teal, Mid, Deep, Tokens.Ink];
            for (long i = 0; i < _count; i++)
            {
                // Stable per-particle values, so each one follows a smooth path.
                double a = i * 2654435761L % 1000 / 1000.0, b = i * 40503L % 1000 / 1000.0;
                var x = ActualWidth * a + Math.Sin(elapsed * 3 + b * 6) * 24;
                var y = -30 + (ActualHeight + 60) * Math.Min(elapsed / Duration * (0.6 + b), 1.2) - b * 120;
                var side = 5 + b * 7;
                dc.PushTransform(new TranslateTransform(x, y));
                dc.PushTransform(new RotateTransform(elapsed * 5 * (a - 0.5) * 4 * 180 / Math.PI));
                dc.DrawRectangle(palette[i % palette.Length], null, new Rect(-side / 2, -side / 2, side, side * 0.6));
                dc.Pop();
                dc.Pop();
            }
        }
    }
}
