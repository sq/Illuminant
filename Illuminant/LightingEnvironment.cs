﻿using System;
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
        public readonly List<LightReceiver> LightReceivers = new List<LightReceiver>();
        public readonly SpatialCollection<LightObstructionBase> Obstructions = new SpatialCollection<LightObstructionBase>(DefaultSubdivision);

        // The 3D volume shadows are clipped to.
        public Bounds3 ClipRegion;

        public float GroundZ {
            get {
                return ClipRegion.Minimum.Z;
            }
        }

        public float CeilingZ {
            get {
                return ClipRegion.Maximum.Z;
            }
        }

        public LightingEnvironment () {
            var huge = 99999f;

            // Random default volume that doesn't constrain x/y and has low ceiling/ground,
            //  but sufficient space for shadows at/around 0 Z to work right
            ClipRegion = new Bounds3(
                new Vector3(-huge, -huge, -1),
                new Vector3(huge, huge, 1)
            );
        }

        public void EnumerateObstructionLinesInBounds (Bounds bounds, ILineWriter output) {
            SpatialCollection<LightObstructionBase>.ItemInfo ii;

            using (var e = Obstructions.GetItemsFromBounds(bounds, false))
            while (e.GetNext(out ii)) {
                if (!ii.Bounds.Intersects(bounds))
                    continue;

                ii.Item.GenerateLines(output);
            }
        }

        public void EnumerateObstructionLinesInBounds (Bounds bounds, ILineWriter output, ref bool cancel) {
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

        /// <param name="position">The position.</param>
        /// <param name="ignoredLights">A set of lights to ignore, if any. If this value is a HashSet of LightSources it will be used directly, otherwise the sequence is copied.</param>
        /// <returns>The created receiver</returns>
        public LightReceiver AddLightReceiver (LightPosition position, LightIgnorePredicate lightIgnorePredicate = null) {
            var result = new LightReceiver {
                Position = position,
                LightIgnorePredicate = lightIgnorePredicate
            };

            LightReceivers.Add(result);

            return result;
        }

        /// <summary>
        /// Updates all the lighting environment's receivers based on the current positions of light sources and obstructions.
        /// </summary>
        public void UpdateReceivers () {
            if (LightReceivers.Count == 0)
                return;

            var query = new LightingQuery(this, true);

            foreach (var receiver in LightReceivers)
                receiver.Update(query);
        }

        public void Clear () {
            Obstructions.Clear();
            LightSources.Clear();
            LightReceivers.Clear();
        }
    }

    public class CroppedListLineWriter : ILineWriter {
        public struct Line {
            public readonly LightPosition A, B;

            public Line (LightPosition a, LightPosition b) {
                A = a; 
                B = b;
            }
        }

        public Bounds? CropBounds;
        public readonly UnorderedList<Line> Lines = new UnorderedList<Line>();

        public void Write (LightPosition a, LightPosition b) {
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

            Lines.Add(new Line(a, b));
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

        public void Write (LightPosition a, LightPosition b) {
            FoundIntersection |= Geometry.DoLinesIntersect(a, b, LightPosition, ReceiverPosition);
        }
    }
}
