using MyMetronom.Services;
using MyMetronom.Utils;

namespace MyMetronom
{
    public partial class MainPage : ContentPage
    {
        private readonly IMetronomeService _metronome;
        private bool _flash;

        public MainPage()
        {
            InitializeComponent();
            _metronome = ServiceHelper.GetRequiredService<IMetronomeService>();

            BpmSlider.Value = _metronome.Bpm;
            UpdateBpmLabel(_metronome.Bpm);

            _metronome.Tick += OnTick;
        }

        private void OnBpmChanged(object sender, ValueChangedEventArgs e)
        {
            var bpm = (int)Math.Round(e.NewValue);
            _metronome.SetBpm(bpm);
            UpdateBpmLabel(bpm);
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
                TickIndicator.Opacity = 0.25;
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

        private void OnTick(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _flash = !_flash;
                TickIndicator.Opacity = _flash ? 1.0 : 0.5;
            });
        }
    }
}
