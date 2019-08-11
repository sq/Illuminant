﻿using System;
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
    }

    public partial class IlluminantMaterials {
        private void DefineMaterial (Material m) {
            MaterialSet.Add(m);
        }

        private void LoadOneMaterial (
            EmbeddedEffectProvider effects, out Material result, string fileName, string techniqueName, 
            Action<DeviceManager>[] begin = null, Action<DeviceManager>[] end = null
        ) {
            try {
                var m = new Material(
                    effects.Load(fileName), techniqueName,
                    begin, end
                );
                result = m;
                DefineMaterial(m);
            } catch (Exception exc) {
                result = null;
                Console.WriteLine("Failed to load shader {0} technique {1}: {2}", fileName, techniqueName, exc);
            }
        }

        public void Load (RenderCoordinator coordinator, EmbeddedEffectProvider effects = null) {
            lock (this) {
                if (IsLoaded)
                    return;

                IsLoaded = true;
                // FIXME: This is a memory leak
                if (effects == null)
                    effects = new EmbeddedEffectProvider(coordinator);
                            
                var neutralDepthStencilState = new DepthStencilState {
                    StencilEnable = false,
                    DepthBufferEnable = false
                };

                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        depthStencilState: neutralDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                LoadOneMaterial(effects, out SphereLight,
                    "SphereLight", "SphereLight", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightWithoutDistanceField,
                    "SphereLightWithoutDistanceField", "SphereLightWithoutDistanceField", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLight,
                    "DirectionalLight", "DirectionalLight", dBegin, dEnd
                );

                LoadOneMaterial(effects, out ParticleSystemSphereLight,
                    "ParticleLight", "ParticleLight", dBegin, dEnd
                );

                LoadOneMaterial(effects, out ParticleSystemSphereLightWithoutDistanceField,
                    "ParticleLight", "ParticleLightWithoutDistanceField", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLight,
                    "LineLight", "LineLight", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightWithDistanceRamp,
                    "SphereLight", "SphereLightWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightWithOpacityRamp,
                    "SphereLight", "SphereLightWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightWithRamp,
                    "DirectionalLight", "DirectionalLightWithRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLightWithDistanceRamp,
                    "LineLightEx", "LineLightWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLightWithOpacityRamp,
                    "LineLightEx", "LineLightWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightProbe,
                    "SphereLightProbe", "SphereLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightProbeWithDistanceRamp,
                    "SphereLightProbe", "SphereLightProbeWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightProbeWithOpacityRamp,
                    "SphereLightProbe", "SphereLightProbeWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightProbe,
                    "DirectionalLight", "DirectionalLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightProbeWithRamp,
                    "DirectionalLight", "DirectionalLightProbeWithRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLightProbe,
                    "LineLightProbe", "LineLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLightProbeWithDistanceRamp,
                    "LineLightProbe", "LineLightProbeWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLightProbeWithOpacityRamp,
                    "LineLightProbe", "LineLightProbeWithOpacityRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out GIProbeSelector,
                    "GIProbe", "ProbeSelector", dBegin, dEnd
                );

                LoadOneMaterial(effects, out GIProbeSHGenerator,
                    "GIProbe", "SHGenerator", dBegin, dEnd
                );

                LoadOneMaterial(effects, out VisualizeGI,
                    "GI", "VisualizeGI", dBegin, dEnd
                );

                LoadOneMaterial(effects, out RenderGI,
                    "GI", "RenderGI", dBegin, dEnd
                );

                LoadOneMaterial(effects, out RenderLightProbesFromGI,
                    "GI", "RenderLightProbesFromGI", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DistanceToPolygon, 
                    "DistanceField", "DistanceToPolygon",
                    new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                );

                LoadOneMaterial(effects, out ClearDistanceFieldSlice,
                    "ClearDistanceField", "ClearDistanceField",
                    new[] { MaterialUtil.MakeDelegate(BlendState.Opaque) }
                );

                DistanceFunctionTypes = new Render.Material[(int)LightObstructionType.MAX + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    LoadOneMaterial(effects, out DistanceFunctionTypes[(int)i],
                        "DistanceFunction", name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    );
                }

                LoadOneMaterial(effects, out GroundPlane,
                    "GBuffer", "GroundPlane");

                LoadOneMaterial(effects, out HeightVolume,
                    "GBuffer", "HeightVolume");

                LoadOneMaterial(effects, out HeightVolumeFace,
                    "GBuffer", "HeightVolumeFace");

                LoadOneMaterial(effects, out MaskBillboard,
                    "GBufferBitmap", "MaskBillboard");

                LoadOneMaterial(effects, out GDataBillboard,
                    "GBufferBitmap", "GDataBillboard");

                LoadOneMaterial(effects, out ScreenSpaceLightingResolve,
                    "Resolve", "ScreenSpaceLightingResolve");

                LoadOneMaterial(effects, out ScreenSpaceGammaCompressedLightingResolve,
                    "Resolve", "ScreenSpaceGammaCompressedLightingResolve");

                LoadOneMaterial(effects, out ScreenSpaceToneMappedLightingResolve,
                    "Resolve", "ScreenSpaceToneMappedLightingResolve");

                LoadOneMaterial(effects, out ScreenSpaceLightingResolveWithAlbedo,
                    "Resolve", "ScreenSpaceLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out ScreenSpaceGammaCompressedLightingResolveWithAlbedo,
                    "Resolve", "ScreenSpaceGammaCompressedLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out ScreenSpaceToneMappedLightingResolveWithAlbedo,
                    "Resolve", "ScreenSpaceToneMappedLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out ScreenSpaceLUTBlendedLightingResolveWithAlbedo,
                    "LUTResolve", "ScreenSpaceLUTBlendedLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out WorldSpaceLightingResolve,
                    "Resolve", "WorldSpaceLightingResolve");

                LoadOneMaterial(effects, out WorldSpaceGammaCompressedLightingResolve,
                    "Resolve", "WorldSpaceGammaCompressedLightingResolve");

                LoadOneMaterial(effects, out WorldSpaceToneMappedLightingResolve,
                    "Resolve", "WorldSpaceToneMappedLightingResolve");

                LoadOneMaterial(effects, out WorldSpaceLightingResolveWithAlbedo,
                    "Resolve", "WorldSpaceLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out WorldSpaceGammaCompressedLightingResolveWithAlbedo,
                    "Resolve", "WorldSpaceGammaCompressedLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out WorldSpaceToneMappedLightingResolveWithAlbedo,
                    "Resolve", "WorldSpaceToneMappedLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out WorldSpaceLUTBlendedLightingResolveWithAlbedo,
                    "LUTResolve", "WorldSpaceLUTBlendedLightingResolveWithAlbedo");

                LoadOneMaterial(effects, out CalculateLuminance,
                    "Resolve", "CalculateLuminance");

                LoadOneMaterial(effects, out ObjectSurfaces,
                    "VisualizeDistanceField", "ObjectSurfaces");

                LoadOneMaterial(effects, out ObjectOutlines,
                    "VisualizeDistanceField", "ObjectOutlines");

                LoadOneMaterial(effects, out FunctionSurface,
                    "VisualizeDistanceFunction", "FunctionSurface");

                LoadOneMaterial(effects, out FunctionOutline,
                    "VisualizeDistanceFunction", "FunctionOutline");

                LoadOneMaterial(effects, out ScreenSpaceGammaCompressedBitmap,
                    "HDRBitmap", "ScreenSpaceGammaCompressedBitmap"
                );

                LoadOneMaterial(effects, out WorldSpaceGammaCompressedBitmap,
                    "HDRBitmap", "WorldSpaceGammaCompressedBitmap"
                );

                LoadOneMaterial(effects, out ScreenSpaceToneMappedBitmap,
                    "HDRBitmap", "ScreenSpaceToneMappedBitmap"
                );

                LoadOneMaterial(effects, out WorldSpaceToneMappedBitmap,
                    "HDRBitmap", "WorldSpaceToneMappedBitmap"
                );

                LoadOneMaterial(effects, out ScreenSpaceVectorWarp,
                    
                        "VectorWarp", "ScreenSpaceVectorWarp", 
                        new [] { MaterialUtil.MakeDelegate(BlendState.AlphaBlend) }
                    );
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

                var countBlendState = new BlendState {
                    AlphaBlendFunction = BlendFunction.Add,
                    ColorBlendFunction = BlendFunction.Add,
                    AlphaDestinationBlend = Blend.One,
                    ColorDestinationBlend = Blend.One,
                    AlphaSourceBlend = Blend.One,
                    ColorSourceBlend = Blend.One,
                };

                ParticleMaterials.CountDepthStencilState = new DepthStencilState {
                    StencilEnable = false,
                    DepthBufferEnable = true,
                    DepthBufferWriteEnable = true,
                    DepthBufferFunction = CompareFunction.Greater
                };

                LoadOneMaterial(out ParticleMaterials.CountLiveParticles,
                    "CountLiveParticles", "CountLiveParticles", new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: DepthStencilState.None,
                            blendState: countBlendState
                        )
                    }, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.CountLiveParticlesFast,
                    "CountLiveParticles", "CountLiveParticles", new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: ParticleMaterials.CountDepthStencilState,
                            blendState: BlendState.Opaque
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
                    "RasterizeParticleSystem", "RasterizeParticlesTextureLinear"
                );
                LoadOneMaterial(out ParticleMaterials.TexturePoint,
                    "RasterizeParticleSystem", "RasterizeParticlesTexturePoint"
                );
                LoadOneMaterial(out ParticleMaterials.NoTexture,
                    "RasterizeParticleSystem", "RasterizeParticlesNoTexture"
                );

                ParticleMaterials.IsLoaded = true;
            }
        }
    }
}
