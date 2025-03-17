using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;
using NAudio.Wave;
using NAudio.Dsp;

namespace SoundSpectrumWPF
{
    public partial class MainWindow : Window
    {
        private WasapiLoopbackCapture capture;
        private const int fftLength = 1024; // power of 2
        private Complex[] fftBuffer = new Complex[fftLength];
        private int fftPos = 0;

        private const int numBars = 20; // number of frequency groups/bars
        private List<Rectangle> bars = new List<Rectangle>();
        private List<TextBlock> barLabels = new List<TextBlock>();
        private double scalingFactor = 4096;    // scale bar heights
        private double smoothingFactor = 0.0001;    // smoothing
        private double[] smoothedAmplitudes = new double[numBars];
        private const double minBarHeight = 5;   // height for bar visibility

        private double[] barCenterFrequencies = null;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            MyCanvas.SizeChanged += MyCanvas_SizeChanged;
            StartAudioCapture();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            MyCanvas.HorizontalAlignment = HorizontalAlignment.Stretch;
            MyCanvas.VerticalAlignment = VerticalAlignment.Stretch;

            double canvasWidth = MyCanvas.ActualWidth;
            double barWidth = canvasWidth / numBars;

            for (int i = 0; i < numBars; i++)
            {
                Rectangle rect = new Rectangle
                {
                    Fill = new SolidColorBrush(Colors.LimeGreen),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1,
                    Width = barWidth - 2,
                    Height = minBarHeight
                };

                Canvas.SetLeft(rect, i * barWidth);
                Canvas.SetBottom(rect, 0);
                MyCanvas.Children.Add(rect);
                bars.Add(rect);

                TextBlock label = new TextBlock
                {
                    Text = "0 Hz",
                    Foreground = new SolidColorBrush(Colors.White)
                };

                Canvas.SetLeft(label, i * barWidth + 2);
                Canvas.SetBottom(label, minBarHeight + 5);
                MyCanvas.Children.Add(label);
                barLabels.Add(label);

                smoothedAmplitudes[i] = 0;
            }
        }

        private void MyCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (bars.Count < numBars || barLabels.Count < numBars)
                return;

            double canvasWidth = MyCanvas.ActualWidth;
            double barWidth = canvasWidth / numBars;

            for (int i = 0; i < numBars; i++)
            {
                bars[i].Width = barWidth - 2;
                Canvas.SetLeft(bars[i], i * barWidth);

                Canvas.SetLeft(barLabels[i], i * barWidth + 2);
                double currentHeight = bars[i].Height;
                Canvas.SetBottom(barLabels[i], currentHeight + 5);
            }
        }

        private void StartAudioCapture()
        {
            capture = new WasapiLoopbackCapture();
            capture.DataAvailable += Capture_DataAvailable;
            capture.RecordingStopped += Capture_RecordingStopped;
            capture.StartRecording();
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            int bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
            int sampleCount = e.BytesRecorded / bytesPerSample;

            for (int i = 0; i < sampleCount; i++)
            {
                float sample = BitConverter.ToInt16(e.Buffer, i * bytesPerSample) / 32768f;

                fftBuffer[fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(fftPos, fftLength));
                fftBuffer[fftPos].Y = 0;
                fftPos++;

                if (fftPos >= fftLength)
                {
                    fftPos = 0;
                    Complex[] fftBufferClone = new Complex[fftLength];
                    fftBuffer.CopyTo(fftBufferClone, 0);

                    FastFourierTransform.FFT(true, (int)Math.Log(fftLength, 2), fftBufferClone);

                    UpdateSpectrum(fftBufferClone);
                }
            }
        }

        private void UpdateSpectrum(Complex[] fftData)
        {
            int positiveBins = fftData.Length / 2;
            int groupSize = positiveBins / numBars;
            double[] newAmplitudes = new double[numBars];

            for (int i = 0; i < numBars; i++)
            {
                double maxAmplitude = 0;
                int start = i * groupSize;
                int end = start + groupSize;
                for (int j = start; j < end; j++)
                {
                    double magnitude = Math.Sqrt(fftData[j].X * fftData[j].X + fftData[j].Y * fftData[j].Y);
                    if (magnitude > maxAmplitude)
                        maxAmplitude = magnitude;
                }
                newAmplitudes[i] = maxAmplitude;
            }

            for (int i = 0; i < numBars; i++)
            {
                smoothedAmplitudes[i] = (smoothedAmplitudes[i] * smoothingFactor) + (newAmplitudes[i] * (1 - smoothingFactor));
            }

            double overallMax = 0;
            for (int i = 0; i < numBars; i++)
            {
                overallMax = Math.Max(overallMax, newAmplitudes[i]);
            }
            // level below which we amplify the bars.
            double targetLevel = 0.10; // adjust this value as needed
            double gain = (overallMax < targetLevel) ? (targetLevel / (overallMax + 1e-9)) : 20.0;

            if (barCenterFrequencies == null)
            {
                barCenterFrequencies = new double[numBars];
                double nyquist = capture.WaveFormat.SampleRate / 2.0;

                barCenterFrequencies[0] = 0;
                barCenterFrequencies[1] = 60;
                barCenterFrequencies[2] = 150;
                barCenterFrequencies[3] = 400;
                barCenterFrequencies[4] = 1000;
                barCenterFrequencies[5] = 2400;
                barCenterFrequencies[6] = 15000;

                int extraCount = numBars - 7;
                for (int i = 1; i <= extraCount; i++)
                {
                    barCenterFrequencies[7 + i - 1] = 15000 + i * (nyquist - 15000) / extraCount;
                }
            }

            Dispatcher.Invoke(() =>
            {
                double canvasHeight = MyCanvas.ActualHeight;
                double canvasWidth = MyCanvas.ActualWidth;
                double barWidth = canvasWidth / numBars;

                for (int i = 0; i < numBars; i++)
                {
                    double height = smoothedAmplitudes[i] * scalingFactor * gain;
                    if (height < minBarHeight)
                        height = minBarHeight;
                    if (height > canvasHeight)
                        height = canvasHeight;

                    bars[i].Height = height;

                    Canvas.SetLeft(barLabels[i], i * barWidth + 2);
                    Canvas.SetBottom(barLabels[i], height + 5);

                    barLabels[i].Text = $"{barCenterFrequencies[i]:0.0} Hz";
                }
            });
        }


        private void Capture_RecordingStopped(object sender, StoppedEventArgs e)
        {
            capture.Dispose();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (capture != null)
            {
                capture.StopRecording();
                capture.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
