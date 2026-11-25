using FaceRecognition.Services;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace FaceRecognition.Forms;

public sealed class MainForm : Form
{
    private readonly PictureBox _picture;
    private readonly Label _statusLabel;
    private readonly Label _resultLabel;
    private readonly TextBox _labelBox;
    private readonly CheckBox _saveCrops;
    private readonly System.Windows.Forms.Timer _timer;
    private FaceAnalyzer? _analyzer;
    private VideoCapture? _camera;
    private Mat? _lastFrame;
    private bool _running;

    public MainForm()
    {
        Text = "YMH219 OpenCV Yüz Algılama ve Tanıma";
        ClientSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 560);

        _statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Padding = new Padding(10, 6, 10, 6),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Text = "Kamera kapalı"
        };

        _resultLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new Padding(10, 8, 10, 8),
            Text = "Etiket: -    Güven: -"
        };

        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
        var start = new Button { Text = "Kamera Başlat", Width = 130, Height = 30 };
        var stop = new Button { Text = "Kamera Durdur", Width = 130, Height = 30 };
        var open = new Button { Text = "Görüntü Seç", Width = 120, Height = 30 };
        var train = new Button { Text = "Örnekleri Eğit", Width = 120, Height = 30 };
        var add = new Button { Text = "Bu Yüzü Kaydet", Width = 130, Height = 30 };
        _labelBox = new TextBox { Width = 120, Text = "Sila" };
        _saveCrops = new CheckBox { Text = "Algılanan yüzleri kaydet", AutoSize = true, Margin = new Padding(12, 8, 8, 8) };

        start.Click += (_, _) => StartCamera();
        stop.Click += (_, _) => StopCamera();
        open.Click += (_, _) => OpenImage();
        train.Click += (_, _) => Train();
        add.Click += (_, _) => SaveCurrentFace();

        tools.Controls.Add(start);
        tools.Controls.Add(stop);
        tools.Controls.Add(open);
        tools.Controls.Add(train);
        tools.Controls.Add(new Label { Text = "Etiket:", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
        tools.Controls.Add(_labelBox);
        tools.Controls.Add(add);
        tools.Controls.Add(_saveCrops);

        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };

        Controls.Add(_picture);
        Controls.Add(tools);
        Controls.Add(_statusLabel);
        Controls.Add(_resultLabel);

        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (_, _) => GrabFrame();

        Load += (_, _) => InitializeAnalyzer();
        FormClosing += (_, _) =>
        {
            StopCamera();
            _lastFrame?.Dispose();
            _analyzer?.Dispose();
        };
    }

    private void InitializeAnalyzer()
    {
        try
        {
            string cascade = Path.Combine(AppContext.BaseDirectory, "haarcascade_frontalface_default.xml");
            string samples = Path.Combine(AppContext.BaseDirectory, "Samples");
            _analyzer = new FaceAnalyzer(cascade, samples);
            _analyzer.TrainFromSamples();
            _statusLabel.Text = _analyzer.ModelReady
                ? "Model hazır. Kamera başlatın veya görüntü seçin."
                : "Cascade yüklendi; örnek görüntü bulunamadı. Önce örnek ekleyin.";
            _statusLabel.BackColor = Color.FromArgb(198, 239, 206);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Başlatma hatası: " + ex.Message;
            _statusLabel.BackColor = Color.FromArgb(255, 220, 220);
            MessageBox.Show(this, ex.Message, "OpenCV", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartCamera()
    {
        if (_analyzer is null)
        {
            MessageBox.Show(this, "Model yüklenemedi.", "Kamera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            StopCamera();
            _camera = new VideoCapture(0);
            if (!_camera.IsOpened())
            {
                _camera.Dispose();
                _camera = null;
                throw new InvalidOperationException("Kamera bulunamadı veya açılamadı.");
            }

            _running = true;
            _timer.Start();
            _statusLabel.Text = "Kamera açık";
            _statusLabel.BackColor = Color.FromArgb(198, 239, 206);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Kamera", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Kamera hatası";
            _statusLabel.BackColor = Color.FromArgb(255, 220, 220);
        }
    }

    private void StopCamera()
    {
        _running = false;
        _timer.Stop();
        _camera?.Release();
        _camera?.Dispose();
        _camera = null;
        _statusLabel.Text = "Kamera kapalı";
    }

    private void GrabFrame()
    {
        if (!_running || _camera is null || _analyzer is null)
        {
            return;
        }

        var frame = new Mat();
        if (!_camera.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            return;
        }

        ShowAnalyzed(frame);
    }

    private void OpenImage()
    {
        if (_analyzer is null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Filter = "Görüntü|*.jpg;*.jpeg;*.png;*.bmp"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        StopCamera();
        Mat frame = Cv2.ImRead(dialog.FileName);
        if (frame.Empty())
        {
            MessageBox.Show(this, "Görüntü okunamadı.", "Dosya", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            frame.Dispose();
            return;
        }

        ShowAnalyzed(frame);
    }

    private void ShowAnalyzed(Mat frame)
    {
        if (_analyzer is null)
        {
            frame.Dispose();
            return;
        }

        string cropDir = Path.Combine(AppContext.BaseDirectory, "CapturedFaces");
        List<FaceDetectionResult> faces = _analyzer.Analyze(frame, _saveCrops.Checked, cropDir);

        foreach (FaceDetectionResult face in faces)
        {
            Cv2.Rectangle(frame, face.Region, Scalar.Lime, 2);
            string caption = $"{face.Label} ({face.Confidence:0.0})";
            Cv2.PutText(frame, caption, new OpenCvSharp.Point(face.Region.X, Math.Max(20, face.Region.Y - 8)),
                HersheyFonts.HersheySimplex, 0.6, Scalar.Yellow, 2);
        }

        _resultLabel.Text = faces.Count == 0
            ? "Yüz algılanmadı"
            : string.Join("   |   ", faces.Select(f => $"Etiket: {f.Label}  Güven: {f.Confidence:0.0}"));

        Bitmap bitmap = BitmapConverter.ToBitmap(frame);
        Image? old = _picture.Image;
        _picture.Image = bitmap;
        old?.Dispose();

        _lastFrame?.Dispose();
        _lastFrame = frame;
    }

    private void Train()
    {
        try
        {
            _analyzer?.TrainFromSamples();
            MessageBox.Show(this,
                _analyzer?.ModelReady == true ? "Eğitim tamamlandı." : "Eğitilecek örnek bulunamadı.",
                "Eğitim",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Eğitim hatası: " + ex.Message, "Eğitim", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveCurrentFace()
    {
        if (_analyzer is null || _lastFrame is null)
        {
            MessageBox.Show(this, "Önce bir kare yakalayın.", "Örnek", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        List<FaceDetectionResult> faces = _analyzer.Analyze(_lastFrame, false, null);
        if (faces.Count == 0)
        {
            MessageBox.Show(this, "Kayıt için yüz bulunamadı.", "Örnek", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _analyzer.SaveSample(_lastFrame, faces[0].Region, _labelBox.Text);
        _analyzer.TrainFromSamples();
        MessageBox.Show(this, "Örnek kaydedildi ve model güncellendi.", "Örnek", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
