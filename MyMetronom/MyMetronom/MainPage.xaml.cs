using MyMetronom.Services;
using MyMetronom.Utils;

namespace MyMetronom
{
    public partial class MainPage : ContentPage
    {
        private const string PrefBpm = "pref_bpm";
        private const string PrefSubdivision = "pref_subdivision";
        private const string PrefYoutube = "pref_youtube";

        private readonly IMetronomeService _metronome;
        private bool _flash;

        public MainPage()
        {
            InitializeComponent();
            _metronome = ServiceHelper.GetRequiredService<IMetronomeService>();

            // restore
            var bpm = Preferences.Get(PrefBpm, _metronome.Bpm);
            _metronome.SetBpm(bpm);
            BpmSlider.Value = bpm;
            UpdateBpmLabel(bpm);

            var subIndex = Preferences.Get(PrefSubdivision, 0);
            SetSubdivisionByIndex(subIndex);

            var yt = Preferences.Get(PrefYoutube, string.Empty);
            if (!string.IsNullOrEmpty(yt))
                YoutubeUrlEntry.Text = yt;

            _metronome.Tick += OnTick;
        }

        private void SetSubdivisionByIndex(int index)
        {
            index = Math.Clamp(index, 0, 3);
            QuarterRadio.IsChecked = index == 0;
            EighthRadio.IsChecked = index == 1;
            TripletRadio.IsChecked = index == 2;
            SixteenthRadio.IsChecked = index == 3;

            var s = index switch
            {
                0 => Subdivision.Quarter,
                1 => Subdivision.Eighth,
                2 => Subdivision.Triplet,
                3 => Subdivision.Sixteenth,
                _ => Subdivision.Quarter
            };
            _metronome.SetSubdivision(s);
            Preferences.Set(PrefSubdivision, index);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            Dispatcher.Dispatch(() =>
            {
                this.ForceLayout();
                this.InvalidateMeasure();
                var prev = TickIndicator.Opacity;
                TickIndicator.Opacity = prev + 0.001;
                TickIndicator.Opacity = prev;
            });
        }

        private void OnBpmChanged(object sender, ValueChangedEventArgs e)
        {
            var bpm = (int)Math.Round(e.NewValue);
            _metronome.SetBpm(bpm);
            UpdateBpmLabel(bpm);
            Preferences.Set(PrefBpm, bpm);
        }

        private void UpdateBpmLabel(int bpm)
        {
            BpmLabel.Text = $"{bpm} BPM";
        }

        private void OnStartStopClicked(object sender, EventArgs e)
        {
            if (_metronome.IsRunning)
            {
                _metronome.Stop();
                StartStopButton.Text = "Start";
                TickIndicator.Opacity = 0.29;
            }
            else
            {
                var bpm = (int)Math.Round(BpmSlider.Value);
                _metronome.Start(bpm);
                StartStopButton.Text = "Stop";
            }
        }

        private void OnBpmUpClicked(object sender, EventArgs e)
        {
            var bpm = Math.Min(300, (int)Math.Round(BpmSlider.Value) + 1);
            BpmSlider.Value = bpm;
        }

        private void OnBpmDownClicked(object sender, EventArgs e)
        {
            var bpm = Math.Max(30, (int)Math.Round(BpmSlider.Value) - 1);
            BpmSlider.Value = bpm;
        }

        private void OnSubdivisionRadioCheckedChanged(object? sender, CheckedChangedEventArgs e)
        {
            if (sender is not RadioButton rb || !e.Value)
                return;

            int index = rb == QuarterRadio ? 0 : rb == EighthRadio ? 1 : rb == TripletRadio ? 2 : 3;
            SetSubdivisionByIndex(index);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _flash = !_flash;
                TickIndicator.Opacity = _flash ? 0.24 : 0.34;
            });
        }

        private void OnYoutubeTextChanged(object? sender, TextChangedEventArgs e)
        {
            Preferences.Set(PrefYoutube, e.NewTextValue ?? string.Empty);
        }

        private void OnLoadYoutubeClicked(object? sender, EventArgs e)
        {
            var input = YoutubeUrlEntry.Text?.Trim();
            if (string.IsNullOrEmpty(input))
                return;

            var videoId = ExtractYouTubeVideoId(input);
            if (string.IsNullOrEmpty(videoId))
            {
                YoutubeWebView.Source = null;
                YoutubeWebView.IsVisible = false;
                return;
            }

            var embedUrl = $"https://www.youtube.com/embed/{videoId}?rel=0&modestbranding=1&playsinline=1";
            var html = $@"<html>
<head><meta name='viewport' content='width=device-width, initial-scale=1'></head>
<body style='margin:0;padding:0;background-color:black;'>
<iframe width='100%' height='100%' src='{embedUrl}' frameborder='0'
allow='accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share'
allowfullscreen></iframe>
</body>
</html>";

            YoutubeWebView.Source = new HtmlWebViewSource { Html = html };
            YoutubeWebView.IsVisible = true;
        }

        private static string? ExtractYouTubeVideoId(string input)
        {
            if (input.Length >= 10 && input.Length <= 20 && !input.Contains(' ') && !input.Contains('/'))
                return input;

            if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            {
                if (uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
                {
                    var q = uri.Query;
                    if (!string.IsNullOrEmpty(q))
                    {
                        var pairs = q.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var p in pairs)
                        {
                            var kv = p.Split('=', 2);
                            if (kv.Length == 2 && kv[0].Equals("v", StringComparison.OrdinalIgnoreCase))
                                return Uri.UnescapeDataString(kv[1]);
                        }
                    }

                    var segs = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segs.Length >= 2 && (segs[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
                                             segs[0].Equals("shorts", StringComparison.OrdinalIgnoreCase)))
                        return segs[1];
                }

                if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
                {
                    var id = uri.AbsolutePath.Trim('/');
                    if (!string.IsNullOrWhiteSpace(id))
                        return id;
                }
            }

            return null;
        }
    }
}
