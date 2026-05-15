using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using System;
using System.Collections.Generic;
using System.Linq;

namespace eco.Services
{
    internal class YoloService
    {
        private InferenceSession _session;
        private readonly int _inputSize = 640;

        public YoloService(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        public List<Prediction> Predict(Mat frame)
        {
            if (frame.Empty()) return new List<Prediction>();

            using var resized = new Mat();
            Cv2.Resize(frame, resized, new Size(_inputSize, _inputSize));
            Cv2.CvtColor(resized, resized, ColorConversionCodes.BGR2RGB);

            var inputTensor = CreateTensor(resized);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            using var results = _session.Run(inputs);

            var output = results.First().AsEnumerable<float>().ToArray();

            var detectedObjects = new List<Prediction>();

            for (int i = 0; i < output.Length; i += 6)
            {
                if (i + 5 >= output.Length) break;

                float score = output[i + 4];

                if (score < 0.25f) continue;

                float x1 = output[i];
                float y1 = output[i + 1];
                float x2 = output[i + 2];
                float y2 = output[i + 3];

                float x1Norm = x1 / _inputSize;
                float y1Norm = y1 / _inputSize;
                float x2Norm = x2 / _inputSize;
                float y2Norm = y2 / _inputSize;

                float finalX = x1Norm * frame.Width;
                float finalY = y1Norm * frame.Height;
                float finalW = (x2Norm - x1Norm) * frame.Width;
                float finalH = (y2Norm - y1Norm) * frame.Height;

                detectedObjects.Add(new Prediction
                {
                    Box = new Rect((int)finalX, (int)finalY, (int)finalW, (int)finalH),
                    Score = score
                });
            }

            var boxes = detectedObjects.Select(x => x.Box).ToArray();
            var scores = detectedObjects.Select(x => x.Score).ToArray();

            if (boxes.Length == 0) return new List<Prediction>();

            CvDnn.NMSBoxes(boxes, scores, 0.25f, 0.45f, out int[] indexes);

            return indexes.Select(idx => detectedObjects[idx]).ToList();
        }

        private DenseTensor<float> CreateTensor(Mat img)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, _inputSize, _inputSize });

            Mat[] channels = Cv2.Split(img);

            for (int c = 0; c < 3; c++)
            {
                channels[c].ConvertTo(channels[c], MatType.CV_32F, 1.0 / 255.0);
                float[] data = new float[_inputSize * _inputSize];
                channels[c].GetArray(out data);

                for (int i = 0; i < data.Length; i++)
                {
                    tensor[0, c, i / _inputSize, i % _inputSize] = data[i];
                }
                channels[c].Dispose();
            }
            return tensor;
        }
    }

    public class Prediction
    {
        public Rect Box { get; set; }
        public float Score { get; set; }
    }
}