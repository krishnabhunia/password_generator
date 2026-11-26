using Avalonia.Controls;
using PasswordGenerator.App.Services;

namespace PasswordGenerator.App.Views;

public partial class CompactView : UserControl
{
    /// <summary>The password's font size at the design's smallest width; it grows with the window.</summary>
    public const double PasswordFontSize = 18;

    public CompactView()
    {
        InitializeComponent();
        PasswordText.Follow(this, PwdList, PasswordFontSize, DesignInfo.CompactWidth);
    }
}
