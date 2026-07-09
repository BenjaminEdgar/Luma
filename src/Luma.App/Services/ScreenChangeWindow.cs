using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Luma.App.Services;

public sealed class ScreenChangeWindow : Window
{
    public ScreenChangeWindow()
    {
        Title = "Screen changed";
        Width = 390;
        Height = 205;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        var keep = ActionButton("Keep this chat", "outline", false);
        var startNew = ActionButton("Start new chat", "accent", true);
        Content = new Border
        {
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(LumaTheme.FloatingCornerRadius),
            Background = LumaTheme.GlassFillBrush,
            BorderBrush = LumaTheme.BorderAccentBrush,
            BorderThickness = new Thickness(1),
            BoxShadow = LumaTheme.SoftShadow,
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Your screen looks different", FontSize = 19, FontWeight = FontWeight.Bold, Foreground = LumaTheme.TextBrightBrush },
                    new TextBlock
                    {
                        Text = "Do you want to start a new chat for what is on screen now?",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = .82,
                        Foreground = LumaTheme.TextMutedBrush,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 9,
                        Children = { keep, startNew }
                    }
                }
            }
        };
    }

    private Button ActionButton(string text, string style, bool result)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8), CornerRadius = new CornerRadius(9) };
        button.Classes.Add(style);
        button.Click += (_, _) => Close(result);
        return button;
    }
}
