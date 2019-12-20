using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Drawing;

namespace Image4Eagle
{
    using Brushes = System.Windows.Media.Brushes;
    
    /// <summary>
    /// Interakční logika pro MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ProcessedImage _img = null;

        private struct LayerItem
        {
            public string Label;
            public int Layer;
            public override string ToString() { return Label; }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Populate layer combo
            for (int i = 1; i < 255; i++)
                layer.Items.Add(new LayerItem { Label = ((Layers)i).ToString(), Layer = i });
            layer.SelectedIndex = (int)Layers.tStop - 1;
        }

        private void PreviewTextInput_CheckInteger(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
            (sender as TextBox).Background = e.Handled ? Brushes.LightCoral : Brushes.LightGreen;
        }

        private void load_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Image files (*.png, *.bmp, *.jpg, *.jpeg)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*";
            openFileDialog.Title = "Select input image file";
            openFileDialog.CheckFileExists = true;
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (var bmp = new Bitmap(openFileDialog.FileName))
                        _img = new ProcessedImage(bmp);
                    refresh_Click(sender, e);
                }
                catch(Exception ex)
                {
                    MessageBox.Show(ex.Message, "File opening failed!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }                
            }
        }

        private void refresh_Click(object sender, RoutedEventArgs e)
        {
            int layer;
            bool flipx, flipy, invert;
            int line, dim;

            if (image.Source != null)
            {
                var src = image.Source;
                image.Source = null;
                (src as IDisposable)?.Dispose();
            }

            try
            {
                layer = ((LayerItem)this.layer.SelectedItem).Layer;
                invert = negative.IsChecked == true;
                flipx = mirror_x.IsChecked == true;
                flipy = mirror_y.IsChecked == true;
                line = int.Parse(this.line.Text);
                dim = int.Parse(this.dimension.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invalid configuration!", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_img == null)
                return;

            _img.Generate(flipx, flipy, line, dim, !invert);
            image.Source = _img.GeneratePreview();
        }

        private void save_Click(object sender, RoutedEventArgs e)
        {
            int layer;

            refresh_Click(sender, e);
            if (_img == null)
                return;

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Eagle script (*.scr)|*.scr|All files (*.*)|*.*";
            saveFileDialog.Title = "Select output script file";
            saveFileDialog.DefaultExt = "scr";
            saveFileDialog.AddExtension = true;
            saveFileDialog.CheckPathExists = true;
            saveFileDialog.OverwritePrompt = true;
            saveFileDialog.DereferenceLinks = true;
            saveFileDialog.ValidateNames = true;
            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    layer = ((LayerItem)this.layer.SelectedItem).Layer;
                    using (var scr = (TextWriter)new StreamWriter(saveFileDialog.FileName))
                        _img.GenerateEagleScript(scr, layer);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "File opening failed!", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }
    }
}
