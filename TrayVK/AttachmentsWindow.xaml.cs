using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static TrayVK.MainWindow;

namespace TrayVK
{
    public partial class AttachmentsWindow : Window
    {
        ObservableCollection<CustomFileInfo> files;

        public AttachmentsWindow(ObservableCollection<CustomFileInfo> files)
        {
            InitializeComponent();
            this.files = files;

            if (list.Items != null && list.Items.Count > 0) list.Items.Clear();
            list.ItemsSource = files;

            UpdateTitle();
            files.CollectionChanged += (s, e) => UpdateTitle();

            void UpdateTitle() =>
                title.Text = $"Вложения к сообщению  ·  {files.Count} файлов";
        }

        protected override void OnContentRendered(EventArgs e)
        {
            UpdateLayout();
            Width = ActualWidth;
            SizeToContent = SizeToContent.Height;
        }

        void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            list.SelectedItem = null;

        void Border_MouseLeftButtonUp_1(object sender, MouseButtonEventArgs e)
        {
            files.Remove((sender as FrameworkElement).DataContext as CustomFileInfo);
            if (files.Count == 0) Border_MouseLeftButtonUp(null, null);
        }

        void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CustomFileInfo file = (sender as FrameworkElement).DataContext as CustomFileInfo;

            if (file != null && file.Bytes == null)
                System.Diagnostics.Process.Start("explorer", "/select, \"" + file.FileName + "\"");
        }

        void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            PowerEase function = new PowerEase { Power = 5, EasingMode = EasingMode.EaseIn };
            TimeSpan duration = TimeSpan.FromSeconds(0.35);

            DoubleAnimation animation1 = new DoubleAnimation
            {
                From = 1,
                To = 0.65,
                Duration = duration,
                EasingFunction = function
            };

            DoubleAnimation animation2 = new DoubleAnimation
            {
                From = 1,
                To = 0.65,
                Duration = duration,
                EasingFunction = function
            };

            DoubleAnimation animation3 = new DoubleAnimation
            {
                From = 1,
                To = 0.3,
                Duration = duration,
                EasingFunction = function
            };

            animation1.Completed += (s, a) => Close();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation1);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation2);
            BeginAnimation(OpacityProperty, animation3);
        }

        void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Border_MouseLeftButtonUp(null, null);
            files.Clear();
        }
    }
}
