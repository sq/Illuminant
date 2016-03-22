using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;

namespace ThreefoldTrials.Framework {
    public static class PerformanceStats {
        private static readonly StringBuilder StringBuilder = new StringBuilder();
        private static string _CachedString = null;

        public const int SampleCount = 15;
        public static readonly List<double> WaitSamples = new List<double>(),
            BeginDrawSamples = new List<double>(),
            DrawSamples = new List<double>(),
            EndDrawSamples = new List<double>();

        public static void PushSample (List<double> list, double sample) {
            if (list.Count == SampleCount)
                list.RemoveAt(0);

            list.Add(sample);
        }

        public static double GetAverage (List<double> list) {
            if (list.Count == 0)
                return 0;

            return list.Average();
        }

        private static string GenerateText (MultithreadedGame game) {
            StringBuilder.Clear();

            var drawAverage = GetAverage(DrawSamples);
            var beginAverage = GetAverage(BeginDrawSamples);
            var endAverage = GetAverage(EndDrawSamples);
            var waitAverage = GetAverage(WaitSamples);

            StringBuilder.AppendFormat("D {0:000.0}\r\n", drawAverage);
            StringBuilder.AppendFormat("BE {0:000.0}\r\n", beginAverage + endAverage);
            StringBuilder.AppendFormat("W {0:000.0}\r\n", waitAverage);
            StringBuilder.AppendFormat("{0:0000} batches\r\n", game.PreviousFrameTiming.BatchCount);

            return StringBuilder.ToString();
        }

        public static void Record (MultithreadedGame game) {
            PushSample(WaitSamples, game.PreviousFrameTiming.Wait.TotalMilliseconds);
            PushSample(BeginDrawSamples, game.PreviousFrameTiming.BeginDraw.TotalMilliseconds);
            PushSample(DrawSamples, game.PreviousFrameTiming.Draw.TotalMilliseconds);
            PushSample(EndDrawSamples, game.PreviousFrameTiming.EndDraw.TotalMilliseconds);

            _CachedString = null;
        }

        public static string GetText (MultithreadedGame game) {
            if (_CachedString == null) {
                _CachedString = GenerateText(game);
            }

            return _CachedString;
        }
    }
}
