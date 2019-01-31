using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Microsoft.Xna.Framework;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;

namespace Lumined {
    public partial class PropertyEditor {
        public struct ModelTypeInfo {
            public string               Type;
            public float?               Min, Max;
            public bool                 AllowNull;
            public bool                 Hidden;
            public Func<object, object> GetDefaultValue;
            public int?                 MaxCount;
            public float?               DragScale;
        }

        public static HashSet<string> GenericObjectTypes = new HashSet<string> {
            "Bounds",
            "ParticleAppearance",
            "ParticleColor",
            "ParticleCollision",
            "FMAParameters",
            "GTParameters",
            "NoiseParameters`1",
        };

        public static HashSet<string> GenericNullableObjectTypes = new HashSet<string> {
            "TransformArea",
            "ParticleColorLifeRamp"
        };

        public static readonly Dictionary<string, Dictionary<string, ModelTypeInfo>> FieldTypeOverrides =
            new Dictionary<string, Dictionary<string, ModelTypeInfo>> {
                {
                    "EditorData", new Dictionary<string, ModelTypeInfo> {
                        {"FrameRate", new ModelTypeInfo { Min = 10, Max = 120 } },
                        {"MaximumDeltaTimeMS", new ModelTypeInfo { Min = 5, Max = 2000 } },
                        {"Sprites", new ModelTypeInfo {
                            Type = "List"
                        } },
                        {"Lights", new ModelTypeInfo {
                            Type = "List"
                        } },
                    }
                },
                {
                    "ParticleTransform", new Dictionary<string, ModelTypeInfo> {
                        {"IsValid", new ModelTypeInfo { Hidden = true } },
                        {"IsAnalyzer", new ModelTypeInfo { Hidden = true } },
                    }
                },
                {
                    "ParticleSystemConfiguration", new Dictionary<string, ModelTypeInfo> {
                        {"BounceVelocityMultiplier", new ModelTypeInfo { Min = 0, Max = 3 } },
                        {"StippleFactor", new ModelTypeInfo { Min = 0, Max = 1 } },
                        {"Friction", new ModelTypeInfo { Min = 0, Max = 1 } },
                        {"MaximumVelocity", new ModelTypeInfo { Min = 0 } },
                        {"EscapeVelocity", new ModelTypeInfo { Min = 0 } },
                        {"CollisionDistance", new ModelTypeInfo { Min = 0, Max = 10 } },
                        {"CollisionLifePenalty", new ModelTypeInfo { Min = -10, Max = 1000 } },
                        {"GlobalLifeDecayRate", new ModelTypeInfo { Min = 0, Max = 1000 } },
                        {"Size", new ModelTypeInfo { Min = 0, Max = 256 } },
                        {"OpacityFromLife", new ModelTypeInfo { Min = -2048, Max = 2048 } },
                        {"SizeFromLife", new ModelTypeInfo { Min = -2048, Max = 2048 } },
                    }
                },
                {
                    "ParticleAppearance", new Dictionary<string, ModelTypeInfo> {
                        {"AnimationRate", new ModelTypeInfo { Min = -100, Max = 100 } },
                        {"Bounds", new ModelTypeInfo { Min = 0, Max = 1 } },
                        {"RoundingPower", new ModelTypeInfo { Min = 0.05f, Max = 1.25f } },
                        {"SizePx", new ModelTypeInfo { Min = 0, GetDefaultValue = (o) => {
                            var pa = (Squared.Illuminant.Particles.ParticleAppearance)o;
                            if (pa.Texture.IsInitialized)
                                return new Vector2(pa.Texture.Instance.Width, pa.Texture.Instance.Height);
                            else
                                return null;
                        } } }
                    }
                },
                {
                    "ParticleColorLifeRamp", new Dictionary<string, ModelTypeInfo> {
                        {"Strength", new ModelTypeInfo { Min = 0, Max = 1 } },
                    }
                },
                {
                    "ParticleColor", new Dictionary<string, ModelTypeInfo> {
                        {"Global", new ModelTypeInfo { Type = "ColorF" } },
                        {"FromLife", new ModelTypeInfo { Type = "ColorBezier4" } }
                    }
                },
                {
                    "PatternSpawner", new Dictionary<string, ModelTypeInfo> {
                        {"MinRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"MaxRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"Color", new ModelTypeInfo { Type = "ColorFormula" } },
                        {"Resolution", new ModelTypeInfo { Min = 0.2f, Max = 1f } },
                        {"AlphaDiscardThreshold", new ModelTypeInfo { Min = 0, Max = 255 } }
                    }
                },
                {
                    "FeedbackSpawner", new Dictionary<string, ModelTypeInfo> {
                        {"MinRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"MaxRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"Color", new ModelTypeInfo { Type = "ColorFormula" } },
                        {"SourceVelocityFactor", new ModelTypeInfo { Min = -2f, Max = 2f } },
                        {"AlphaDiscardThreshold", new ModelTypeInfo { Min = 0, Max = 255 } }
                    }
                },
                {
                    "Spawner", new Dictionary<string, ModelTypeInfo> {
                        {"MinRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"MaxRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"AdditionalPositions", new ModelTypeInfo {
                            Type = "ValueList",
                            GetDefaultValue = (obj) => {
                                var s = ((SpawnerBase)obj);
                                // FIXME: Parameter references?
                                var c = s.Position.Constant.Evaluate(0, null);
                                return c;
                            }
                        } },
                        {"Color", new ModelTypeInfo { Type = "ColorFormula" } },
                        {"AlphaDiscardThreshold", new ModelTypeInfo { Min = 0, Max = 255 } }
                    }
                },
                {
                    "ParticleAreaTransform", new Dictionary<string, ModelTypeInfo> {
                        {"Strength", new ModelTypeInfo { Min = 0, Max = 1 } },
                    }
                },
                {
                    "FMA", new Dictionary<string, ModelTypeInfo> {
                        {"CyclesPerSecond", new ModelTypeInfo { Min = -1, Max = 60 } },
                    }
                },
                {
                    "NoiseParameters`1", new Dictionary<string, ModelTypeInfo> {
                        {"Offset", new ModelTypeInfo { Min = -1, Max = 0 } }
                    }
                },
                {
                    "Noise", new Dictionary<string, ModelTypeInfo> {
                        {"Interval", new ModelTypeInfo { Min = 0, Max = 10000 } },
                    }
                },
                {
                    "SpatialNoise", new Dictionary<string, ModelTypeInfo> {
                        {"Interval", new ModelTypeInfo { Min = 0, Max = 10000 } },
                    }
                },
                {
                    "MatrixMultiply", new Dictionary<string, ModelTypeInfo> {
                        {"CyclesPerSecond", new ModelTypeInfo { Min = -1, Max = 60 } },
                        {"Velocity", new ModelTypeInfo { Type = "Matrix3x4" } }
                    }
                },
                {
                    "Gravity", new Dictionary<string, ModelTypeInfo> {
                        {"Attractors", new ModelTypeInfo {
                            Type = "List",
                            MaxCount = Gravity.MaxAttractors
                        } },
                    }
                },
                {
                    "Sensor", new Dictionary<string, ModelTypeInfo> {
                        {"Strength", new ModelTypeInfo {
                            Hidden = true
                        } }
                    }
                }
            };
    }
}
