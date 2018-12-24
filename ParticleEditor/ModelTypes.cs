using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        public struct ModelTypeInfo {
            public string               Type;
            public float?               Min, Max;
            public bool                 AllowNull;
            public Func<object, object> GetDefaultValue;
            public int?                 MaxCount;
        }

        public static readonly Dictionary<string, Dictionary<string, ModelTypeInfo>> FieldTypeOverrides =
            new Dictionary<string, Dictionary<string, ModelTypeInfo>> {
                {
                    "ParticleSystemConfiguration", new Dictionary<string, ModelTypeInfo> {
                        {"GlobalColor", new ModelTypeInfo { Type = "ColorF" } },
                        {"BounceVelocityMultiplier", new ModelTypeInfo { Min = 0, Max = 3 } },
                        {"StippleFactor", new ModelTypeInfo { Min = 0, Max = 1 } },
                        {"Friction", new ModelTypeInfo { Min = 0, Max = 1 } },
                        {"MaximumVelocity", new ModelTypeInfo { Min = 0 } },
                        {"EscapeVelocity", new ModelTypeInfo { Min = 0 } },
                        {"CollisionDistance", new ModelTypeInfo { Min = 0, Max = 10 } },
                        {"CollisionLifePenalty", new ModelTypeInfo { Min = -10, Max = 1000 } },
                        {"GlobalLifeDecayRate", new ModelTypeInfo { Min = 0, Max = 1000 } },
                        {"Size", new ModelTypeInfo { Min = 0, Max = 256 } },
                        {"AnimationRate", new ModelTypeInfo { Min = -100, Max = 100 } },
                        {"OpacityFromLife", new ModelTypeInfo { Min = -2048, Max = 2048 } },
                        {"SizeFromLife", new ModelTypeInfo { Min = -2048, Max = 2048 } },
                    }
                },
                {
                    "Spawner", new Dictionary<string, ModelTypeInfo> {
                        {"MinRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"MaxRate", new ModelTypeInfo { Min = 0, Max = 100000 } },
                        {"AdditionalPositions", new ModelTypeInfo {
                            Type = "ValueList",
                            GetDefaultValue = (obj) => {
                                var s = ((Spawner)obj);
                                return s.Position.Constant;
                            },
                            MaxCount = Spawner.MaxPositions - 1
                        } }
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
                }
            };
    }
}
