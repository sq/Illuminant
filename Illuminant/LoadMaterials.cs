using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private void DefineMaterial (Material m) {
            Materials.Add(m);
        }

        private void LoadMaterials (ContentManager content) {
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
                    content.Load<Effect>("SphereLight"), "SphereLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLight = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.ParticleSystemSphereLight = new Material(
                    content.Load<Effect>("ParticleLight"), "ParticleLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLight = new Material(
                    content.Load<Effect>("LineLight"), "LineLight", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithDistanceRamp = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightWithOpacityRamp = new Material(
                    content.Load<Effect>("SphereLight"), "SphereLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightWithRamp = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLightWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightWithDistanceRamp = new Material(
                    content.Load<Effect>("LineLight"), "LineLightWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightWithOpacityRamp = new Material(
                    content.Load<Effect>("LineLight"), "LineLightWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbe = new Material(
                    content.Load<Effect>("SphereLightProbe"), "SphereLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbeWithDistanceRamp = new Material(
                    content.Load<Effect>("SphereLightProbe"), "SphereLightProbeWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.SphereLightProbeWithOpacityRamp = new Material(
                    content.Load<Effect>("SphereLightProbe"), "SphereLightProbeWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbe = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DirectionalLightProbeWithRamp = new Material(
                    content.Load<Effect>("DirectionalLight"), "DirectionalLightProbeWithRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbe = new Material(
                    content.Load<Effect>("LineLightProbe"), "LineLightProbe", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbeWithDistanceRamp = new Material(
                    content.Load<Effect>("LineLightProbe"), "LineLightProbeWithDistanceRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.LineLightProbeWithOpacityRamp = new Material(
                    content.Load<Effect>("LineLightProbe"), "LineLightProbeWithOpacityRamp", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.GIProbeSelector = new Material(
                    content.Load<Effect>("GIProbe"), "ProbeSelector", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.GIProbeSHGenerator = new Material(
                    content.Load<Effect>("GIProbe"), "SHGenerator", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.VisualizeGI = new Material(
                    content.Load<Effect>("GI"), "VisualizeGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.RenderGI = new Material(
                    content.Load<Effect>("GI"), "RenderGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.RenderLightProbesFromGI = new Material(
                    content.Load<Effect>("GI"), "RenderLightProbesFromGI", dBegin, dEnd
                ));

                DefineMaterial(IlluminantMaterials.DistanceToPolygon = 
                    new Material(
                        content.Load<Effect>("DistanceField"), "DistanceToPolygon",
                        new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                    )
                );

                DefineMaterial(IlluminantMaterials.ClearDistanceFieldSlice =
                    new Material(
                        content.Load<Effect>("ClearDistanceField"), "Clear",
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
                            content.Load<Effect>("DistanceFunction"), name,
                            new[] { MaterialUtil.MakeDelegate(RenderStates.MaxBlendValue) }
                        )
                    );
                }

                DefineMaterial(IlluminantMaterials.GroundPlane = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "GroundPlane"));

                DefineMaterial(IlluminantMaterials.HeightVolume = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "HeightVolume"));

                DefineMaterial(IlluminantMaterials.HeightVolumeFace = 
                    new Squared.Render.Material(content.Load<Effect>("GBuffer"), "HeightVolumeFace"));

                DefineMaterial(IlluminantMaterials.MaskBillboard = 
                    new Squared.Render.Material(content.Load<Effect>("GBufferBitmap"), "MaskBillboard"));

                DefineMaterial(IlluminantMaterials.GDataBillboard = 
                    new Squared.Render.Material(content.Load<Effect>("GBufferBitmap"), "GDataBillboard"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ScreenSpaceLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ScreenSpaceGammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ScreenSpaceToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ScreenSpaceLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ScreenSpaceGammaCompressedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "ScreenSpaceToneMappedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceLUTBlendedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("LUTResolve"), "ScreenSpaceLUTBlendedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "WorldSpaceLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "WorldSpaceGammaCompressedLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedLightingResolve = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "WorldSpaceToneMappedLightingResolve"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "WorldSpaceLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "WorldSpaceGammaCompressedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "WorldSpaceToneMappedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.WorldSpaceLUTBlendedLightingResolveWithAlbedo = 
                    new Squared.Render.Material(content.Load<Effect>("LUTResolve"), "WorldSpaceLUTBlendedLightingResolveWithAlbedo"));

                DefineMaterial(IlluminantMaterials.CalculateLuminance = 
                    new Squared.Render.Material(content.Load<Effect>("Resolve"), "CalculateLuminance"));

                DefineMaterial(IlluminantMaterials.ObjectSurfaces = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceField"), "ObjectSurfaces"));

                DefineMaterial(IlluminantMaterials.ObjectOutlines = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceField"), "ObjectOutlines"));

                DefineMaterial(IlluminantMaterials.FunctionSurface = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceFunction"), "FunctionSurface"));

                DefineMaterial(IlluminantMaterials.FunctionOutline = 
                    new Squared.Render.Material(content.Load<Effect>("VisualizeDistanceFunction"), "FunctionOutline"));

                DefineMaterial(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.Material(
                    content.Load<Effect>("HDRBitmap"), "ScreenSpaceGammaCompressedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.Material(
                    content.Load<Effect>("HDRBitmap"), "WorldSpaceGammaCompressedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.Material(
                    content.Load<Effect>("HDRBitmap"), "ScreenSpaceToneMappedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.Material(
                    content.Load<Effect>("HDRBitmap"), "WorldSpaceToneMappedBitmap"
                ));

                DefineMaterial(IlluminantMaterials.ScreenSpaceVectorWarp = 
                    new Squared.Render.Material(
                        content.Load<Effect>("VectorWarp"), "ScreenSpaceVectorWarp", 
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

        private void LoadMaterials (ContentManager content) {
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

                DefineMaterial(ParticleMaterials.UpdatePositions = new Material(
                    content.Load<Effect>("UpdateParticleSystem"), "UpdatePositions", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.UpdateWithDistanceField = new Material(
                    content.Load<Effect>("UpdateParticleSystemWithDistanceField"), "UpdateWithDistanceField", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.FMA = new Material(
                    content.Load<Effect>("FMA"), "FMA", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.MatrixMultiply = new Material(
                    content.Load<Effect>("MatrixMultiply"), "MatrixMultiply", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Gravity = new Material(
                    content.Load<Effect>("Gravity"), "Gravity", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.Spawn = new Material(
                    content.Load<Effect>("SpawnParticles"), "SpawnParticles", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.NullTransform = new Material(
                    content.Load<Effect>("NullTransform"), "NullTransform", dBegin, dEnd
                ));

                DefineMaterial(ParticleMaterials.White = new Material(
                    content.Load<Effect>("RasterizeParticleSystem"), "White"
                ));
                DefineMaterial(ParticleMaterials.AttributeColor = new Material(
                    content.Load<Effect>("RasterizeParticleSystem"), "AttributeColor"
                ));

                Materials.ForEachMaterial<object>((m, _) => {
                    Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                    Materials.GetUniformBinding<Uniforms.DistanceField>(m, "DistanceField");
                    Materials.GetUniformBinding<Uniforms.ParticleSystem>(m, "System");
                }, null);

                Materials.PreallocateBindings();

                ParticleMaterials.IsLoaded = true;
            }
        }
    }
}
