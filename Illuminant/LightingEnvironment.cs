using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        // If you have very detailed obstruction geometry, set this lower to reduce GPU load.
        // For coarse geometry you might want to set this higher to reduce CPU load and memory usage.
        public static float DefaultSubdivision = 128f;

        public readonly SpatialCollection<LightSource> LightSources = new SpatialCollection<LightSource>(DefaultSubdivision);
        public readonly SpatialCollection<LightObstructionBase> Obstructions = new SpatialCollection<LightObstructionBase>(DefaultSubdivision);
        public readonly SpatialCollection<HeightVolumeBase> HeightVolumes = new SpatialCollection<HeightVolumeBase>(DefaultSubdivision);

        public float GroundZ = 0f;

        // Scaling factor for falloff based on Z (since Z is 0-1 instead of in pixels)
        public float ZDistanceScale = 1f;
        // Offsets Y coordinates by (Z * -ZToYMultiplier) if TwoPointFiveD is enabled
        public float ZToYMultiplier = 1f;

        public void EnumerateObstructionLinesInBounds (Bounds bounds, ILineWriter output) {
            {
                SpatialCollection<HeightVolumeBase>.ItemInfo ii;

                using (var e = HeightVolumes.GetItemsFromBounds(bounds, false))
                while (e.GetNext(out ii)) {
                    if (!ii.Bounds.Intersects(bounds))
                        continue;

                    ii.Item.GenerateLines(output);
                }
            }

            {
                SpatialCollection<LightObstructionBase>.ItemInfo ii;

                using (var e = Obstructions.GetItemsFromBounds(bounds, false))
                while (e.GetNext(out ii)) {
                    if (!ii.Bounds.Intersects(bounds))
                        continue;

                    ii.Item.GenerateLines(output);
                }
            }
        }

        public void EnumerateObstructionLinesInBounds (Bounds bounds, ILineWriter output, ref bool cancel) {
            {
                SpatialCollection<HeightVolumeBase>.ItemInfo ii;

                using (var e = HeightVolumes.GetItemsFromBounds(bounds, false))
                while (e.GetNext(out ii)) {
                    if (!ii.Bounds.Intersects(bounds))
                        continue;

                    ii.Item.GenerateLines(output);

                    if (cancel)
                        return;
                }
            }

            {
                SpatialCollection<LightObstructionBase>.ItemInfo ii;

                using (var e = Obstructions.GetItemsFromBounds(bounds, false))
                while (e.GetNext(out ii)) {
                    if (!ii.Bounds.Intersects(bounds))
                        continue;

                    ii.Item.GenerateLines(output);

                    if (cancel)
                        return;
                }
            }
        }

        public void Clear () {
            Obstructions.Clear();
            LightSources.Clear();
        }
    }

    public class CroppedListLineWriter : ILineWriter {
        public struct Line {
            public readonly Vector2 A, B;
            public readonly Vector2 AHeights, BHeights;

            public Line (Vector2 a, Vector2 aHeights, Vector2 b, Vector2 bHeights) {
                A = a; 
                B = b;
                AHeights = aHeights;
                BHeights = bHeights;
            }
        }

        public Bounds? CropBounds;
        public readonly UnorderedList<Line> Lines = new UnorderedList<Line>();

        public void Write (Vector2 a, Vector2 aHeights, Vector2 b, Vector2 bHeights) {
            if (CropBounds.HasValue) {
                // constructor doesn't get inlined here :(
                Bounds lineBounds;
                lineBounds.TopLeft.X = Math.Min(a.X, b.X);
                lineBounds.TopLeft.Y = Math.Min(a.Y, b.Y);
                lineBounds.BottomRight.X = Math.Max(a.X, b.X);
                lineBounds.BottomRight.Y = Math.Max(a.Y, b.Y);

                if (!CropBounds.Value.Intersects(lineBounds))
                    return;
            }

            Lines.Add(new Line(a, aHeights, b, bHeights));
        }

        public void Reset () {
            Lines.Clear();
        }
    }

    internal class ReceivedLightIntersectionTester : ILineWriter {
        public LightPosition ReceiverPosition, LightPosition;
        public bool FoundIntersection;

        public void Reset (LightPosition receiverPosition, LightPosition lightPosition) {
            ReceiverPosition = receiverPosition;
            LightPosition = lightPosition;
            FoundIntersection = false;
        }

        public void Write (
            Vector2 a, Vector2 aHeights,
            Vector2 b, Vector2 bHeights
        ) {
            // FIXME: This is probably wrong
            var _a = new Vector3(a, aHeights.Y);
            var _b = new Vector3(b, bHeights.Y);

            FoundIntersection |= Geometry.DoLinesIntersect(_a, _b, LightPosition, ReceiverPosition);
        }
    }
}
