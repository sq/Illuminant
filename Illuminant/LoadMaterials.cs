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

                DefineMaterial(IlluminantMaterials.SphereLight = new Material(
                    effects.Load("SphereLight"), "SphereLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLight = new Material(
                    effects.Load("DirectionalLight"), "DirectionalLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.ParticleSystemSphereLight = new Material(
                    effects.Load("ParticleLight"), "ParticleLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLight = new Material(
                    effects.Load("LineLight"), "LineLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithDistanceRamp = new Material(
                    effects.Load("SphereLight"), "SphereLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithOpacityRamp = new Material(
                    effects.Load("SphereLight"), "SphereLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightWithRamp = new Material(
                    effects.Load("DirectionalLight"), "DirectionalLightWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightWithDistanceRamp = new Material(
                    effects.Load("LineLight"), "LineLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightWithOpacityRamp = new Material(
                    effects.Load("LineLight"), "LineLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbe = new Material(
                    effects.Load("SphereLightProbe"), "SphereLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbeWithDistanceRamp = new Material(
                    effects.Load("SphereLightProbe"), "SphereLightProbeWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbeWithOpacityRamp = new Material(
                    effects.Load("SphereLightProbe"), "SphereLightProbeWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbe = new Material(
                    effects.Load("DirectionalLight"), "DirectionalLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbeWithRamp = new Material(
                    effects.Load("DirectionalLight"), "DirectionalLightProbeWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbe = new Material(
                    effects.Load("LineLightProbe"), "LineLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbeWithDistanceRamp = new Material(
                    effects.Load("LineLightProbe"), "LineLightProbeWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbeWithOpacityRamp = new Material(
                    effects.Load("LineLightProbe"), "LineLightProbeWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.GIProbeSelector = new Material(
                    effects.Load("GIProbe"), "ProbeSelector", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.GIProbeSHGenerator = new Material(
                    effects.Load("GIProbe"), "SHGenerator", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.VisualizeGI = new Material(
                    effects.Load("GI"), "VisualizeGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.RenderGI = new Material(
                    effects.Load("GI"), "RenderGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.RenderLightProbesFromGI = new Material(
                    effects.Load("GI"), "RenderLightProbesFromGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DistanceToPolygon = 
                    new Material(
                        effects.Load("DistanceField"), "DistanceToPolygon",
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                DefineMaterial(IlluminantMaterials.ClearDistanceFieldSlice =
                    new Material(
                        effects.Load("ClearDistanceField"), "Clear",
                        new[] { MaterialUtil.MakeDelegate(BlendState.Opaque) }
                    )
                );

                IlluminantMaterials.DistanceFunctionTypes = new Render.Material[(int)LightObstructionType.MAX + 1];

                foreach (var i in Enum.GetValues(typeof(LightObstructionType))) {
                    var name = Enum.GetName(typeof(LightObstructionType), i);
                    if (name == "MAX")
                        continue;

                    DefineMaterial(
                        IlluminantMaterials.DistanceFunctionTypes[(int)i] = 
                        new Material(
                            effects.Load("DistanceFunction"), name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                        )
                    );
                }

                DefineMaterial(IlluminantMaterials.GroundPlane = 
                    new Squared.Render.Material(effects.Load("GBuffer"), "GroundPlane"));

                DefineMaterial(IlluminantMaterials.HeightVolume = 
                    new Squared.Render.Material(effects.Load("GBuffer"), "HeightVolume"));

                DefineMaterial(IlluminantMaterials.HeightVolumeFace = 
                    new Squared.Render.Material(effects.Load("GBuffer"), "HeightVolumeFace"));

                DefineMaterial(IlluminantMaterials.MaskBillboard = 
                    new Squared.Render.Material(effects.Load("GBufferBitmap"), "MaskBillboard"));

                DefineMaterial(IlluminantMaterials.GDataBillboard = 
                    new Squared.Render.Material(effects.Load("GBufferBitmap"), "GDataBillboard"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLightingResolve = 
                    new Squared.Render.Material(effects.Load("Resolve"), "ScreenSpaceLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolve = 
                    new Squared.Render.Material(effects.Load("Resolve"), "ScreenSpaceGammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedLightingResolve = 
                    new Squared.Render.Material(effects.Load("Resolve"), "ScreenSpaceToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("Resolve"), "ScreenSpaceLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("Resolve"), "ScreenSpaceGammaCompressedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("Resolve"), "ScreenSpaceToneMappedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLUTBlendedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("LUTResolve"), "ScreenSpaceLUTBlendedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLightingResolve = 
                    new Squared.Render.Material(effects.Load("Resolve"), "WorldSpaceLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedLightingResolve = 
                    new Squared.Render.Material(effects.Load("Resolve"), "WorldSpaceGammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedLightingResolve = 
                    new Squared.Render.Material(effects.Load("Resolve"), "WorldSpaceToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("Resolve"), "WorldSpaceLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("Resolve"), "WorldSpaceGammaCompressedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("Resolve"), "WorldSpaceToneMappedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLUTBlendedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(effects.Load("LUTResolve"), "WorldSpaceLUTBlendedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.CalculateLuminance = 
                    new Squared.Render.Material(effects.Load("Resolve"), "CalculateLuminance"));

                DefineMaterial(IlluminantMaterials.ObjectSurfaces = 
                    new Squared.Render.Material(effects.Load("VisualizeDistanceField"), "ObjectSurfaces"));

                DefineMaterial(IlluminantMaterials.ObjectOutlines = 
                    new Squared.Render.Material(effects.Load("VisualizeDistanceField"), "ObjectOutlines"));

                DefineMaterial(IlluminantMaterials.FunctionSurface = 
                    new Squared.Render.Material(effects.Load("VisualizeDistanceFunction"), "FunctionSurface"));

                DefineMaterial(IlluminantMaterials.FunctionOutline = 
                    new Squared.Render.Material(effects.Load("VisualizeDistanceFunction"), "FunctionOutline"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.Material(
                    effects.Load("HDRBitmap"), "ScreenSpaceGammaCompressedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.Material(
                    effects.Load("HDRBitmap"), "WorldSpaceGammaCompressedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.Material(
                    effects.Load("HDRBitmap"), "ScreenSpaceToneMappedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.Material(
                    effects.Load("HDRBitmap"), "WorldSpaceToneMappedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.ScreenSpaceVectorWarp = 
                    new Squared.Render.Material(
                        effects.Load("VectorWarp"), "ScreenSpaceVectorWarp", 
                        new [] { MaterialUtil.MakeDelegate(BlendState.AlphaBlend) }
                    ));

                Materials.PreallocateBindings();

                Materials.ForEachMaterial<object>((m, _) => {
                    Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                    Materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
                }, null);

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

                DefineMaterial(ParticleMaterials.CountLiveParticles = new Material(
                    effects.Load("CountLiveParticles"), "CountLiveParticles", new[] {
                        MaterialUtil.MakeDelegate(
                            rasterizerState: RasterizerState.CullNone,
                            depthStencilState: DepthStencilState.None,
                            blendState: noopBlendState
                        )
                    }, dEnd
                ));

                DefineMaterial(ParticleMaterials.Erase = new Material(
                    effects.Load("UpdateParticleSystem"), "Erase", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.UpdatePositions = new Material(
                    effects.Load("UpdateParticleSystem"), "UpdatePositions", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.UpdateWithDistanceField = new Material(
                    effects.Load("UpdateParticleSystemWithDistanceField"), "UpdateWithDistanceField", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.FMA = new Material(
                    effects.Load("FMA"), "FMA", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.MatrixMultiply = new Material(
                    effects.Load("MatrixMultiply"), "MatrixMultiply", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Noise = new Material(
                    effects.Load("Noise"), "Noise", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Gravity = new Material(
                    effects.Load("Gravity"), "Gravity", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Spawn = new Material(
                    effects.Load("SpawnParticles"), "SpawnParticles", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.SpawnFeedback = new Material(
                    effects.Load("SpawnParticles"), "SpawnFeedbackParticles", dBegin, dEnd
                ));
                
                DefineMaterial(ParticleMaterials.AttributeColor = new Material(
                    effects.Load("RasterizeParticleSystem"), "AttributeColor"
                ));
                DefineMaterial(ParticleMaterials.AttributeColorNoTexture = new Material(
                    effects.Load("RasterizeParticleSystem"), "AttributeColorNoTexture"
                ));

                Materials.ForEachMaterial<object>((m, _) => {
                    Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                    Materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
                    Materials.GetUniformBinding<Uniforms.ParticleSystem>(m, "System");
                    Materials.GetUniformBinding<Uniforms.ClampedBezier4>(m, "ColorFromLife");
                    Materials.GetUniformBinding<Uniforms.ClampedBezier4>(m, "ColorFromVelocity");
                    Materials.GetUniformBinding<Uniforms.ClampedBezier2>(m, "SizeFromLife");
                    Materials.GetUniformBinding<Uniforms.ClampedBezier2>(m, "SizeFromVelocity");
                }, null);

                Materials.PreallocateBindings();

                ParticleMaterials.IsLoaded = true;
            }
        }
    }
}
