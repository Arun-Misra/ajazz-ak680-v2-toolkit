using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AK680V2Toolkit;

public partial class KeyboardView : UserControl
{
    // label, width in 0.25u units
    private static readonly (string label, int units)[][] Rows =
    {
        new[]{("Esc",4),("1 !",4),("2 @",4),("3 #",4),("4 $",4),("5 %",4),("6 ^",4),("7 &",4),("8 *",4),("9 (",4),("0 )",4),("ˉ -",4),("= +",4),("Backspace",8),("Home",4)},
        new[]{("Tab",6),("Q",4),("W",4),("E",4),("R",4),("T",4),("Y",4),("U",4),("I",4),("O",4),("P",4),("[ {",4),("] }",4),("\\ |",4),("Del",4)},
        new[]{("Caps",7),("A",4),("S",4),("D",4),("F",4),("G",4),("H",4),("J",4),("K",4),("L",4),("; :",4),("' \"",4),("Enter",9),("PgUp",4)},
        new[]{("L-Shift",9),("Z",4),("X",4),("C",4),("V",4),("B",4),("N",4),("M",4),(", <",4),(". >",4),("/ ?",4),("R-Shift",7),("↑",4),("PgDn",4)},
        new[]{("L-Ctrl",5),("L-Win",5),("L-Alt",5),("Spacebar",25),("R-Alt",5),("R-Ctrl",5),("Fn",4),("←",4),("↓",4),("→",4)}
    };

    public static readonly DependencyProperty ShowValuesProperty =
        DependencyProperty.Register(nameof(ShowValues), typeof(bool), typeof(KeyboardView),
            new PropertyMetadata(false, (d, _) => ((KeyboardView)d).Rebuild()));
    public bool ShowValues { get => (bool)GetValue(ShowValuesProperty); set => SetValue(ShowValuesProperty, value); }

    public event EventHandler<string>? KeyClicked;

    public KeyboardView() { InitializeComponent(); Loaded += (_, _) => Rebuild(); }

    private void Rebuild()
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.ColumnDefinitions.Clear();
        const int totalUnits = 66;
        for (int u = 0; u < totalUnits; u++) Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < Rows.Length; r++) Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int r = 0; r < Rows.Length; r++)
        {
            int col = 0;
            foreach (var (label, units) in Rows[r])
            {
                var btn = new Button
                {
                    Content = ShowValues ? $"1.5{Environment.NewLine}{label}" : label,
                    Margin = new Thickness(2),
                    FontSize = 11,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E7DCD3")),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    Tag = label
                };
                btn.Click += (s, _) => KeyClicked?.Invoke(this, (string)((Button)s!).Tag);
                Grid.SetRow(btn, r);
                Grid.SetColumn(btn, col);
                Grid.SetColumnSpan(btn, units);
                Root.Children.Add(btn);
                col += units;
            }
        }
    }
}
