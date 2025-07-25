﻿// Copyright © 2017-2025 QL-Win Contributors
//
// This file is part of QuickLook program.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using Microsoft.Win32;
using QuickLook.Common.Annotations;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickLook.Plugin.PlantUMLViewer;

public partial class ImagePanel : UserControl, INotifyPropertyChanged, IDisposable
{
    private Visibility _backgroundVisibility = Visibility.Visible;
    private ContextObject _contextObject = null!;
    private Point? _dragInitPos = null!;
    private Uri _imageSource = null!;
    private bool _isZoomFactorFirstSet = true;
    private DateTime _lastZoomTime = DateTime.MinValue;
    private double _maxZoomFactor = 3d;
    private MetaProvider _meta = null!;
    private Visibility _metaIconVisibility = Visibility.Collapsed;
    private double _minZoomFactor = 0.1d;
    private BitmapScalingMode _renderMode = BitmapScalingMode.Linear;
    private bool _showZoomLevelInfo = true;
    private BitmapSource _source = null!;
    private double _zoomFactor = 1d;

    private bool _zoomToFit = true;
    private double _zoomToFitFactor;
    private bool _zoomWithControlKey;

    private Visibility _copyVisibility = Visibility.Visible;
    private Visibility _saveAsVisibility = Visibility.Visible;
    private Visibility _reverseColorVisibility = Visibility.Visible;

    public ImagePanel()
    {
        InitializeComponent();

        Resources.MergedDictionaries.Clear();

        buttonCopy.Click += OnCopyOnClick;

        buttonSaveAs.Click += OnSaveAsOnClick;

        buttonReverseColor.Click += OnReverseColorOnClick;

        buttonMeta.Click += (sender, e) =>
            textMeta.Visibility = textMeta.Visibility == Visibility.Collapsed
                ? Visibility.Visible
                : Visibility.Collapsed;

        buttonBackgroundColour.Click += OnBackgroundColourOnClick;

        SizeChanged += ImagePanel_SizeChanged;
        viewPanelImage.DoZoomToFit += (sender, e) => DoZoomToFit();
        viewPanelImage.ImageLoaded += (sender, e) => ContextObject.IsBusy = false;

        viewPanel.PreviewMouseWheel += ViewPanel_PreviewMouseWheel;
        viewPanel.MouseLeftButtonDown += ViewPanel_MouseLeftButtonDown;
        viewPanel.MouseMove += ViewPanel_MouseMove;
        viewPanel.MouseDoubleClick += ViewPanel_MouseDoubleClick;

        viewPanel.ManipulationInertiaStarting += ViewPanel_ManipulationInertiaStarting;
        viewPanel.ManipulationStarting += ViewPanel_ManipulationStarting;
        viewPanel.ManipulationDelta += ViewPanel_ManipulationDelta;
    }

    internal ImagePanel(ContextObject context, MetaProvider meta) : this()
    {
        ContextObject = context;
        Meta = meta;

        ShowMeta();
        Theme = ContextObject.Theme;
    }

    public bool ZoomWithControlKey
    {
        get => _zoomWithControlKey;
        set
        {
            _zoomWithControlKey = value;
            OnPropertyChanged();
        }
    }

    public bool ShowZoomLevelInfo
    {
        get => _showZoomLevelInfo;
        set
        {
            if (value == _showZoomLevelInfo) return;
            _showZoomLevelInfo = value;
            OnPropertyChanged();
        }
    }

    public Themes Theme
    {
        get => ContextObject?.Theme ?? Themes.Dark;
        set
        {
            ContextObject.Theme = value;
            OnPropertyChanged();
        }
    }

    public BitmapScalingMode RenderMode
    {
        get => _renderMode;
        set
        {
            _renderMode = value;
            OnPropertyChanged();
        }
    }

    public bool ZoomToFit
    {
        get => _zoomToFit;
        set
        {
            _zoomToFit = value;
            OnPropertyChanged();
        }
    }

    public Visibility CopyVisibility
    {
        get => _copyVisibility;
        set
        {
            _copyVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility SaveAsVisibility
    {
        get => _saveAsVisibility;
        set
        {
            _saveAsVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility ReverseColorVisibility
    {
        get => _reverseColorVisibility;
        set
        {
            _reverseColorVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility MetaIconVisibility
    {
        get => _metaIconVisibility;
        set
        {
            _metaIconVisibility = value;
            OnPropertyChanged();
        }
    }

    public Visibility BackgroundVisibility
    {
        get => _backgroundVisibility;
        set
        {
            _backgroundVisibility = value;
            OnPropertyChanged();
        }
    }

    public double MinZoomFactor
    {
        get => _minZoomFactor;
        set
        {
            _minZoomFactor = value;
            OnPropertyChanged();
        }
    }

    public double MaxZoomFactor
    {
        get => _maxZoomFactor;
        set
        {
            _maxZoomFactor = value;
            OnPropertyChanged();
        }
    }

    public double ZoomToFitFactor
    {
        get => _zoomToFitFactor;
        private set
        {
            _zoomToFitFactor = value;
            OnPropertyChanged();
        }
    }

    public double ZoomFactor
    {
        get => _zoomFactor;
        private set
        {
            _zoomFactor = value;
            OnPropertyChanged();

            if (_isZoomFactorFirstSet)
            {
                _isZoomFactorFirstSet = false;
                return;
            }

            if (ShowZoomLevelInfo)
                ((Storyboard)zoomLevelInfo.FindResource("StoryboardShowZoomLevelInfo")).Begin();
        }
    }

    public Uri ImageUriSource
    {
        get => _imageSource;
        set
        {
            _imageSource = value;

            OnPropertyChanged();
        }
    }

    public BitmapSource Source
    {
        get => _source;
        set
        {
            if (_source == null)
            {
                Theme = (Themes)SettingHelper.Get("LastTheme", (int)Theme, "QuickLook.Plugin.ImageViewer");

                if (Theme == Themes.Dark)
                {
                    _source = InvertColors(value);
                }
                else
                {
                    _source = value;
                }
            }
            else
            {
                _source = value;
            }
            OnPropertyChanged();

            if (ImageUriSource == null)
                viewPanelImage.Source = _source;
        }
    }

    public ContextObject ContextObject
    {
        get => _contextObject;
        set
        {
            _contextObject = value;
            OnPropertyChanged();
        }
    }

    public MetaProvider Meta
    {
        get => _meta;
        set
        {
            if (Equals(value, _meta)) return;
            _meta = value;
            OnPropertyChanged();
        }
    }

    public void Dispose()
    {
        viewPanelImage?.Dispose();
        viewPanelImage = null;
    }

    public event PropertyChangedEventHandler PropertyChanged = null!;

    private void OnCopyOnClick(object sender, RoutedEventArgs e)
    {
        if (_source == null)
        {
            return;
        }

        try
        {
            Clipboard.SetImage(_source);
        }
        catch
        {
            ///
        }
    }

    private void OnSaveAsOnClick(object sender, RoutedEventArgs e)
    {
        if (_source == null)
        {
            return;
        }

        var dialog = new SaveFileDialog()
        {
            Filter = "PNG Image|*.png",
            DefaultExt = ".png",
            FileName = Path.GetFileNameWithoutExtension(ContextObject.Title)
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                if (File.Exists(dialog.FileName))
                {
                    File.Delete(dialog.FileName);
                }

                PngBitmapEncoder encoder = new();
                encoder.Frames.Add(BitmapFrame.Create(_source));
                using FileStream stream = new(dialog.FileName, FileMode.Create, FileAccess.Write);
                encoder.Save(stream);
            }
            catch
            {
                ///
            }
        }
    }

    private void OnReverseColorOnClick(object sender, RoutedEventArgs e)
    {
        if (_source == null)
        {
            return;
        }

        Source = InvertColors(_source);
    }

    private static BitmapSource InvertColors(BitmapSource source)
    {
        WriteableBitmap writableBitmap = new(source);
        writableBitmap.Lock();

        nint pBackBuffer = writableBitmap.BackBuffer;
        int stride = writableBitmap.BackBufferStride;
        int width = writableBitmap.PixelWidth;
        int height = writableBitmap.PixelHeight;
        int bytesPerPixel = (writableBitmap.Format.BitsPerPixel + 7) / 8;

        unsafe
        {
            byte* pPixels = (byte*)pBackBuffer;

            for (int y = 0; y < height; y++)
            {
                Span<byte> row = new(pPixels + y * stride, width * bytesPerPixel);

                for (int x = 0; x < width; x++)
                {
                    int index = x * bytesPerPixel;

                    row[index] = (byte)(255 - row[index]);
                    row[index + 1] = (byte)(255 - row[index + 1]);
                    row[index + 2] = (byte)(255 - row[index + 2]);
                }
            }
        }

        writableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        writableBitmap.Unlock();

        return writableBitmap;
    }

    private void OnBackgroundColourOnClick(object sender, RoutedEventArgs e)
    {
        Theme = Theme == Themes.Dark ? Themes.Light : Themes.Dark;

        SettingHelper.Set("LastTheme", (int)Theme, "QuickLook.Plugin.ImageViewer");
    }

    private void ShowMeta()
    {
    }

    public event EventHandler<int> ImageScrolled = null!;

    public event EventHandler ZoomChanged = null!;

    private void ImagePanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateZoomToFitFactor();

        if (ZoomToFit)
            DoZoomToFit();
    }

    private void ViewPanel_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e)
    {
        e.TranslationBehavior = new InertiaTranslationBehavior
        {
            InitialVelocity = e.InitialVelocities.LinearVelocity,
            DesiredDeceleration = 10.0 * 96.0 / (1000.0 * 1000.0)
        };
    }

    private void ViewPanel_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
    {
        e.ManipulationContainer = viewPanel;
        e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
    }

    private void ViewPanel_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
    {
        var delta = e.DeltaManipulation;

        var newZoom = ZoomFactor + ZoomFactor * (delta.Scale.X - 1);

        Zoom(newZoom);

        viewPanel.ScrollToHorizontalOffset(viewPanel.HorizontalOffset - delta.Translation.X);
        viewPanel.ScrollToVerticalOffset(viewPanel.VerticalOffset - delta.Translation.Y);

        e.Handled = true;
    }

    private void ViewPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.MouseDevice.Capture(viewPanel);

        _dragInitPos = e.GetPosition(viewPanel);
        var temp = _dragInitPos.Value; // Point is a type value
        temp.Offset(viewPanel.HorizontalOffset, viewPanel.VerticalOffset);
        _dragInitPos = temp;
    }

    private void ViewPanel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        DoZoomToFit();
    }

    private void ViewPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragInitPos.HasValue)
            return;

        if (e.LeftButton == MouseButtonState.Released)
        {
            e.MouseDevice.Capture(null);

            _dragInitPos = null;
            return;
        }

        e.Handled = true;

        var delta = _dragInitPos.Value - e.GetPosition(viewPanel);

        viewPanel.ScrollToHorizontalOffset(delta.X);
        viewPanel.ScrollToVerticalOffset(delta.Y);
    }

    private void ViewPanel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        // normal scroll when Control is not pressed, useful for PdfViewer
        if (ZoomWithControlKey && (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            viewPanel.ScrollToVerticalOffset(viewPanel.VerticalOffset - e.Delta);
            ImageScrolled?.Invoke(this, e.Delta);
            return;
        }

        // otherwise, perform normal zooming
        var newZoom = ZoomFactor + ZoomFactor * e.Delta / 120 * 0.1;

        Zoom(newZoom);
    }

    public Size GetScrollSize()
    {
        return new Size(viewPanel.ScrollableWidth, viewPanel.ScrollableHeight);
    }

    public Point GetScrollPosition()
    {
        return new Point(viewPanel.HorizontalOffset, viewPanel.VerticalOffset);
    }

    public void SetScrollPosition(Point point)
    {
        viewPanel.ScrollToHorizontalOffset(point.X);
        viewPanel.ScrollToVerticalOffset(point.Y);
    }

    public void DoZoomToFit()
    {
        UpdateZoomToFitFactor();

        Zoom(ZoomToFitFactor, false, true);
    }

    private void UpdateZoomToFitFactor()
    {
        if (viewPanelImage?.Source == null)
        {
            ZoomToFitFactor = 1d;
            return;
        }

        var factor = Math.Min(viewPanel.ActualWidth / viewPanelImage.Source.Width,
            viewPanel.ActualHeight / viewPanelImage.Source.Height);

        ZoomToFitFactor = factor;
    }

    public void ResetZoom()
    {
        ZoomToFitFactor = 1;
        Zoom(1d, true, ZoomToFit);
    }

    public void Zoom(double factor, bool suppressEvent = false, bool isToFit = false)
    {
        if (viewPanelImage?.Source == null)
            return;

        // pause when fit width
        if (ZoomFactor < ZoomToFitFactor && factor > ZoomToFitFactor
            || ZoomFactor > ZoomToFitFactor && factor < ZoomToFitFactor)
        {
            factor = ZoomToFitFactor;
            ZoomToFit = true;
        }
        // pause when 100%
        else if (ZoomFactor < 1 && factor > 1 || ZoomFactor > 1 && factor < 1)
        {
            factor = 1;
            ZoomToFit = false;
        }
        else
        {
            if (!isToFit)
                ZoomToFit = false;
        }

        factor = Math.Max(factor, MinZoomFactor);
        factor = Math.Min(factor, MaxZoomFactor);

        ZoomFactor = factor;

        var position = ZoomToFit
            ? new Point(viewPanelImage.Source.Width / 2, viewPanelImage.Source.Height / 2)
            : Mouse.GetPosition(viewPanelImage);

        viewPanelImage.LayoutTransform = new ScaleTransform(factor, factor);

        viewPanel.InvalidateMeasure();

        // critical for calculating offset
        viewPanel.ScrollToHorizontalOffset(0);
        viewPanel.ScrollToVerticalOffset(0);
        UpdateLayout();

        var offset = viewPanelImage.TranslatePoint(position, viewPanel) - Mouse.GetPosition(viewPanel);
        viewPanel.ScrollToHorizontalOffset(offset.X);
        viewPanel.ScrollToVerticalOffset(offset.Y);
        UpdateLayout();

        if (!suppressEvent)
            FireZoomChangedEvent();
    }

    private void FireZoomChangedEvent()
    {
        _lastZoomTime = DateTime.Now;

        Task.Delay(500).ContinueWith(t =>
        {
            if (DateTime.Now - _lastZoomTime < TimeSpan.FromSeconds(0.5))
                return;

            Debug.WriteLine($"FireZoomChangedEvent fired: {Thread.CurrentThread.ManagedThreadId}");

            Dispatcher.BeginInvoke(new Action(() => ZoomChanged?.Invoke(this, new EventArgs())),
                DispatcherPriority.Background);
        });
    }

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void ScrollToTop()
    {
        viewPanel.ScrollToTop();
    }

    public void ScrollToBottom()
    {
        viewPanel.ScrollToBottom();
    }
}
