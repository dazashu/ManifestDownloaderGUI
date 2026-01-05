using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ManifestDownloaderGUI.Controls
{
    public class WatermarkTextBox : TextBox
    {
        public static readonly DependencyProperty WatermarkProperty =
            DependencyProperty.Register(nameof(Watermark), typeof(string), typeof(WatermarkTextBox),
                new PropertyMetadata(string.Empty));

        public string Watermark
        {
            get => (string)GetValue(WatermarkProperty);
            set => SetValue(WatermarkProperty, value);
        }

        static WatermarkTextBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(WatermarkTextBox),
                new FrameworkPropertyMetadata(typeof(WatermarkTextBox)));
        }
    }
}

