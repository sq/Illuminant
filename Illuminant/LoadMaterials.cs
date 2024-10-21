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
using Squared.Render.Resources;
using Squared.Render.Tracing;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
    }

    public partial class IlluminantMaterials {
        private void DefineMaterial (Material m, bool addToList) {
            MaterialSet.Add(m, addToList);
        }

        private Material LoadOneMaterial (
            EffectProvider effects, out Material result, string fileName, string techniqueName = null, 
            Action<DeviceManager>[] begin = null, Action<DeviceManager>[] end = null, bool addToList = false
        ) {
            try {
                if (techniqueName == null)
                    techniqueName = fileName;

                var m = new Material(
                    effects.Load(fileName), techniqueName,
                    begin, end
                );
                result = m;
                DefineMaterial(m, addToList);
                return m;
            } catch (Exception exc) {
                result = null;
                Console.WriteLine("Failed to load shader {0} technique {1}: {2}", fileName, techniqueName, exc);
                return null;
            }
        }

        internal void Load (RenderCoordinator coordinator, EffectProvider effects = null) {
            lock (this) {
                if (IsLoaded)
                    return;

                IsLoaded = true;
                // FIXME: This is a memory leak
                if (effects == null)
                    effects = new EffectProvider(System.Reflection.Assembly.GetExecutingAssembly(), coordinator);
                
                var neutralDepthStencilState = new DepthStencilState {
                    StencilEnable = false,
                    DepthBufferEnable = false
                };

                // FIXME: Stop using MakeDelegate
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        depthStencilState: neutralDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                LoadOneMaterial(effects, out SphereLight,
                    "SphereLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightWithoutDistanceField,
                    "SphereLightWithoutDistanceField", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLight,
                    "DirectionalLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightWithoutDistanceField,
                    "DirectionalLightWithoutDistanceField", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out ParticleSystemSphereLight,
                    "ParticleLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out ParticleSystemSphereLightWithoutDistanceField,
                    "ParticleLight", "ParticleLightWithoutDistanceField", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLight,
                    "LineLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out VolumetricLight,
                    "VolumetricLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out ShadowedVolumetricLight,
                    "ShadowedVolumetricLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out ProjectorLight,
                    "ProjectorLight", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out ProjectorLightWithoutDistanceField,
                    "ProjectorLightWithoutDistanceField", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightWithDistanceRamp,
                    "SphereLight", "SphereLightWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightWithRamp,
                    "DirectionalLight", "DirectionalLightWithRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightProbe,
                    "SphereLightProbe", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out SphereLightProbeWithDistanceRamp,
                    "SphereLightProbe", "SphereLightProbeWithDistanceRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightProbe,
                    "DirectionalLight", "DirectionalLightProbe", dBegin, dEnd
                );

                LoadOneMaterial(effects, out DirectionalLightProbeWithRamp,
                    "DirectionalLight", "DirectionalLightProbeWithRamp", dBegin, dEnd
                );

                LoadOneMaterial(effects, out LineLightProbe,
                    "LineLightProbe", null, dBegin, dEnd
                );

                // FIXME
                /*
                LoadOneMaterial(effects, out VolumetricLightProbe,
                    "VolumetricLightProbe", null, dBegin, dEnd
                );
                */

                LoadOneMaterial(effects, out ProjectorLightProbe,
                    "ProjectorLightProbe", null, dBegin, dEnd
                );

                LoadOneMaterial(effects, out DistanceToPolygon, 
                    "DistanceField", "DistanceToPolygon",
                    new[] { MaterialUtil.MakeDelegate(blendState: RenderStates.MaxBlendValue) }
                );

                LoadOneMaterial(effects, out ClearDistanceFieldSlice,
                    "ClearDistanceField", null,
                    new[] { MaterialUtil.MakeDelegate(blendState: BlendState.Opaque) }
                );

                DistanceFunctionTypes = new Material[(int)LightObstruction.MAX_Type + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    LoadOneMaterial(effects, out DistanceFunctionTypes[(short)i],
                        "DistanceFunction", name,
                            new[] { MaterialUtil.MakeDelegate(blendState: RenderStates.MaxBlendValue) },
                        addToList: true
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

                LoadOneMaterial(effects, out AutoGBufferBitmap,
                    "AutoGBufferBitmap", "AutoGBufferBitmap"
                );

                LoadOneMaterial(effects, out NormalBillboard,
                    "AutoGBufferBitmap", "NormalBillboard");

                LoadOneMaterial(effects, out DistanceBillboard,
                    "AutoGBufferBitmap", "DistanceBillboard");

                LoadOneMaterial(effects, out GBufferMask,
                    "GBufferMask", "GBufferMask"
                );

                LoadOneMaterial(effects, out ScreenSpaceVectorWarp,
                    "VectorWarp", "ScreenSpaceVectorWarp", 
                    new [] { MaterialUtil.MakeDelegate(blendState: BlendState.AlphaBlend) }
                );

                LoadOneMaterial(effects, out ScreenSpaceNormalRefraction,
                    "VectorWarp", "ScreenSpaceNormalRefraction"
                );

                LoadOneMaterial(effects, out ScreenSpaceHeightmapRefraction,
                    "VectorWarp", "ScreenSpaceHeightmapRefraction"
                );

                LoadOneMaterial(effects, out HeightmapToNormals,
                    "ProcessHeightmap", "HeightmapToNormals"
                );

                LoadOneMaterial(effects, out HeightmapToDisplacement,
                    "ProcessHeightmap", "HeightmapToDisplacement"
                );

                LoadOneMaterial(effects, out HeightFromDistance,
                    "ProcessHeightmap", "HeightFromDistance"
                );

                LoadOneMaterial(effects, out NormalsFromLightmaps,
                    "ProcessNormals", "NormalsFromLightmaps"
                );
            }
        }
    }
}

namespace Squared.Illuminant.Particles {
    public sealed partial class ParticleEngine : IDisposable {
        private void DefineMaterial (Material m, bool addToList) {
            Materials.Add(m, addToList);
        }

        private Material LoadOneMaterial (out Material result, string fileName, string techniqueName, Action<DeviceManager>[] begin = null, Action<DeviceManager>[] end = null, bool addToList = false) {
            var effect = Effects.Load(fileName);
            if (effect == null)
                throw new Exception("Failed to load shader " + fileName);
            try {
                var m = new Material(
                    effect, techniqueName,
                    begin, end
                );
                result = m;
                DefineMaterial(m, addToList);
                if (result == null)
                    Console.WriteLine("Failed to load shader {0} technique {1}", fileName, techniqueName);
                return m;
            } catch (Exception exc) {
                result = null;
                Console.WriteLine("Failed to load shader {0} technique {1}: {2}", fileName, techniqueName, exc);
                return null;
            }
        }

        private void LoadMaterials (EffectProvider effects) {
            lock (ParticleMaterials) {
                if (ParticleMaterials.IsLoaded)
                    return;

                // FIXME: Stop using MakeDelegate
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
                    "CountLiveParticles", null, new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: DepthStencilState.None,
                            blendState: countBlendState
                        )
                    }, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.CountLiveParticlesFast,
                    "CountLiveParticles", null, new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: ParticleMaterials.CountDepthStencilState,
                            blendState: BlendState.Opaque
                        )
                    }, dEnd
                );

                LoadOneMaterial(out ParticleMaterials.CollectParticles,
                    "CollectParticles", null, new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: DepthStencilState.None,
                            blendState: noopBlendState
                        )
                    }, dEnd
                );

                var updateHint = new Material.PipelineHint {
                    HasIndices = true,
                    VertexFormats = new Type[] {
                        typeof(ParticleSystemVertex),
                    },
                    VertexTextureFormats = new SurfaceFormat[] {
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4
                    }
                };

                LoadOneMaterial(out ParticleMaterials.Erase,
                    "UpdateParticleSystem", "Erase", dBegin, dEnd
                ).HintPipeline = updateHint;

                LoadOneMaterial(out ParticleMaterials.UpdatePositions,
                    "UpdateParticleSystem", "UpdatePositions", dBegin, dEnd
                ).HintPipeline = updateHint;

                LoadOneMaterial(out ParticleMaterials.UpdateWithDistanceField,
                    "UpdateParticleSystemWithDistanceField", "UpdateWithDistanceField", dBegin, dEnd
                ).HintPipeline = updateHint;

                var transformHint = new Material.PipelineHint {
                    HasIndices = true,
                    VertexFormats = new Type[] {
                        typeof(ParticleSystemVertex),
                    },
                    VertexTextureFormats = new SurfaceFormat[] {
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4
                    }
                };

                LoadOneMaterial(out ParticleMaterials.FMA,
                    "FMA", null, dBegin, dEnd
                ).HintPipeline = transformHint;

                LoadOneMaterial(out ParticleMaterials.MatrixMultiply,
                    "MatrixMultiply", null, dBegin, dEnd
                ).HintPipeline = transformHint;

                LoadOneMaterial(out ParticleMaterials.Noise,
                    "Noise", null, dBegin, dEnd
                ).HintPipeline = transformHint;

                LoadOneMaterial(out ParticleMaterials.SpatialNoise,
                    "Noise", "SpatialNoise", dBegin, dEnd
                ).HintPipeline = transformHint;

                LoadOneMaterial(out ParticleMaterials.Gravity,
                    "Gravity", null, dBegin, dEnd
                ).HintPipeline = transformHint;

                var spawnHint = new Material.PipelineHint {
                    HasIndices = true,
                    VertexFormats = new Type[] {
                        typeof(ParticleSystemVertex),
                    },
                    VertexTextureFormats = new SurfaceFormat[] {
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4
                    }
                };

                LoadOneMaterial(out ParticleMaterials.Spawn,
                    "SpawnParticles", null, dBegin, dEnd
                ).HintPipeline = spawnHint;

                LoadOneMaterial(out ParticleMaterials.SpawnFromPositionTexture,
                    "SpawnParticles", "SpawnParticlesFromPositionTexture", dBegin, dEnd
                ).HintPipeline = spawnHint;

                LoadOneMaterial(out ParticleMaterials.SpawnFeedback,
                    "SpawnParticles", "SpawnFeedbackParticles", dBegin, dEnd
                ).HintPipeline = spawnHint;

                LoadOneMaterial(out ParticleMaterials.SpawnPattern,
                    "PatternSpawner", "SpawnPatternParticles", dBegin, dEnd
                ).HintPipeline = spawnHint;

                var rasterizeHint = new Material.PipelineHint {
                    HasIndices = true,
                    VertexFormats = new Type[] {
                        typeof(ParticleSystemVertex),
                        typeof(ParticleOffsetVertex)
                    },
                    VertexTextureFormats = new SurfaceFormat[] {
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4,
                        SurfaceFormat.Vector4
                    }
                };
                
                LoadOneMaterial(out ParticleMaterials.TextureLinear,
                    "RasterizeParticleSystem", "RasterizeParticlesTextureLinear"
                ).HintPipeline = rasterizeHint;
                LoadOneMaterial(out ParticleMaterials.TexturePoint,
                    "RasterizeParticleSystem", "RasterizeParticlesTexturePoint"
                ).HintPipeline = rasterizeHint;
                LoadOneMaterial(out ParticleMaterials.NoTexture,
                    "RasterizeParticleSystem", "RasterizeParticlesNoTexture"
                ).HintPipeline = rasterizeHint;

                ParticleMaterials.IsLoaded = true;
            }
        }
    }
}
