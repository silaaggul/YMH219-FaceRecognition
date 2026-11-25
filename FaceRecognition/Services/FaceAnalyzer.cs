using OpenCvSharp;
using OpenCvSharp.Face;

namespace FaceRecognition.Services;

/// <summary>
/// Haar cascade ile yüz algılama ve LBPH ile yerel örnek kümede etiketleme.
/// </summary>
public sealed class FaceAnalyzer : IDisposable
{
    private readonly CascadeClassifier _cascade;
    private LBPHFaceRecognizer? _recognizer;
    private readonly Dictionary<int, string> _labels = new();
    private int _nextId = 1;

    public string CascadePath { get; }
    public string SamplesRoot { get; }
    public bool ModelReady => _recognizer is not null && _labels.Count > 0;
    public IReadOnlyDictionary<int, string> Labels => _labels;

    public FaceAnalyzer(string cascadePath, string samplesRoot)
    {
        CascadePath = cascadePath;
        SamplesRoot = samplesRoot;

        if (!File.Exists(cascadePath))
        {
            throw new FileNotFoundException("Haar cascade dosyası bulunamadı.", cascadePath);
        }

        _cascade = new CascadeClassifier(cascadePath);
        if (_cascade.Empty())
        {
            throw new InvalidOperationException("Cascade modeli yüklenemedi.");
        }
    }

    public void TrainFromSamples()
    {
        _labels.Clear();
        _nextId = 1;
        var images = new List<Mat>();
        var ids = new List<int>();

        if (!Directory.Exists(SamplesRoot))
        {
            Directory.CreateDirectory(SamplesRoot);
        }

        foreach (string personDir in Directory.GetDirectories(SamplesRoot))
        {
            string name = Path.GetFileName(personDir);
            int id = _nextId++;
            _labels[id] = name;

            foreach (string file in Directory.EnumerateFiles(personDir, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                     f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
            {
                using var src = Cv2.ImRead(file, ImreadModes.Grayscale);
                if (src.Empty())
                {
                    continue;
                }

                Rect[] faces = _cascade.DetectMultiScale(src, 1.1, 4, HaarDetectionTypes.ScaleImage, new Size(40, 40));
                Mat face = faces.Length > 0 ? new Mat(src, faces[0]) : src.Clone();
                Cv2.Resize(face, face, new Size(160, 160));
                images.Add(face);
                ids.Add(id);
            }
        }

        if (images.Count == 0)
        {
            _recognizer = null;
            return;
        }

        _recognizer?.Dispose();
        _recognizer = LBPHFaceRecognizer.Create();
        _recognizer.Train(images, ids.ToArray());

        foreach (Mat mat in images)
        {
            mat.Dispose();
        }
    }

    public List<FaceDetectionResult> Analyze(Mat bgrFrame, bool saveCrops, string? saveFolder)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgrFrame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.EqualizeHist(gray, gray);

        Rect[] faces = _cascade.DetectMultiScale(gray, 1.2, 5, HaarDetectionTypes.ScaleImage, new Size(60, 60));
        var results = new List<FaceDetectionResult>();

        int index = 0;
        foreach (Rect rect in faces)
        {
            string label = "Bilinmiyor";
            double confidence = 0;

            if (_recognizer is not null)
            {
                using var roi = new Mat(gray, rect);
                using var resized = new Mat();
                Cv2.Resize(roi, resized, new Size(160, 160));
                _recognizer.Predict(resized, out int predicted, out double dist);
                if (_labels.TryGetValue(predicted, out string? name))
                {
                    label = name;
                    // LBPH uzaklığı: küçük değer daha iyi eşleşme
                    confidence = Math.Max(0, Math.Min(100, 100.0 - dist));
                    if (dist > 90)
                    {
                        label = "Bilinmiyor";
                    }
                }
            }

            results.Add(new FaceDetectionResult(rect, label, Math.Round(confidence, 1)));

            if (saveCrops && !string.IsNullOrWhiteSpace(saveFolder))
            {
                Directory.CreateDirectory(saveFolder);
                using var crop = new Mat(bgrFrame, rect);
                Cv2.ImWrite(Path.Combine(saveFolder, $"face_{DateTime.Now:HHmmss}_{index}.png"), crop);
            }

            index++;
        }

        return results;
    }

    public void SaveSample(Mat bgrFrame, Rect region, string label)
    {
        string dir = Path.Combine(SamplesRoot, Sanitize(label));
        Directory.CreateDirectory(dir);
        using var crop = new Mat(bgrFrame, region);
        string path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        Cv2.ImWrite(path, crop);
    }

    private static string Sanitize(string label)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            label = label.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(label) ? "Ornek" : label.Trim();
    }

    public void Dispose()
    {
        _recognizer?.Dispose();
        _cascade.Dispose();
    }
}
