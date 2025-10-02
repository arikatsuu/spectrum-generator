using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Windows.Forms;
using NAudio.Wave;
using NAudio.Flac;
using MathNet.Numerics.IntegralTransforms;
using System.Diagnostics;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // select audio file
        string audioPath = "";
        if (args.Length > 0)
            audioPath = args[0];
        else
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Audio Files|*.mp3;*.wav;*.flac";
                ofd.Title = "Select an audio file";

                if (ofd.ShowDialog() == DialogResult.OK)
                    audioPath = ofd.FileName;
                else
                {
                    Console.WriteLine("No file selected. Exiting...");
                    return;
                }
            }
        }

        ISampleProvider reader;
        double totalSeconds;

        if (audioPath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
        {
            var flac = new FlacReader(audioPath);
            reader = flac.ToSampleProvider();
            totalSeconds = flac.TotalTime.TotalSeconds;
        }
        else
        {
            var wav = new AudioFileReader(audioPath);
            reader = wav;
            totalSeconds = wav.TotalTime.TotalSeconds;
        }

        // settings
        int width = 1280;
        int height = 720;
        int fftSize = 1024;
        int frameRate = 30;
        int barCount = 64;

        int totalFrames = (int)Math.Ceiling(totalSeconds * frameRate);
        int samplesPerFrame = (int)(reader.WaveFormat.SampleRate / (double)frameRate);
        float[] frameBuffer = new float[samplesPerFrame];
        Complex[] fftBuffer = new Complex[fftSize];

        // create temporary frames folder
        string tempFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempFrames");
        Directory.CreateDirectory(tempFolder);

        Console.WriteLine($"Rendering {totalFrames} frames...");

        for (int frameCount = 0; frameCount < totalFrames; frameCount++)
        {
            int read = reader.Read(frameBuffer, 0, samplesPerFrame);
            if (read == 0) break;

            for (int i = 0; i < fftSize; i++)
                fftBuffer[i] = i < read ? new Complex(frameBuffer[i], 0) : Complex.Zero;

            Fourier.Forward(fftBuffer, FourierOptions.Matlab);

            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                int barWidth = width / barCount;

                for (int i = 0; i < barCount; i++)
                {
                    int binIndex = i * (fftSize / 2 / barCount);
                    double magnitude = fftBuffer[binIndex].Magnitude;
                    double db = 20.0 * Math.Log10(magnitude / fftSize + 1e-9);

                    double normalizedHeight = Math.Clamp((db + 100) / 100.0, 0, 1);
                    int barHeight = (int)(normalizedHeight * height);
                    barHeight = (int)Math.Clamp(barHeight * 1.2, 0, height);

                    int red = (int)(normalizedHeight * 255);
                    int green = 255 - red;
                    Color barColor = Color.FromArgb(255, 50, green, red);

                    using (var brush = new SolidBrush(barColor))
                        g.FillRectangle(brush, i * barWidth, height - barHeight, barWidth - 2, barHeight);
                }

                // save frame as png PLEASE
                string framePath = Path.Combine(tempFolder, $"frame{frameCount:D5}.png");
                bmp.Save(framePath, ImageFormat.Png);
            }

            DrawProgressBar(frameCount + 1, totalFrames);
        }

        Console.WriteLine("\nRendering complete! :3\nMerging with audio...");

        // merge frames and audio
        string outputVideo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisualizerWithAudio.mp4");
        string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");

        var ffmpegProcess = new Process();
        ffmpegProcess.StartInfo.FileName = ffmpegPath;
        ffmpegProcess.StartInfo.Arguments =
            $"-y -framerate {frameRate} -i \"{tempFolder}/frame%05d.png\" -i \"{audioPath}\" " +
            $"-c:v libx264 -pix_fmt yuv420p -c:a flac \"{outputVideo}\"";
        ffmpegProcess.StartInfo.UseShellExecute = false;
        ffmpegProcess.StartInfo.RedirectStandardError = true;
        ffmpegProcess.Start();

        string mergeLog = ffmpegProcess.StandardError.ReadToEnd();
        ffmpegProcess.WaitForExit();

        Console.WriteLine("Final video saved as " + outputVideo);

        // garbage collection
        Directory.Delete(tempFolder, true);
    }

    static void DrawProgressBar(int progress, int total, int barWidth = 50)
    {
        double percent = Math.Min(1.0, (double)progress / total);
        int filled = (int)(percent * barWidth);
        filled = Math.Clamp(filled, 0, barWidth);

        Console.Write("\r[");
        Console.Write(new string('#', filled));
        Console.Write(new string('-', barWidth - filled));
        Console.Write($"] {Math.Min(progress, total)}/{total} frames ({percent * 100:0.0}%)");
    }
}
