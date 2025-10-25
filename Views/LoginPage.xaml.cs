using Microsoft.Maui.ApplicationModel;

namespace OrbanaDrive.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(OrbanaDrive.ViewModels.LoginVM vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // estado inicial para animación tipo sheet
        Sheet.TranslationY = 80;
        Sheet.Opacity = 0;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            LblBuild.Text = $"{AppInfo.Name} v{AppInfo.VersionString} ({AppInfo.BuildString})";
        }
        catch { LblBuild.Text = " "; }
        void Resize()
        {
            if (Height <= 0) return;
            var target = Math.Min(520, Height * 0.42);
            Sheet.HeightRequest = target;
        }

        Resize();
        SizeChanged += (_, __) => Resize();
        // animación suave
        await Task.WhenAll(
            Sheet.TranslateTo(0, 0, 220, Easing.CubicOut),
            Sheet.FadeTo(1, 220, Easing.CubicOut)
        );
    }
}
