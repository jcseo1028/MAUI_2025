using CommunityToolkit.Maui.Views;

namespace MyMetronom.Pages;

public partial class SettingsPopup : Popup
{
    private readonly Action<bool>? _onFlashChanged;
    private bool _suppressNotify;

    public SettingsPopup(bool initialFlashEnabled = true, Action<bool>? onFlashChanged = null)
    {
        InitializeComponent();
        _onFlashChanged = onFlashChanged;

        // Initialize switch without firing callback
        _suppressNotify = true;
        FlashSwitch.IsToggled = initialFlashEnabled;
        _suppressNotify = false;
    }

    private void OnFlashToggled(object? sender, ToggledEventArgs e)
    {
        if (_suppressNotify) return;
        _onFlashChanged?.Invoke(e.Value);
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        this.CloseAsync();
    }
}
