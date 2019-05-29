using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Evil;
using Squared.Render.Tracing;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private void LoadOneMaterial (out Material result, string fileName, string techniqueName, Action<DeviceManager>[] begin = null, Action<DeviceManager>[] end = null) {
            try {
                var m = new Material(
                    Effects.Load(fileName), techniqueName,
                    begin, end
                );
                result = m;
                DefineMaterial(m);
            } catch (Exception exc) {
                result = null;
                Console.WriteLine("Failed to load shader {0} technique {1}: {2}", fileName, techniqueName, exc);
            }
        }

        private void LoadMaterials (EmbeddedEffectProvider effects) {
            lock (IlluminantMaterials) {
                if (IlluminantMaterials.IsLoaded)
                    return;

                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        depthStencilState: NeutralDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                LoadOneMaterial(out IlluminantMaterials.SphereLight,
                    "SphereLight", "SphereLight", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.DirectionalLight,
                    "DirectionalLight", "DirectionalLight", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.ParticleSystemSphereLight,
                    "ParticleLight", "ParticleLight", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.LineLight,
                    "LineLight", "LineLight", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.SphereLightWithDistanceRamp,
                    "SphereLight", "SphereLightWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.SphereLightWithOpacityRamp,
                    "SphereLight", "SphereLightWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.DirectionalLightWithRamp,
                    "DirectionalLight", "DirectionalLightWithRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.LineLightWithDistanceRamp,
                    "LineLightEx", "LineLightWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.LineLightWithOpacityRamp,
                    "LineLightEx", "LineLightWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.SphereLightProbe,
                    "SphereLightProbe", "SphereLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.SphereLightProbeWithDistanceRamp,
                    "SphereLightProbe", "SphereLightProbeWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.SphereLightProbeWithOpacityRamp,
                    "SphereLightProbe", "SphereLightProbeWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.DirectionalLightProbe,
                    "DirectionalLight", "DirectionalLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.DirectionalLightProbeWithRamp,
                    "DirectionalLight", "DirectionalLightProbeWithRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.LineLightProbe,
                    "LineLightProbe", "LineLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.LineLightProbeWithDistanceRamp,
                    "LineLightProbe", "LineLightProbeWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.LineLightProbeWithOpacityRamp,
                    "LineLightProbe", "LineLightProbeWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.GIProbeSelector,
                    "GIProbe", "ProbeSelector", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.GIProbeSHGenerator,
                    "GIProbe", "SHGenerator", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.VisualizeGI,
                    "GI", "VisualizeGI", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.RenderGI,
                    "GI", "RenderGI", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.RenderLightProbesFromGI,
                    "GI", "RenderLightProbesFromGI", dBegin, dEnd
                );

                LoadOneMaterial(out IlluminantMaterials.DistanceToPolygon, 
                    "DistanceField", "DistanceToPolygon",
                    new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                );

                LoadOneMaterial(out IlluminantMaterials.ClearDistanceFieldSlice,
                    "ClearDistanceField", "ClearDistanceField",
                    new[] { MaterialUtil.MakeDelegate(BlendState.Opaque) }
                );

                IlluminantMaterials.DistanceFunctionTypes = new Render.Material[(int)LightObstructionType.MAX + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    LoadOneMaterial(out IlluminantMaterials.DistanceFunctionTypes[(int)i],
                        "DistanceFunction", name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    );
                }

                LoadOneMaterial(out IlluminantMaterials.GroundPlane,
                    "GBuffer", "GroundPlane");

                LoadOneMaterial(out IlluminantMaterials.HeightVolume,
                    "GBuffer", "HeightVolume");

                LoadOneMaterial(out IlluminantMaterials.HeightVolumeFace,
                    "GBuffer", "HeightVolumeFace");

                LoadOneMaterial(out IlluminantMaterials.MaskBillboard,
                    "GBufferBitmap", "MaskBillboard");

                LoadOneMaterial(out IlluminantMaterials.GDataBillboard,
                    "GBufferBitmap", "GDataBillboard");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceLightingResolve,
                    "Resolve", "ScreenSpaceLightingResolve");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolve,
                    "Resolve", "ScreenSpaceGammaCompressedLightingResolve");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceToneMappedLightingResolve,
                    "Resolve", "ScreenSpaceToneMappedLightingResolve");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceLightingResolveWithAlbedo,
                    "Resolve", "ScreenSpaceLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolveWithAlbedo,
                    "Resolve", "ScreenSpaceGammaCompressedLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceToneMappedLightingResolveWithAlbedo,
                    "Resolve", "ScreenSpaceToneMappedLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceLUTBlendedLightingResolveWithAlbedo,
                    "LUTResolve", "ScreenSpaceLUTBlendedLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceLightingResolve,
                    "Resolve", "WorldSpaceLightingResolve");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceGammaCompressedLightingResolve,
                    "Resolve", "WorldSpaceGammaCompressedLightingResolve");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceToneMappedLightingResolve,
                    "Resolve", "WorldSpaceToneMappedLightingResolve");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceLightingResolveWithAlbedo,
                    "Resolve", "WorldSpaceLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceGammaCompressedLightingResolveWithAlbedo,
                    "Resolve", "WorldSpaceGammaCompressedLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceToneMappedLightingResolveWithAlbedo,
                    "Resolve", "WorldSpaceToneMappedLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceLUTBlendedLightingResolveWithAlbedo,
                    "LUTResolve", "WorldSpaceLUTBlendedLightingResolveWithAlbedo");

                LoadOneMaterial(out IlluminantMaterials.CalculateLuminance,
                    "Resolve", "CalculateLuminance");

                LoadOneMaterial(out IlluminantMaterials.ObjectSurfaces,
                    "VisualizeDistanceField", "ObjectSurfaces");

                LoadOneMaterial(out IlluminantMaterials.ObjectOutlines,
                    "VisualizeDistanceField", "ObjectOutlines");

                LoadOneMaterial(out IlluminantMaterials.FunctionSurface,
                    "VisualizeDistanceFunction", "FunctionSurface");

                LoadOneMaterial(out IlluminantMaterials.FunctionOutline,
                    "VisualizeDistanceFunction", "FunctionOutline");

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceGammaCompressedBitmap,
                    "HDRBitmap", "ScreenSpaceGammaCompressedBitmap"
                );

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceGammaCompressedBitmap,
                    "HDRBitmap", "WorldSpaceGammaCompressedBitmap"
                );

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceToneMappedBitmap,
                    "HDRBitmap", "ScreenSpaceToneMappedBitmap"
                );

                LoadOneMaterial(out IlluminantMaterials.WorldSpaceToneMappedBitmap,
                    "HDRBitmap", "WorldSpaceToneMappedBitmap"
                );

                LoadOneMaterial(out IlluminantMaterials.ScreenSpaceVectorWarp,
                    
                        "VectorWarp", "ScreenSpaceVectorWarp", 
                        new [] { MaterialUtil.MakeDelegate(BlendState.AlphaBlend) }
                    );

                IlluminantMaterials.IsLoaded = true;
            }
        }
    }
}

namespace Squared.Illuminant.Particles {
    public sealed partial class ParticleEngine : IDisposable {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private void LoadOneMaterial (out Material result, string fileName, string techniqueName, Action<DeviceManager>[] begin = null, Action<DeviceManager>[] end = null) {
            try {
                var m = new Material(
                    Effects.Load(fileName), techniqueName,
                    begin, end
                );
                result = m;
                DefineMaterial(m);
            } catch (Exception exc) {
                result = null;
                Console.WriteLine("Failed to load shader {0} technique {1}: {2}", fileName, techniqueName, exc);
            }
        }

        private void LoadMaterials (EmbeddedEffectProvider effects) {
            lock (ParticleMaterials) {
                if (ParticleMaterials.IsLoaded)
                    return;

                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone,
                        depthStencilState: DepthStencilState.None,
                        blendState: BlendState.Opaque
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                // Set null color write mask to attempt to prevent live particles' fragments
                //  from actually writing anything to the RT. This probably still will pass the
                //  occlusion test, assuming drivers aren't Really Bad...
                var noopBlendState = new BlendState {
                    AlphaBlendFunction = BlendFunction.Add,
                    ColorBlendFunction = BlendFunction.Add,
                    AlphaDestinationBlend = Blend.Zero,
                    ColorDestinationBlend = Blend.Zero,
                    AlphaSourceBlend = Blend.One,
                    ColorSourceBlend = Blend.One,
                    ColorWriteChannels = ColorWriteChannels.None,
                    ColorWriteChannels1 = ColorWriteChannels.None,
                    ColorWriteChannels2 = ColorWriteChannels.None,
                    ColorWriteChannels3 = ColorWriteChannels.None
                };

                LoadOneMaterial(out ParticleMaterials.CountLiveParticles,
                    "CountLiveParticles", "CountLiveParticles", new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: DepthStencilState.None,
                            blendState: noopBlendState
                        )
                    }, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.CollectParticles,
                    "CollectParticles", "CollectParticles", new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: DepthStencilState.None,
                            blendState: noopBlendState
                        )
                    }, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.Erase,
                    "UpdateParticleSystem", "Erase", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.UpdatePositions,
                    "UpdateParticleSystem", "UpdatePositions", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.UpdateWithDistanceField,
                    "UpdateParticleSystemWithDistanceField", "UpdateWithDistanceField", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.FMA,
                    "FMA", "FMA", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.MatrixMultiply,
                    "MatrixMultiply", "MatrixMultiply", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.Noise,
                    "Noise", "Noise", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.SpatialNoise,
                    "Noise", "SpatialNoise", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.Gravity,
                    "Gravity", "Gravity", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.Spawn,
                    "SpawnParticles", "SpawnParticles", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.SpawnFromPositionTexture,
                    "SpawnParticles", "SpawnParticlesFromPositionTexture", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.SpawnFeedback,
                    "SpawnParticles", "SpawnFeedbackParticles", dBegin, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.SpawnPattern,
                    "SpawnParticles", "SpawnPatternParticles", dBegin, dEnd
                );
                
                LoadOneMaterial(out ParticleMaterials.TextureLinear,
                    "RasterizeParticleSystem", "TextureLinear"
                );
                LoadOneMaterial(out ParticleMaterials.TexturePoint,
                    "RasterizeParticleSystem", "TexturePoint"
                );
                LoadOneMaterial(out ParticleMaterials.NoTexture,
                    "RasterizeParticleSystem", "NoTexture"
                );

                ParticleMaterials.IsLoaded = true;
            }
        }
    }
}
