using OpenCvSharp;

namespace FaceRecognition.Services;

public sealed class FaceDetectionResult
{
    public Rect Region { get; }
    public string Label { get; }
    public double Confidence { get; }

    public FaceDetectionResult(Rect region, string label, double confidence)
    {
        Region = region;
        Label = label;
        Confidence = confidence;
    }
}
