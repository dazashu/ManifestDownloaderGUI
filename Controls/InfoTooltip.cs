using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ManifestDownloaderGUI.Controls
{
    public class InfoTooltip : TextBlock
    {
        public static readonly DependencyProperty TooltipTextProperty =
            DependencyProperty.Register(nameof(TooltipText), typeof(string), typeof(InfoTooltip),
                new PropertyMetadata(string.Empty, OnTooltipTextChanged));

        public string TooltipText
        {
            get => (string)GetValue(TooltipTextProperty);
            set => SetValue(TooltipTextProperty, value);
        }

        public InfoTooltip()
        {
            Text = "ℹ";
            
            // Try to use theme brushes, fallback to hardcoded colors
            try
            {
                var primaryBrush = Application.Current.Resources["PrimaryBrush"] as SolidColorBrush;
                if (primaryBrush != null)
                {
                    Foreground = primaryBrush;
                }
                else
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 255));
                }
            }
            catch
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 255));
            }
            
            FontWeight = FontWeights.Bold;
            FontSize = 16;
            Cursor = Cursors.Hand;
            Margin = new Thickness(5, 0, 0, 0);
            VerticalAlignment = VerticalAlignment.Center;
            HorizontalAlignment = HorizontalAlignment.Left;
            Width = 20;
            Height = 20;
            TextAlignment = TextAlignment.Center;

            // Always set the tooltip
            Loaded += (s, e) => UpdateTooltip();
            
            MouseEnter += (s, e) => 
            {
                try
                {
                    var hoverBrush = Application.Current.Resources["PrimaryHoverBrush"] as SolidColorBrush;
                    if (hoverBrush != null)
                    {
                        Foreground = hoverBrush;
                    }
                    else
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 184, 230));
                    }
                }
                catch
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 184, 230));
                }
            };
            MouseLeave += (s, e) => 
            {
                try
                {
                    var primaryBrush = Application.Current.Resources["PrimaryBrush"] as SolidColorBrush;
                    if (primaryBrush != null)
                    {
                        Foreground = primaryBrush;
                    }
                    else
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 255));
                    }
                }
                catch
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 212, 255));
                }
            };
        }

        private void UpdateTooltip()
        {
            if (!string.IsNullOrEmpty(TooltipText))
            {
                ToolTip = TooltipText;
            }
        }

        private static void OnTooltipTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is InfoTooltip tooltip)
            {
                var newText = e.NewValue?.ToString() ?? string.Empty;
                tooltip.ToolTip = newText; // Always set the tooltip
            }
        }
    }
}

