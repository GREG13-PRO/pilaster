using System;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Pilaster.Setup.Views.Pages;

public partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => PlayEntrance();
    }

    private void PlayEntrance()
    {
        var lockupFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
        var lockupScale = new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(400))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        LockupImage.BeginAnimation(OpacityProperty, lockupFade);
        LockupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, lockupScale);
        LockupScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, lockupScale);

        var taglineFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(150),
        };
        var taglineSlide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        TaglineText.BeginAnimation(OpacityProperty, taglineFade);
        TaglineOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, taglineSlide);
    }
}
